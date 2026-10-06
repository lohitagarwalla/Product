using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Entities;
using ResourceBooking.Core.Enums;
using ResourceBooking.Infrastructure.Data;
using ResourceBooking.Infrastructure.Services;
using ResourceBooking.Infrastructure.Storage;

namespace ResourceBooking.IntegrationTests;

// A dedicated, disposable SQL Server database verifies real rowversion, constraints,
// migrations, and transaction behavior. It never connects to the app's database.
public class OrderWebApplicationFactory : WebApplicationFactory<Program>
{
    private readonly string databaseName = $"ResourceBookingOrderTests_{Guid.NewGuid():N}";
    public string ConnectionString { get; }

    public OrderWebApplicationFactory()
    {
        var connection = new SqlConnectionStringBuilder(
            Environment.GetEnvironmentVariable("ORDER_TEST_SQLSERVER_CONNECTION") ??
            @"Server=(localdb)\MSSQLLocalDB;Integrated Security=true;TrustServerCertificate=true")
        {
            InitialCatalog = databaseName,
            AttachDBFilename = string.Empty
        };
        ConnectionString = connection.ConnectionString;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<ApplicationDbContext>>();
            services.RemoveAll<IDbContextOptionsConfiguration<ApplicationDbContext>>();
            services.AddDbContext<ApplicationDbContext>(options => options.UseSqlServer(ConnectionString));
            services.PostConfigure<ImageStorageOptions>(options => options.CleanupEnabled = false);
        });
    }

    public ApplicationDbContext CreateDb(SaveChangesInterceptor? interceptor = null)
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>().UseSqlServer(ConnectionString);
        if (interceptor is not null) options.AddInterceptors(interceptor);
        return new ApplicationDbContext(options.Options);
    }

    public override async ValueTask DisposeAsync()
    {
        await base.DisposeAsync();
        // Delete only the unique database created by this factory.
        if (new SqlConnectionStringBuilder(ConnectionString).InitialCatalog != databaseName ||
            !databaseName.StartsWith("ResourceBookingOrderTests_", StringComparison.Ordinal))
            throw new InvalidOperationException("Unexpected test database name.");
        await using var db = CreateDb();
        await db.Database.EnsureDeletedAsync();
    }
}

public class OrderEndpointsTests(OrderWebApplicationFactory factory) : IClassFixture<OrderWebApplicationFactory>
{
    private async Task<HttpClient> UserAsync(bool admin = false)
    {
        var client = factory.CreateClient();
        var response = admin
            ? await client.PostAsJsonAsync("/api/auth/login", new LoginDto { Email = "admin@company.com", Password = "Admin123!" })
            : await client.PostAsJsonAsync("/api/auth/register", new RegisterDto
            {
                Email = $"{Guid.NewGuid():N}@example.com", Password = "Password123!", ConfirmPassword = "Password123!",
                FirstName = "Order", LastName = "Customer"
            });
        response.EnsureSuccessStatusCode();
        var auth = (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        return client;
    }

    private static async Task<ProductResponseDto> ProductAsync(HttpClient admin, decimal price = 123.45m,
        string currency = "INR", bool published = true, string? title = null)
    {
        var response = await admin.PostAsJsonAsync("/api/products", new ProductWriteDto
        { Title = title ?? $"Order product {Guid.NewGuid():N}", Brand = "Example", Price = price, Currency = currency, IsPublished = published });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponseDto>())!;
    }

    private static OrderCreateDto Request(params (int ProductId, int Quantity)[] items) => new()
    {
        RequestId = Guid.NewGuid(),
        DeliveryAddress = new DeliveryAddressWriteDto { RecipientName = "Order Customer", AddressLine1 = "1 Main Street", City = "Bengaluru", State = "Karnataka", PostalCode = "560001", CountryCode = "IN" },
        Items = items.Select(i => new OrderItemCreateDto { ProductId = i.ProductId, Quantity = i.Quantity }).ToList()
    };

    private static async Task<OrderResponseDto> CreateAsync(HttpClient user, OrderCreateDto request)
    {
        var response = await user.PostAsJsonAsync("/api/orders", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);
        Assert.True(response.Headers.CacheControl?.NoStore);
        return (await response.Content.ReadFromJsonAsync<OrderResponseDto>())!;
    }

