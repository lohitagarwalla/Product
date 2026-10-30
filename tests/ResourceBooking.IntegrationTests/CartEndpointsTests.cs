using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Entities;
using ResourceBooking.Infrastructure.Data;
using ResourceBooking.Infrastructure.Services;

namespace ResourceBooking.IntegrationTests;

public class CartWebApplicationFactory : CustomWebApplicationFactory<Program>
{
    protected override Microsoft.Extensions.Hosting.IHost CreateHost(Microsoft.Extensions.Hosting.IHostBuilder builder)
    {
        var host = base.CreateHost(builder);
        OrderWebApplicationFactory.InitializeCheckoutDatabaseAsync(host.Services).GetAwaiter().GetResult();
        return host;
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        base.ConfigureWebHost(builder);
        builder.ConfigureLogging(logging => logging.ClearProviders());
        builder.ConfigureServices(services => services.AddDbContext<ApplicationDbContext>(options =>
            options.AddInterceptors(new TestRowVersionInterceptor())));
    }

    // InMemory does not generate rowversions. This tests the API contract only;
    // CartSqlServerTests runs the same scenarios with real SQL constraints/versions.
    private class TestRowVersionInterceptor : SaveChangesInterceptor
    {
        private static long version;
        public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            foreach (var entry in eventData.Context!.ChangeTracker.Entries()
                .Where(e => e.Entity is Cart or UserAddress or Order && e.State is EntityState.Added or EntityState.Modified))
                entry.Property("RowVersion").CurrentValue = BitConverter.GetBytes(Interlocked.Increment(ref version));
            return ValueTask.FromResult(result);
        }
    }
}

public class CartEndpointsTests(CartWebApplicationFactory factory) : CartContractTests(factory), IClassFixture<CartWebApplicationFactory>;

public abstract class CartContractTests(WebApplicationFactory<Program> factory)
{
    [Fact]
    public async Task PhoneNumber_CartAndOrderSnapshots_AndReplayAfterAddressEdit()
    {
        using var user = await UserAsync(); using var admin = await UserAsync(true);
        var product = await ProductAsync(admin);
        var response = await user.PostAsJsonAsync("/api/profile/addresses", new AddressCreateDto
        {
            RecipientName = "Recipient", AddressLine1 = "1 Main Street", City = "Bengaluru",
            State = "Karnataka", PostalCode = "560001", PhoneNumber = "9876543210"
        });
        response.EnsureSuccessStatusCode();
        var address = (await response.Content.ReadFromJsonAsync<AddressResponseDto>())!;
        var cart = await SelectAsync(user, await CartAsync(user), address.Id);
        Assert.Equal("+919876543210", cart.SelectedAddress!.PhoneNumber);
        Assert.Equal("+919876543210", (await CartAsync(user)).SelectedAddress!.PhoneNumber);
        var request = await RequestAsync(user, product.Id); request.AddressId = address.Id;
        response = await user.PostAsJsonAsync("/api/orders", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<OrderResponseDto>())!;
        Assert.Equal("+919876543210", order.DeliveryAddress!.PhoneNumber);
        response = await user.PutAsJsonAsync($"/api/profile/addresses/{address.Id}", new AddressUpdateDto
        {
            RecipientName = address.RecipientName, AddressLine1 = address.AddressLine1, City = address.City,
            State = address.State, PostalCode = address.PostalCode, CountryCode = address.CountryCode,
            RowVersion = address.RowVersion, PhoneNumber = "8765432109"
        });
        response.EnsureSuccessStatusCode();
        Assert.Equal("+918765432109", (await CartAsync(user)).SelectedAddress!.PhoneNumber);
        Assert.Equal("+919876543210", (await user.GetFromJsonAsync<OrderResponseDto>($"/api/orders/{order.Id}"))!.DeliveryAddress!.PhoneNumber);
        Assert.Equal(HttpStatusCode.OK, (await user.PostAsJsonAsync("/api/orders", request)).StatusCode);
        request.AddressId = (await AddressAsync(user)).Id;
        Assert.Equal(HttpStatusCode.Conflict, (await user.PostAsJsonAsync("/api/orders", request)).StatusCode);
    }

    [Fact]
    public async Task Checkout_RequiresAddressId_AndIgnoresClientAddressAndOwnership()
    {
        using var user = await UserAsync(); using var admin = await UserAsync(true);
        var product = await ProductAsync(admin);
        var address = await AddressAsync(user);
        var request = new
        {
            requestId = Guid.NewGuid(), addressId = address.Id, userId = "forged-user",
            items = new[] { new { productId = product.Id, quantity = 1 } },
            deliveryAddress = new { recipientName = "Forged", addressLine1 = "Forged street" }
        };
        var response = await user.PostAsJsonAsync("/api/orders", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<OrderResponseDto>())!;
        Assert.NotEqual("forged-user", order.UserId);
        Assert.Equal(address.RecipientName, order.DeliveryAddress!.RecipientName);
        Assert.Equal(address.AddressLine1, order.DeliveryAddress.AddressLine1);
        response = await user.PostAsJsonAsync("/api/orders", new
        {
            requestId = Guid.NewGuid(), request.items, request.deliveryAddress
        });
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Single((await user.GetFromJsonAsync<OrderPageDto>("/api/orders"))!.Items);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Checkout_RejectsMissingOrOtherUsersAddress_WithoutChangingCart(bool otherUsersAddress)
    {
        using var user = await UserAsync(); using var other = await UserAsync(); using var admin = await UserAsync(true);
        var product = await ProductAsync(admin);
        await SetItemAsync(user, await CartAsync(user), product.Id, 3);
        var request = await RequestAsync(user, product.Id);
        request.AddressId = otherUsersAddress ? (await AddressAsync(other)).Id : int.MaxValue;
        var response = await user.PostAsJsonAsync("/api/orders", request);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal(3, Assert.Single((await CartAsync(user)).Items).Quantity);
        Assert.Empty((await user.GetFromJsonAsync<OrderPageDto>("/api/orders"))!.Items);
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var cart = await CartAsync(user);
        var userId = await db.Carts.Where(c => c.Id == cart.Id).Select(c => c.UserId).SingleAsync();
        await Assert.ThrowsAsync<KeyNotFoundException>(() => new OrderService(db).CreateAsync(request, userId, default));
    }

    [Fact]
    public async Task Checkout_ReplaySurvivesAddressDeletion_ButNewRequestIsRejected()
    {
        using var user = await UserAsync(); using var admin = await UserAsync(true);
        var product = await ProductAsync(admin);
        var address = await AddressAsync(user);
        var request = new OrderCreateDto { RequestId = Guid.NewGuid(), AddressId = address.Id,
            Items = [new() { ProductId = product.Id, Quantity = 1 }] };
        var response = await user.PostAsJsonAsync("/api/orders", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var order = (await response.Content.ReadFromJsonAsync<OrderResponseDto>())!;
        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/profile/addresses/{address.Id}")
            { Content = JsonContent.Create(new AddressVersionDto { RowVersion = address.RowVersion }) };
        (await user.SendAsync(delete)).EnsureSuccessStatusCode();
        response = await user.PostAsJsonAsync("/api/orders", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var replay = (await response.Content.ReadFromJsonAsync<OrderResponseDto>())!;
        Assert.Equal(order.Id, replay.Id);
        Assert.Equal(address.AddressLine1, replay.DeliveryAddress!.AddressLine1);
        request.RequestId = Guid.NewGuid();
        Assert.Equal(HttpStatusCode.NotFound, (await user.PostAsJsonAsync("/api/orders", request)).StatusCode);
    }

    [Theory]
    [InlineData("GET", "")]
    [InlineData("PUT", "/items/1")]
    [InlineData("DELETE", "/items/1")]
    [InlineData("DELETE", "/items")]
    [InlineData("PUT", "/address")]
    public async Task Operations_RequireAuthentication(string method, string path)
    {
        using var client = factory.CreateClient();
        using var request = new HttpRequestMessage(new HttpMethod(method), "/api/cart" + path);
        if (method == "PUT") request.Content = JsonContent.Create(new { });
        Assert.Equal(HttpStatusCode.Unauthorized, (await client.SendAsync(request)).StatusCode);
    }

    [Fact]
    public async Task Items_Persist_WithoutVersion_LastWriteWins_AndIsolateAccounts()
    {
        using var user = await UserAsync(); using var other = await UserAsync();
        using var admin = await UserAsync(true);
        var product = await ProductAsync(admin);
        var cart = await SetItemAsync(user, await CartAsync(user), product.Id, 3);
        var response = await user.PutAsJsonAsync($"/api/cart/items/{product.Id}", new { quantity = 7 });
        response.EnsureSuccessStatusCode();
        Assert.Equal(7, Assert.Single((await CartAsync(user)).Items).Quantity);
        Assert.Empty((await CartAsync(other)).Items);
        using var resumed = factory.CreateClient();
        resumed.DefaultRequestHeaders.Authorization = user.DefaultRequestHeaders.Authorization;
        Assert.Equal(cart.Id, (await CartAsync(resumed)).Id);
        Assert.Equal(7, Assert.Single((await CartAsync(resumed)).Items).Quantity);
        Assert.Empty((await DeleteAsync(user, $"/api/cart/items/{product.Id}")).Items);
        Assert.Empty((await DeleteAsync(user, "/api/cart/items")).Items);
    }

    [Theory]
    [InlineData(0)] [InlineData(-1)] [InlineData(1001)]
    public async Task Quantity_RejectsInvalidValues(int quantity)
    {
        using var user = await UserAsync(); using var admin = await UserAsync(true);
        var product = await ProductAsync(admin); await CartAsync(user);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PutAsJsonAsync($"/api/cart/items/{product.Id}",
            new { quantity })).StatusCode);
        Assert.Empty((await CartAsync(user)).Items);
    }

    [Fact]
    public async Task Mutations_RejectUnavailableProducts_AndCheckoutRouteIsRetired()
    {
        using var user = await UserAsync(); using var admin = await UserAsync(true);
        var product = await ProductAsync(admin, false); await CartAsync(user);
        Assert.Equal(HttpStatusCode.NotFound, (await user.PutAsJsonAsync($"/api/cart/items/{product.Id}",
            new { quantity = 1 })).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await user.PostAsJsonAsync("/api/cart/checkout", new { })).StatusCode);
    }