    private static async Task<OrderResponseDto> ChangeAsync(HttpClient client, OrderResponseDto order, string action, string? reason = null)
    {
        var response = await client.PostAsJsonAsync($"/api/orders/{order.Id}/{action}", new { order.RowVersion, reason });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<OrderResponseDto>())!;
    }

    [Fact]
    public async Task Create_CalculatesTotals_SnapshotsProducts_AndRecordsInitialHistory()
    {
        using var admin = await UserAsync(true);
        using var user = await UserAsync();
        var first = await ProductAsync(admin);
        var second = await ProductAsync(admin, 10m);
        var order = await CreateAsync(user, Request((first.Id, 2), (second.Id, 3)));
        Assert.Equal(276.90m, order.TotalAmount);
        Assert.Equal(OrderStatus.Placed, order.Status);
        Assert.Equal(8, order.RowVersion.Length);
        Assert.Equal(DateTimeKind.Utc, order.CreatedAt.Kind);
        var history = Assert.Single(order.StatusHistory);
        Assert.Null(history.PreviousStatus);
        Assert.Equal(OrderStatus.Placed, history.NewStatus);
        Assert.Equal(order.UserId, history.ChangedByUserId);
        Assert.Null(history.Reason);

        (await admin.PutAsJsonAsync($"/api/products/{first.Id}", new ProductWriteDto
        { Title = "Changed title", Brand = "Changed", Price = 999, IsPublished = true })).EnsureSuccessStatusCode();
        (await admin.DeleteAsync($"/api/products/{second.Id}")).EnsureSuccessStatusCode();
        var saved = (await user.GetFromJsonAsync<OrderResponseDto>($"/api/orders/{order.Id}"))!;
        Assert.Equal(276.90m, saved.TotalAmount);
        Assert.Equal(first.Title, saved.Items.Single(i => i.ProductId == first.Id).ProductTitle);
        Assert.Equal(123.45m, saved.Items.Single(i => i.ProductId == first.Id).UnitPrice);
        Assert.Equal(2, saved.Items.Count);
        Assert.Null(saved.Items.Single(i => i.ProductId == second.Id).ImageUrl);
        Assert.Single((await user.GetFromJsonAsync<OrderPageDto>($"/api/orders?search={Uri.EscapeDataString(second.Title)}"))!.Items);
    }

    [Fact]
    public async Task Create_RetryReturnsSameOrder_EvenAfterCatalogDeletion_AndDifferentPayloadConflicts()
    {
        using var admin = await UserAsync(true);
        using var user = await UserAsync();
        var product = await ProductAsync(admin);
        var request = Request((product.Id, 1));
        var order = await CreateAsync(user, request);
        (await admin.DeleteAsync($"/api/products/{product.Id}")).EnsureSuccessStatusCode();
        var retry = await user.PostAsJsonAsync("/api/orders", request);
        Assert.Equal(HttpStatusCode.OK, retry.StatusCode);
        var replay = (await retry.Content.ReadFromJsonAsync<OrderResponseDto>())!;
        Assert.Equal(order.Id, replay.Id);
        Assert.Single(replay.StatusHistory);
        request.Items[0].Quantity = 2;
        Assert.Equal(HttpStatusCode.Conflict, (await user.PostAsJsonAsync("/api/orders", request)).StatusCode);
        await using var db = factory.CreateDb();
        Assert.Equal(1, await db.Orders.CountAsync(o => o.UserId == order.UserId && o.RequestId == request.RequestId));
    }

    [Fact]
    public async Task Creation_IgnoresClientOwnershipPricesAndStatus_AndScopesRequestKeysToUser()
    {
        using var admin = await UserAsync(true);
        using var owner = await UserAsync();
        using var other = await UserAsync();
        var product = await ProductAsync(admin, price: 25m);
        var requestId = Guid.NewGuid();
        var response = await owner.PostAsJsonAsync("/api/orders", new
        {
            deliveryAddress = new DeliveryAddressWriteDto { RecipientName = "Order Customer", AddressLine1 = "1 Main Street", City = "Bengaluru", State = "Karnataka", PostalCode = "560001", CountryCode = "IN" },
            requestId, userId = "forged-owner", totalAmount = 0m, currency = "USD", status = "Delivered",
            items = new[] { new { productId = product.Id, quantity = 2, unitPrice = 0m, lineTotal = 0m } }
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<OrderResponseDto>())!;
        Assert.NotEqual("forged-owner", order.UserId);
        Assert.Equal(50m, order.TotalAmount);
        Assert.Equal("INR", order.Currency);
        Assert.Equal(OrderStatus.Placed, order.Status);
        var otherOrder = await CreateAsync(other, new OrderCreateDto
        { DeliveryAddress = new DeliveryAddressWriteDto { RecipientName = "Order Customer", AddressLine1 = "1 Main Street", City = "Bengaluru", State = "Karnataka", PostalCode = "560001", CountryCode = "IN" }, RequestId = requestId, Items = [new() { ProductId = product.Id, Quantity = 2 }] });
        Assert.NotEqual(order.Id, otherOrder.Id);
        Assert.NotEqual(order.UserId, otherOrder.UserId);
    }

    [Fact]
    public async Task HistoryConstraint_RejectsInvalidInitialTransition()
    {
        using var admin = await UserAsync(true);
        using var owner = await UserAsync();
        var product = await ProductAsync(admin);
        var order = await CreateAsync(owner, Request((product.Id, 1)));
        await using var db = factory.CreateDb();
        db.OrderStatusHistory.Add(new OrderStatusHistory
        {
            OrderId = order.Id, PreviousStatus = null, NewStatus = OrderStatus.Delivered,
            ChangedByUserId = order.UserId, ChangedAtUtc = DateTime.UtcNow
        });
        await Assert.ThrowsAsync<DbUpdateException>(() => db.SaveChangesAsync());
        Assert.Single((await owner.GetFromJsonAsync<OrderResponseDto>($"/api/orders/{order.Id}"))!.StatusHistory);
    }

    [Fact]
    public async Task Permissions_ProtectAllEndpoints_AndIsolateOrders()
    {
        using var anonymous = factory.CreateClient();
        using var admin = await UserAsync(true);
        using var owner = await UserAsync();
        using var other = await UserAsync();
        var product = await ProductAsync(admin);
        var order = await CreateAsync(owner, Request((product.Id, 1)));
        foreach (var path in new[] { "/api/orders", "/api/orders/manage", $"/api/orders/{order.Id}" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.GetAsync(path)).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync("/api/orders", Request((product.Id, 1)))).StatusCode);
        foreach (var action in new[] { "cancel", "ship", "deliver" })
            Assert.Equal(HttpStatusCode.Unauthorized, (await anonymous.PostAsJsonAsync($"/api/orders/{order.Id}/{action}", new { order.RowVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.GetAsync($"/api/orders/{order.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", new { order.RowVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.Forbidden, (await owner.GetAsync("/api/orders/manage")).StatusCode);
        foreach (var action in new[] { "ship", "deliver" })
            Assert.Equal(HttpStatusCode.Forbidden, (await owner.PostAsJsonAsync($"/api/orders/{order.Id}/{action}", new { order.RowVersion })).StatusCode);
        Assert.Empty((await other.GetFromJsonAsync<OrderPageDto>("/api/orders"))!.Items);
        Assert.Empty((await admin.GetFromJsonAsync<OrderPageDto>($"/api/orders?search={order.OrderNumber}"))!.Items);
        Assert.Single((await admin.GetFromJsonAsync<OrderPageDto>($"/api/orders/manage?search={order.OrderNumber}"))!.Items);
        Assert.Equal(HttpStatusCode.OK, (await admin.GetAsync($"/api/orders/{order.Id}")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await owner.GetAsync("/api/orders/2147483647")).StatusCode);
    }

    [Fact]
    public async Task AdminShipsAndDelivers_DeliveredOrderCannotBeCancelledByAnyone()
    {
        using var admin = await UserAsync(true);
        using var owner = await UserAsync();
        var product = await ProductAsync(admin);
        var order = await CreateAsync(owner, Request((product.Id, 1)));
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/orders/{order.Id}/deliver", new { order.RowVersion })).StatusCode);
        var shipped = await ChangeAsync(admin, order, "ship");
        Assert.NotEqual(Convert.ToBase64String(order.RowVersion), Convert.ToBase64String(shipped.RowVersion));
        Assert.NotNull(shipped.ShippedAt);
        Assert.Equal(2, (await ChangeAsync(admin, order, "ship")).StatusHistory.Count);
        var delivered = await ChangeAsync(admin, shipped, "deliver");
        Assert.NotNull(delivered.DeliveredAt);
        Assert.Equal(OrderStatus.Delivered, delivered.Status);
        Assert.Equal(new[] { OrderStatus.Placed, OrderStatus.Shipped, OrderStatus.Delivered }, delivered.StatusHistory.Select(h => h.NewStatus));
        Assert.NotEqual(order.UserId, delivered.StatusHistory[1].ChangedByUserId);
        foreach (var client in new[] { owner, admin })
            Assert.Equal(HttpStatusCode.Conflict, (await client.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", new { delivered.RowVersion })).StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/orders/{order.Id}/ship", new { delivered.RowVersion })).StatusCode);
        Assert.Equal(3, (await owner.GetFromJsonAsync<OrderResponseDto>($"/api/orders/{order.Id}"))!.StatusHistory.Count);
    }

    [Theory]
    [InlineData(false, false, null)]
    [InlineData(false, true, "  Changed my mind  ")]
    [InlineData(true, false, null)]
    [InlineData(true, true, "  Shipment recalled  ")]
    public async Task OwnerAndAdminCanCancelPlacedOrShippedOrders(bool adminCancels, bool shipped, string? reason)
    {
        using var admin = await UserAsync(true);
        using var owner = await UserAsync();
        var product = await ProductAsync(admin);
        var order = await CreateAsync(owner, Request((product.Id, 1)));
        if (shipped) order = await ChangeAsync(admin, order, "ship");
        var cancelled = await ChangeAsync(adminCancels ? admin : owner, order, "cancel", reason);
        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        Assert.NotNull(cancelled.CancelledAt);
        Assert.Equal(reason?.Trim(), cancelled.CancellationReason);
        var history = cancelled.StatusHistory.Last();
        Assert.Equal(order.Status, history.PreviousStatus);
        Assert.Equal(cancelled.CancelledByUserId, history.ChangedByUserId);
        Assert.Equal(cancelled.CancellationReason, history.Reason);
        if (adminCancels) Assert.NotEqual(order.UserId, history.ChangedByUserId);
        else Assert.Equal(order.UserId, history.ChangedByUserId);
        var retry = await ChangeAsync(admin, order, "cancel", "Do not overwrite original reason");
        Assert.Equal(cancelled.CancelledByUserId, retry.CancelledByUserId);
        Assert.Equal(cancelled.CancelledAt, retry.CancelledAt);
        Assert.Equal(cancelled.CancellationReason, retry.CancellationReason);
        Assert.Equal(cancelled.StatusHistory.Count, retry.StatusHistory.Count);
        foreach (var action in new[] { "ship", "deliver" })
            Assert.Equal(HttpStatusCode.Conflict, (await admin.PostAsJsonAsync($"/api/orders/{order.Id}/{action}", new { cancelled.RowVersion })).StatusCode);
    }

    [Fact]
    public async Task Search_FiltersStatusDateAndSavedTitle_PaginatesAndValidatesQueries()
    {
        using var admin = await UserAsync(true);
        using var owner = await UserAsync();
        var product = await ProductAsync(admin);
        var first = await CreateAsync(owner, Request((product.Id, 1)));
        var second = await CreateAsync(owner, Request((product.Id, 2)));
        await ChangeAsync(owner, first, "cancel");
        var page = (await owner.GetFromJsonAsync<OrderPageDto>("/api/orders?pageSize=1&page=2"))!;
        Assert.Equal(2, page.TotalCount);
        Assert.Equal(first.Id, Assert.Single(page.Items).Id);
        var placed = (await owner.GetFromJsonAsync<OrderPageDto>("/api/orders?status=Placed"))!;
        Assert.Equal(second.Id, Assert.Single(placed.Items).Id);
        var from = Uri.EscapeDataString(first.CreatedAt.AddSeconds(-1).ToString("O"));
        var to = Uri.EscapeDataString(second.CreatedAt.AddSeconds(1).ToString("O"));
        Assert.Equal(2, (await owner.GetFromJsonAsync<OrderPageDto>($"/api/orders?fromUtc={from}&toUtc={to}&search={Uri.EscapeDataString(product.Title)}"))!.TotalCount);
        Assert.Empty((await owner.GetFromJsonAsync<OrderPageDto>($"/api/orders?fromUtc={to}"))!.Items);
        foreach (var query in new[] { "page=0", "pageSize=101", "status=999", "status=Unknown", "fromUtc=2026-02-01&toUtc=2026-01-01" })
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.GetAsync($"/api/orders?{query}")).StatusCode);
    }

    [Fact]
    public async Task InvalidOrders_RejectBadItemsUnavailableProductsMixedCurrenciesAndOverflow()
    {
        using var admin = await UserAsync(true);
        using var user = await UserAsync();
        var product = await ProductAsync(admin);
        var draft = await ProductAsync(admin, published: false);
        var deleted = await ProductAsync(admin);
        (await admin.DeleteAsync($"/api/products/{deleted.Id}")).EnsureSuccessStatusCode();
        var usd = await ProductAsync(admin, currency: "USD");
        var expensive = await ProductAsync(admin, price: 9999999999999999.99m);
        var requests = new[]
        {
            Request(), Request((product.Id, 0)), Request((product.Id, -1)), Request((product.Id, 1001)),
            Request((0, 1)), Request((int.MaxValue, 1)), Request((draft.Id, 1)), Request((deleted.Id, 1)),
            Request((product.Id, 1), (product.Id, 2)), Request((product.Id, 1), (usd.Id, 1)),
            Request((expensive.Id, 2)), Request((expensive.Id, 1), (product.Id, 1)),
            new OrderCreateDto { Items = [new() { ProductId = product.Id, Quantity = 1 }] },
            new OrderCreateDto { RequestId = Guid.NewGuid(), Items = null! },
            new OrderCreateDto { RequestId = Guid.NewGuid(), Items = [null!] },
            Request(Enumerable.Range(1, 101).Select(id => (id, 1)).ToArray())
        };
        foreach (var request in requests)
            Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsJsonAsync("/api/orders", request)).StatusCode);
        Assert.Empty((await user.GetFromJsonAsync<OrderPageDto>("/api/orders"))!.Items);
    }

    [Fact]
    public async Task StaleOrMalformedVersion_IsRejectedWithoutHistoryChanges()
    {
        using var admin = await UserAsync(true);
        using var owner = await UserAsync();
        var product = await ProductAsync(admin);
        var order = await CreateAsync(owner, Request((product.Id, 1)));
        var shipped = await ChangeAsync(admin, order, "ship");
        Assert.Equal(HttpStatusCode.Conflict, (await owner.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", new { order.RowVersion })).StatusCode);
        foreach (var body in new object[] { new { }, new { rowVersion = "invalid base64" }, new { rowVersion = "AQ==" }, new { rowVersion = (string?)null }, new { shipped.RowVersion, reason = new string('x', 1001) } })
            Assert.Equal(HttpStatusCode.BadRequest, (await owner.PostAsJsonAsync($"/api/orders/{order.Id}/cancel", body)).StatusCode);
        var saved = (await owner.GetFromJsonAsync<OrderResponseDto>($"/api/orders/{order.Id}"))!;
        Assert.Equal(OrderStatus.Shipped, saved.Status);
        Assert.Equal(2, saved.StatusHistory.Count);
    }

    [Fact]
    public async Task ConcurrentDeliveryAndCancellation_OnlyOneCommits_WithItsHistory()
    {
        using var admin = await UserAsync(true);
        using var owner = await UserAsync();
        var product = await ProductAsync(admin);
        var order = await CreateAsync(owner, Request((product.Id, 1)));
        order = await ChangeAsync(admin, order, "ship");
        var adminId = order.StatusHistory.Last().ChangedByUserId;
        var barrier = new TwoSavesBarrier();
        await using var firstDb = factory.CreateDb(barrier);
        await using var secondDb = factory.CreateDb(barrier);
        var outcomes = await Task.WhenAll(
            Capture(() => new OrderService(firstDb).ChangeStatusAsync(order.Id, OrderStatus.Delivered, order.RowVersion, null, adminId, true, default)),
            Capture(() => new OrderService(secondDb).ChangeStatusAsync(order.Id, OrderStatus.Cancelled, order.RowVersion, "Customer cancellation", order.UserId, false, default)));
        Assert.Single(outcomes, e => e is null);
        Assert.IsType<DbUpdateConcurrencyException>(Assert.Single(outcomes, e => e is not null));
        var saved = (await owner.GetFromJsonAsync<OrderResponseDto>($"/api/orders/{order.Id}"))!;
        Assert.Equal(3, saved.StatusHistory.Count);
        Assert.Equal(saved.Status, saved.StatusHistory.Last().NewStatus);
        Assert.Equal(OrderStatus.Shipped, saved.StatusHistory.Last().PreviousStatus);
        Assert.Equal(saved.Status == OrderStatus.Delivered ? adminId : order.UserId, saved.StatusHistory.Last().ChangedByUserId);
    }

    [Fact]
    public async Task ConcurrentCreateRetries_CommitOneOrderAndOneHistory()
    {
        using var admin = await UserAsync(true);
        using var owner = await UserAsync();
        var product = await ProductAsync(admin);
        var initial = await CreateAsync(owner, Request((product.Id, 1)));
        var request = Request((product.Id, 2));
        await using var firstDb = factory.CreateDb();
        await using var secondDb = factory.CreateDb();
        var results = await Task.WhenAll(
            new OrderService(firstDb).CreateAsync(request, initial.UserId, default),
            new OrderService(secondDb).CreateAsync(request, initial.UserId, default));
        Assert.Equal(results[0].Order.Id, results[1].Order.Id);
        Assert.Single(results, r => r.IsReplay);
        Assert.Single(results, r => !r.IsReplay);
        await using var db = factory.CreateDb();
        var saved = await db.Orders.Include(o => o.StatusHistory).SingleAsync(o => o.UserId == initial.UserId && o.RequestId == request.RequestId);
        Assert.Single(saved.StatusHistory);
    }

    [Fact]
    public async Task FailedHistoryInsert_RollsBackStatusChange_AndHistoryCannotBeEditedOrDeleted()
    {
        using var admin = await UserAsync(true);
        using var owner = await UserAsync();
        var product = await ProductAsync(admin);
        var order = await CreateAsync(owner, Request((product.Id, 1)));
        await using (var db = factory.CreateDb())
            await Assert.ThrowsAsync<DbUpdateException>(() => new OrderService(db).ChangeStatusAsync(order.Id,
                OrderStatus.Shipped, order.RowVersion, null, "missing-actor", true, default));
        var saved = (await owner.GetFromJsonAsync<OrderResponseDto>($"/api/orders/{order.Id}"))!;
        Assert.Equal(OrderStatus.Placed, saved.Status);
        Assert.Equal(order.RowVersion, saved.RowVersion);
        Assert.Single(saved.StatusHistory);
        await using var editDb = factory.CreateDb();
        var history = await editDb.OrderStatusHistory.SingleAsync(h => h.OrderId == order.Id);
        history.Reason = "Rewritten";
        await Assert.ThrowsAsync<InvalidOperationException>(() => editDb.SaveChangesAsync());
        editDb.ChangeTracker.Clear();
        history = await editDb.OrderStatusHistory.SingleAsync(h => h.OrderId == order.Id);
        editDb.OrderStatusHistory.Remove(history);
        Assert.Throws<InvalidOperationException>(() => editDb.SaveChanges());
    }

    private static async Task<Exception?> Capture(Func<Task<OrderResponseDto>> action)
    {
        try { await action(); return null; }
        catch (Exception ex) { return ex; }
    }

    private sealed class TwoSavesBarrier : SaveChangesInterceptor
    {
        private int arrivals;
        private readonly TaskCompletionSource ready = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public override async ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (Interlocked.Increment(ref arrivals) == 2) ready.TrySetResult();
            await ready.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
            return result;
        }
    }
}