    [Fact]
    public async Task AddressSelection_Persists_RejectsForeignAddresses_AndDeletionClearsSelection()
    {
        using var owner = await UserAsync(); using var other = await UserAsync();
        var address = await AddressAsync(owner);
        await CartAsync(other);
        Assert.Equal(HttpStatusCode.NotFound, (await other.PutAsJsonAsync("/api/cart/address",
            new { addressId = address.Id })).StatusCode);
        await SelectAsync(owner, await CartAsync(owner), address.Id);
        Assert.Equal(address.Id, (await CartAsync(owner)).SelectedAddressId);
        using var delete = new HttpRequestMessage(HttpMethod.Delete, $"/api/profile/addresses/{address.Id}")
        { Content = JsonContent.Create(new AddressVersionDto { RowVersion = address.RowVersion }) };
        (await owner.SendAsync(delete)).EnsureSuccessStatusCode();
        var cart = await CartAsync(owner);
        Assert.Null(cart.SelectedAddressId); Assert.Null(cart.SelectedAddress);
    }

    [Fact]
    public async Task Checkout_UsesOwnedDatabaseAddress_RemovesMatchingProducts_AndReplayPreservesNewItems()
    {
        using var user = await UserAsync(); using var admin = await UserAsync(true);
        var ordered = await ProductAsync(admin); var retained = await ProductAsync(admin);
        var submittedOnly = await ProductAsync(admin);
        var defaultAddress = await AddressAsync(user);
        var address = await AddressAsync(user);
        var cart = await SetItemAsync(user, await CartAsync(user), ordered.Id, 9);
        cart = await SetItemAsync(user, cart, retained.Id, 4);
        cart = await SelectAsync(user, cart, address.Id);
        var request = await RequestAsync(user, ordered.Id, 2);
        request.AddressId = address.Id;
        request.Items.Add(new() { ProductId = submittedOnly.Id, Quantity = 1 });
        (await user.PutAsJsonAsync($"/api/profile/addresses/{address.Id}", new AddressUpdateDto
        {
            RecipientName = address.RecipientName, AddressLine1 = "Changed saved address", City = address.City,
            State = address.State, PostalCode = address.PostalCode, CountryCode = address.CountryCode,
            RowVersion = address.RowVersion
        })).EnsureSuccessStatusCode();
        var response = await user.PostAsJsonAsync("/api/orders", request);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location); Assert.True(response.Headers.CacheControl?.NoStore);
        var order = (await response.Content.ReadFromJsonAsync<OrderResponseDto>())!;
        Assert.Equal(37.02m, order.TotalAmount);
        Assert.Equal("Changed saved address", order.DeliveryAddress!.AddressLine1);
        Assert.Equal(2, order.Items.Single(i => i.ProductId == ordered.Id).Quantity);
        cart = await CartAsync(user);
        Assert.Equal(retained.Id, Assert.Single(cart.Items).ProductId);
        Assert.Equal(4, Assert.Single(cart.Items).Quantity);
        Assert.Equal(defaultAddress.Id, cart.SelectedAddressId);
        Assert.True(cart.SelectedAddress!.IsDefault);
        await SelectAsync(user, cart, address.Id);
        await SetItemAsync(user, cart, ordered.Id, 5);
        (await admin.DeleteAsync($"/api/products/{ordered.Id}")).EnsureSuccessStatusCode();
        var replay = await user.PostAsJsonAsync("/api/orders", request);
        Assert.Equal(HttpStatusCode.OK, replay.StatusCode);
        Assert.Equal(order.Id, (await replay.Content.ReadFromJsonAsync<OrderResponseDto>())!.Id);
        Assert.Equal(5, (await CartAsync(user)).Items.Single(i => i.ProductId == ordered.Id).Quantity);
        Assert.Equal(address.Id, (await CartAsync(user)).SelectedAddressId);
        request.AddressId = (await AddressAsync(user)).Id;
        Assert.Equal(HttpStatusCode.Conflict, (await user.PostAsJsonAsync("/api/orders", request)).StatusCode);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task Checkout_ResetsAddressEvenWithoutMatchingCartItems(bool hasDefault)
    {
        using var user = await UserAsync(); using var admin = await UserAsync(true);
        var product = await ProductAsync(admin);
        var defaultAddress = await AddressAsync(user);
        var selected = await AddressAsync(user);
        var cart = await SelectAsync(user, await CartAsync(user), selected.Id);
        if (!hasDefault)
        {
            using var scope = factory.Services.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var address = await db.UserAddresses.SingleAsync(a => a.Id == defaultAddress.Id);
            address.IsDefault = false;
            await db.SaveChangesAsync();
        }
        var response = await user.PostAsJsonAsync("/api/orders", new OrderCreateDto
        {
            RequestId = Guid.NewGuid(), AddressId = selected.Id,
            Items = [new() { ProductId = product.Id, Quantity = 1 }]
        });
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        cart = await CartAsync(user);
        Assert.Equal(hasDefault ? defaultAddress.Id : (int?)null, cart.SelectedAddressId);
        if (!hasDefault) Assert.Null(cart.SelectedAddress);
    }

    [Fact]
    public async Task Checkout_WorksWithoutSavedCart_AndItemOrderDoesNotAffectReplay()
    {
        using var user = await UserAsync(); using var admin = await UserAsync(true);
        var first = await ProductAsync(admin); var second = await ProductAsync(admin);
        var request = await RequestAsync(user, first.Id);
        request.Items.Add(new() { ProductId = second.Id, Quantity = 2 });
        Assert.Equal(HttpStatusCode.Created, (await user.PostAsJsonAsync("/api/orders", request)).StatusCode);
        request.Items.Reverse();
        Assert.Equal(HttpStatusCode.OK, (await user.PostAsJsonAsync("/api/orders", request)).StatusCode);
        Assert.Empty((await CartAsync(user)).Items);
    }

    [Theory]
    [InlineData("missing-address")] [InlineData("missing-items")] [InlineData("null-item")]
    [InlineData("empty-items")] [InlineData("duplicate-product")] [InlineData("bad-address")]
    [InlineData("bad-quantity")] [InlineData("bad-product")] [InlineData("empty-request-id")]
    public async Task Checkout_InvalidRequest_DoesNotCreateOrderOrRemoveItems(string invalid)
    {
        using var user = await UserAsync(); using var admin = await UserAsync(true);
        var product = await ProductAsync(admin);
        await SetItemAsync(user, await CartAsync(user), product.Id, 3);
        var request = await RequestAsync(user, product.Id);
        switch (invalid)
        {
            case "missing-address": request.AddressId = 0; break;
            case "missing-items": request.Items = null!; break;
            case "null-item": request.Items = [null!]; break;
            case "empty-items": request.Items.Clear(); break;
            case "duplicate-product": request.Items.Add(new() { ProductId = product.Id, Quantity = 1 }); break;
            case "bad-address": request.AddressId = -1; break;
            case "bad-quantity": request.Items[0].Quantity = 0; break;
            case "bad-product": request.Items[0].ProductId = 0; break;
            case "empty-request-id": request.RequestId = Guid.Empty; break;
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsJsonAsync("/api/orders", request)).StatusCode);
        Assert.Equal(3, Assert.Single((await CartAsync(user)).Items).Quantity);
        Assert.Empty((await user.GetFromJsonAsync<OrderPageDto>("/api/orders"))!.Items);
    }

    [Fact]
    public async Task Checkout_RejectsUnavailableProductsAndMixedCurrencies()
    {
        using var user = await UserAsync(); using var admin = await UserAsync(true);
        var first = await ProductAsync(admin); var second = await ProductAsync(admin);
        await SetItemAsync(user, await CartAsync(user), first.Id, 2);
        (await admin.PutAsJsonAsync($"/api/products/{second.Id}", new ProductWriteDto
            { Title = second.Title, Brand = "Example", Price = 12.34m, Currency = "USD", IsPublished = true })).EnsureSuccessStatusCode();
        var request = await RequestAsync(user, first.Id);
        request.Items.Add(new() { ProductId = second.Id, Quantity = 1 });
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsJsonAsync("/api/orders", request)).StatusCode);
        (await admin.DeleteAsync($"/api/products/{first.Id}")).EnsureSuccessStatusCode();
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PostAsJsonAsync("/api/orders", await RequestAsync(user, first.Id))).StatusCode);
        Assert.Equal(2, Assert.Single((await CartAsync(user)).Items).Quantity);
        Assert.Empty((await user.GetFromJsonAsync<OrderPageDto>("/api/orders"))!.Items);
    }
    [Fact]
    public async Task ItemLimit_AllowsUpdatingExistingItem_ButRejectsAnotherProduct()
    {
        using var user = await UserAsync(); using var admin = await UserAsync(true);
        var initial = await CartAsync(user);
        int existingProduct;
        using (var scope = factory.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            var products = Enumerable.Range(1, 100).Select(i => new Product
                { Title = $"Limit product {Guid.NewGuid():N}", Brand = "Example", Price = 1, IsPublished = true }).ToList();
            db.Products.AddRange(products);
            await db.SaveChangesAsync();
            var cart = await db.Carts.SingleAsync(c => c.Id == initial.Id);
            foreach (var product in products) cart.Items.Add(new CartItem { ProductId = product.Id, Quantity = 1 });
            await db.SaveChangesAsync();
            existingProduct = products[0].Id;
        }
        var saved = await SetItemAsync(user, await CartAsync(user), existingProduct, 1000);
        var extra = await ProductAsync(admin);
        Assert.Equal(HttpStatusCode.BadRequest, (await user.PutAsJsonAsync($"/api/cart/items/{extra.Id}",
            new { quantity = 1 })).StatusCode);
        Assert.Equal(100, (await CartAsync(user)).Items.Count);
        Assert.Equal(1000, saved.Items.Single(i => i.ProductId == existingProduct).Quantity);
    }

    protected async Task<HttpClient> UserAsync(bool admin = false)
    {
        var client = factory.CreateClient();
        var response = admin
            ? await client.PostAsJsonAsync("/api/auth/login", new LoginDto { Email = "admin@company.com", Password = "Admin123!" })
            : await client.PostAsJsonAsync("/api/auth/register", new RegisterDto
            {
                Email = $"{Guid.NewGuid():N}@example.com", Password = "Password123!", ConfirmPassword = "Password123!",
                FirstName = "Cart", LastName = "Tester"
            });
        response.EnsureSuccessStatusCode();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", (await response.Content.ReadFromJsonAsync<AuthResponseDto>())!.Token);
        return client;
    }

    protected static async Task<ProductResponseDto> ProductAsync(HttpClient admin, bool published = true)
    {
        var response = await admin.PostAsJsonAsync("/api/products", new ProductWriteDto
        { Title = $"Cart product {Guid.NewGuid():N}", Brand = "Example", Price = 12.34m, Currency = "INR", IsPublished = published });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ProductResponseDto>())!;
    }

    protected static async Task<CartResponseDto> CartAsync(HttpClient user) => (await user.GetFromJsonAsync<CartResponseDto>("/api/cart"))!;
    protected static async Task<CartResponseDto> SetItemAsync(HttpClient user, CartResponseDto cart, int productId, int quantity)
    {
        var response = await user.PutAsJsonAsync($"/api/cart/items/{productId}", new CartItemWriteDto { Quantity = quantity });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CartResponseDto>())!;
    }
    protected static async Task<AddressResponseDto> AddressAsync(HttpClient user)
    {
        var response = await user.PostAsJsonAsync("/api/profile/addresses", new AddressCreateDto
        { RecipientName = "Cart Tester", AddressLine1 = "1 Main Street", City = "Bengaluru", State = "Karnataka", PostalCode = "560001" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<AddressResponseDto>())!;
    }
    protected static async Task<CartResponseDto> SelectAsync(HttpClient user, CartResponseDto cart, int? addressId)
    {
        var response = await user.PutAsJsonAsync("/api/cart/address", new CartAddressWriteDto { AddressId = addressId });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CartResponseDto>())!;
    }
    protected static async Task<CartResponseDto> DeleteAsync(HttpClient user, string url)
    {
        using var request = new HttpRequestMessage(HttpMethod.Delete, url);
        var response = await user.SendAsync(request);
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CartResponseDto>())!;
    }

    protected static async Task<OrderCreateDto> RequestAsync(HttpClient user, int productId, int quantity = 1) => new()
    {
        RequestId = Guid.NewGuid(), Items = [new() { ProductId = productId, Quantity = quantity }],
        AddressId = (await AddressAsync(user)).Id
    };

}
public class CartSqlServerTests(OrderWebApplicationFactory factory) : CartContractTests(factory), IClassFixture<OrderWebApplicationFactory>
{
    [Fact]
    public async Task ConcurrentCheckout_RetriesReturnOneOrder_AndConsumeCartOnce()
    {
        using var user = await UserAsync();
        using var admin = await UserAsync(true);
        var product = await ProductAsync(admin);
        var address = await AddressAsync(user);
        var cart = await SetItemAsync(user, await CartAsync(user), product.Id, 2);
        cart = await SelectAsync(user, cart, address.Id);
        var request = await RequestAsync(user, product.Id, 2);
        var results = await Task.WhenAll(Enumerable.Range(0, 2).Select(_ => user.PostAsJsonAsync("/api/orders", request)));
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.Created);
        Assert.Single(results, r => r.StatusCode == HttpStatusCode.OK);
        var orders = await Task.WhenAll(results.Select(r => r.Content.ReadFromJsonAsync<OrderResponseDto>()));
        Assert.Equal(orders[0]!.Id, orders[1]!.Id);
        Assert.Empty((await CartAsync(user)).Items);
        foreach (var result in results) result.Dispose();
    }

    [Fact]
    public async Task Checkout_CartDeleteFailure_KeepsCommittedOrderAndItems()
    {
        using var user = await UserAsync();
        using var admin = await UserAsync(true);
        var product = await ProductAsync(admin);
        await AddressAsync(user); // Default differs from the selected checkout address.
        var address = await AddressAsync(user);
        var cart = await SetItemAsync(user, await CartAsync(user), product.Id, 2);
        cart = await SelectAsync(user, cart, address.Id);
        var request = await RequestAsync(user, product.Id, 2);
        await using (var db = factory.CreateDb(new FailCartClearInterceptor()))
        {
            var userId = await db.Carts.Where(c => c.Id == cart.Id).Select(c => c.UserId).SingleAsync();
            var result = await new OrderService(db).CreateAsync(request, userId, default);
            Assert.False(result.IsReplay);
            var replay = await new OrderService(db).CreateAsync(request, userId, default);
            Assert.True(replay.IsReplay);
            Assert.Equal(result.Order.Id, replay.Order.Id);
        }
        await using var verify = factory.CreateDb();
        Assert.Equal(address.Id, await verify.Carts.Where(c => c.Id == cart.Id).Select(c => c.SelectedAddressId).SingleAsync());
        Assert.True(await verify.Orders.AnyAsync(o => o.RequestId == request.RequestId));
        Assert.Equal(2, await verify.CartItems.Where(i => i.CartId == cart.Id).Select(i => i.Quantity).SingleAsync());
    }

    [Fact]
    public async Task ConcurrentQuantityUpdates_BothSucceed_LastWriteWins()
    {
        using var user = await UserAsync();
        using var admin = await UserAsync(true);
        var product = await ProductAsync(admin);
        var cart = await SetItemAsync(user, await CartAsync(user), product.Id, 1);
        var results = await Task.WhenAll(Enumerable.Range(2, 2).Select(quantity => user.PutAsJsonAsync($"/api/cart/items/{product.Id}",
            new CartItemWriteDto { Quantity = quantity })));
        Assert.All(results, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
        Assert.Contains(Assert.Single((await CartAsync(user)).Items).Quantity, new[] { 2, 3 });
        foreach (var result in results) result.Dispose();
    }

    private class FailCartClearInterceptor : DbCommandInterceptor
    {
        public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
            System.Data.Common.DbCommand command, CommandEventData eventData,
            InterceptionResult<int> result, CancellationToken cancellationToken = default)
        {
            if (command.CommandText.Contains("DELETE", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("CartItems", StringComparison.Ordinal))
                throw new InvalidOperationException("Injected cart-clear failure.");
            return ValueTask.FromResult(result);
        }
    }
}
