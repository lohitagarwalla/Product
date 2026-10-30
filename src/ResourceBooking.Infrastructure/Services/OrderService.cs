using System.Text.Json;
using System.Security.Cryptography;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.SqlClient;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Entities;
using ResourceBooking.Core.Enums;
using ResourceBooking.Core.Exceptions;
using ResourceBooking.Core.Interfaces;
using ResourceBooking.Infrastructure.Data;

namespace ResourceBooking.Infrastructure.Services;

public class OrderService(ApplicationDbContext db, ILogger<OrderService>? logger = null) : IOrderService
{
    private const decimal MaxAmount = 9999999999999999.99m;
    private IQueryable<Order> OrderWithDetails => db.Orders.Include(o => o.Items).Include(o => o.StatusHistory);

    public async Task<OrderCreateResult> CreateAsync(OrderCreateDto dto, string userId, CancellationToken ct)
    {
        ValidateCreateRequest(dto);
        var hash = ComputeRequestHash(dto);

        var existing = await FindRequestAsync(userId, dto.RequestId, ct);
        if (existing is not null) return await ReplayAsync(existing, hash, ct);

        var deliveryAddress = await LoadDeliveryAddressAsync(dto.AddressId, userId, ct);
        var productIds = dto.Items.Select(i => i.ProductId).ToArray();
        var products = await LoadCheckoutProductsAsync(productIds, ct);
        var order = BuildOrder(dto, userId, deliveryAddress, hash, products);
        var saved = await SaveOrderAsync(order, ct);
        if (saved.IsReplay) return await ReplayAsync(saved.Order, hash, ct);

        // Only newly created orders clean up the cart. Replays must preserve later additions.
        await ResetCartAfterCheckoutAsync(userId, productIds, order.Id, ct);
        return new(await ToDtoAsync(order, ct), false);
    }

    public async Task<OrderPageDto> ListAsync(OrderQueryDto query, string userId, bool includeAllUsers, CancellationToken ct)
    {
        var orders = db.Orders.AsNoTracking().Where(o => includeAllUsers || o.UserId == userId);
        orders = ApplyListFilters(orders, query);
        var count = await orders.CountAsync(ct);
        var page = await orders.OrderByDescending(o => o.CreatedAt).ThenByDescending(o => o.Id)
            .Skip((query.Page - 1) * query.PageSize).Take(query.PageSize)
            .Select(o => new OrderSummaryDto(o.Id, o.OrderNumber, o.UserId, o.Status, o.Currency,
                o.TotalAmount, o.Items.Sum(i => i.Quantity), o.CreatedAt,
                db.Users.Where(u => u.Id == o.UserId).Select(u => u.FirstName).FirstOrDefault())).ToListAsync(ct);
        // SQL datetime2 does not retain DateTime.Kind; the API always emits UTC.
        return new(page.Select(o => o with { CreatedAt = Utc(o.CreatedAt) }).ToList(), count, query.Page, query.PageSize);
    }

    public async Task<OrderResponseDto> GetAsync(int id, string userId, bool isAdmin, CancellationToken ct)
    {
        var order = await AccessibleOrders(userId, isAdmin).AsNoTracking().SingleOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new KeyNotFoundException("Order not found.");
        return await ToDtoAsync(order, ct);
    }

    public async Task<OrderResponseDto> ChangeStatusAsync(int id, OrderStatus target, byte[] rowVersion,
        string? reason, string userId, bool isAdmin, CancellationToken ct)
    {
        ValidateStatusChangeRequest(target, rowVersion, reason, isAdmin);

        var order = await AccessibleOrders(userId, isAdmin).SingleOrDefaultAsync(o => o.Id == id, ct)
            ?? throw new KeyNotFoundException("Order not found.");
        // A retry of the completed operation must not replace its actor, time, or reason.
        if (order.Status == target) return await ToDtoAsync(order, ct);
        if (!order.RowVersion.SequenceEqual(rowVersion)) throw new DbUpdateConcurrencyException();
        ApplyStatusChange(order, target, reason, userId);

        // EF uses the original RowVersion in UPDATE's WHERE clause. Its implicit transaction
        // rolls back the new history entry too if another request changed the order first.
        await db.SaveChangesAsync(ct);
        return await ToDtoAsync(order, ct);
    }

    private static void ValidateCreateRequest(OrderCreateDto dto)
    {
        if (dto.RequestId == Guid.Empty || dto.Items is null || dto.Items.Count is < 1 or > 100 ||
            dto.Items.Any(i => i is null || i.ProductId <= 0 || i.Quantity is < 1 or > 1000) ||
            dto.Items.Select(i => i.ProductId).Distinct().Count() != dto.Items.Count)
            throw new InvalidOperationException("Supply a request ID and 1-100 distinct products with quantities between 1 and 1000.");

        if (dto.AddressId <= 0)
            throw new InvalidOperationException("Supply a valid delivery address ID.");
    }

    private async Task<OrderDeliveryAddress> LoadDeliveryAddressAsync(int addressId, string userId, CancellationToken ct)
    {
        var address = await db.UserAddresses.AsNoTracking()
            .SingleOrDefaultAsync(a => a.Id == addressId && a.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Delivery address not found.");

        // Keep an independent snapshot so profile edits never change an existing order.
        return new OrderDeliveryAddress
        {
            PhoneNumber = AddressPhoneNumber.Normalize(address.PhoneNumber),
            RecipientName = address.RecipientName.Trim(), AddressLine1 = address.AddressLine1.Trim(),
            AddressLine2 = string.IsNullOrWhiteSpace(address.AddressLine2) ? null : address.AddressLine2.Trim(),
            City = address.City.Trim(), State = address.State.Trim(), PostalCode = address.PostalCode.Trim(),
            CountryCode = address.CountryCode.Trim().ToUpperInvariant()
        };
    }

    private static string ComputeRequestHash(OrderCreateDto dto)
    {
        // Structured, sorted inputs make retry identity independent of item order.
        var canonicalRequest = JsonSerializer.Serialize(new
        {
            Items = dto.Items.OrderBy(i => i.ProductId).Select(i => new { i.ProductId, i.Quantity }),
            dto.AddressId
        });
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest)));
    }

    private async Task<Dictionary<int, Product>> LoadCheckoutProductsAsync(int[] ids, CancellationToken ct)
    {
        // Snapshot the published catalog values read for this checkout.
        // Catalog edits do not need to wait for order creation to commit.
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id) && p.IsPublished)
            .ToDictionaryAsync(p => p.Id, ct);
        if (products.Count != ids.Length)
            throw new InvalidOperationException("One or more products are unavailable. Refresh your cart.");
        if (products.Values.Select(p => p.Currency).Distinct(StringComparer.OrdinalIgnoreCase).Count() != 1)
            throw new InvalidOperationException("All products in an order must use the same currency.");

        return products;
    }

    private static Order BuildOrder(OrderCreateDto dto, string userId, OrderDeliveryAddress deliveryAddress,
        string hash, IReadOnlyDictionary<int, Product> products)
    {
        var order = new Order
        {
            OrderNumber = $"ORD-{Guid.NewGuid():N}".ToUpperInvariant(), UserId = userId,
            RequestId = dto.RequestId, RequestHash = hash,
            Currency = products.Values.First().Currency.ToUpperInvariant(), DeliveryAddress = deliveryAddress
        };
        foreach (var item in dto.Items.OrderBy(i => i.ProductId))
        {
            var product = products[item.ProductId];
            var lineTotal = product.Price * item.Quantity;
            if (lineTotal > MaxAmount || order.TotalAmount > MaxAmount - lineTotal)
                throw new InvalidOperationException("The order total exceeds the supported amount.");
            order.Items.Add(new OrderItem
            {
                ProductId = product.Id, ProductTitle = product.Title, UnitPrice = product.Price,
                Quantity = item.Quantity, LineTotal = lineTotal
            });
            order.TotalAmount += lineTotal;
        }
        order.StatusHistory.Add(new OrderStatusHistory
        {
            PreviousStatus = null, NewStatus = OrderStatus.Placed,
            ChangedByUserId = userId, ChangedAtUtc = DateTime.UtcNow
        });
        return order;
    }

    private async Task<(Order Order, bool IsReplay)> SaveOrderAsync(Order order, CancellationToken ct)
    {
        db.Orders.Add(order);
        try
        {
            // SaveChanges commits the order, items, and history atomically.
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
        {
            // Another request may have committed the same idempotency key after our read.
            // Discard the failed insert graph before loading the winner.
            db.ChangeTracker.Clear();
            var existing = await FindRequestAsync(order.UserId, order.RequestId, ct);
            if (existing is null) throw;
            return (existing, true);
        }

        return (order, false);
    }

    private async Task ResetCartAfterCheckoutAsync(string userId, int[] productIds, int orderId, CancellationToken ct)
    {
        try
        {
            var purchased = db.CartItems.Where(i => i.Cart.UserId == userId && productIds.Contains(i.ProductId));
            if (db.Database.IsRelational())
            {
                // Cart reset is atomic and independent of the already committed order.
                // Bulk writes deliberately omit cart row-version and quantity checks.
                await using var transaction = await db.Database.BeginTransactionAsync(ct);
                await db.Carts.Where(c => c.UserId == userId).ExecuteUpdateAsync(setters => setters
                    .SetProperty(c => c.SelectedAddressId, c => db.UserAddresses
                        .Where(a => a.UserId == c.UserId && a.IsDefault)
                        .Select(a => (int?)a.Id).FirstOrDefault())
                    .SetProperty(c => c.UpdatedAt, DateTime.UtcNow), ct);
                await purchased.ExecuteDeleteAsync(ct);
                await transaction.CommitAsync(ct);
            }
            else
            {
                // The InMemory provider does not support bulk writes or transactions.
                var cart = await db.Carts.SingleOrDefaultAsync(c => c.UserId == userId, ct);
                if (cart is not null)
                {
                    cart.SelectedAddressId = await db.UserAddresses.Where(a => a.UserId == userId && a.IsDefault)
                        .Select(a => (int?)a.Id).SingleOrDefaultAsync(ct);
                    db.Entry(cart).Property(c => c.UpdatedAt).IsModified = true;
                }
                db.CartItems.RemoveRange(await purchased.ToListAsync(ct));
                await db.SaveChangesAsync(ct);
            }
        }
        catch (Exception ex)
        {
            // Cleanup is best effort: never turn a committed order into a checkout failure.
            // Log only identifiers and the exception type, not SQL or customer details.
            (logger ?? NullLogger<OrderService>.Instance).LogWarning(
                "Cart cleanup failed for order {OrderId} ({FailureType}). The order remains committed.",
                orderId, ex.GetType().Name);
        }
    }

    private IQueryable<Order> AccessibleOrders(string userId, bool isAdmin) =>
        OrderWithDetails.Where(o => isAdmin || o.UserId == userId);

    private static IQueryable<Order> ApplyListFilters(IQueryable<Order> orders, OrderQueryDto query)
    {
        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            orders = orders.Where(o => o.OrderNumber.Contains(term) || o.Items.Any(i => i.ProductTitle.Contains(term)));
        }
        if (query.Status.HasValue) orders = orders.Where(o => o.Status == query.Status.Value);
        if (query.FromUtc.HasValue)
        {
            var from = query.FromUtc.Value.UtcDateTime;
            orders = orders.Where(o => o.CreatedAt >= from);
        }
        if (query.ToUtc.HasValue)
        {
            var to = query.ToUtc.Value.UtcDateTime;
            orders = orders.Where(o => o.CreatedAt < to);
        }
        return orders;
    }

    private static void ValidateStatusChangeRequest(OrderStatus target, byte[] rowVersion,
        string? reason, bool isAdmin)
    {
        if (target is not (OrderStatus.Shipped or OrderStatus.Delivered or OrderStatus.Cancelled))
            throw new InvalidOperationException("Unsupported order status change.");
        if (target != OrderStatus.Cancelled && !isAdmin)
            throw new UnauthorizedAccessException("Only admins can ship or deliver orders.");
        if (rowVersion is null || rowVersion.Length != 8 || reason?.Length > 1000)
            throw new InvalidOperationException("Supply an eight-byte row version. An optional reason may have at most 1000 characters.");
    }

    private static void ApplyStatusChange(Order order, OrderStatus target, string? reason, string userId)
    {
        var allowed = (order.Status, target) switch
        {
            (OrderStatus.Placed, OrderStatus.Shipped or OrderStatus.Cancelled) => true,
            (OrderStatus.Shipped, OrderStatus.Delivered or OrderStatus.Cancelled) => true,
            _ => false
        };
        if (!allowed) throw new OrderConflictException($"Cannot change an order from {order.Status} to {target}.");

        var now = DateTime.UtcNow;
        var normalizedReason = target == OrderStatus.Cancelled && !string.IsNullOrWhiteSpace(reason) ? reason.Trim() : null;
        order.StatusHistory.Add(new OrderStatusHistory
        {
            PreviousStatus = order.Status, NewStatus = target, ChangedByUserId = userId,
            ChangedAtUtc = now, Reason = normalizedReason
        });
        order.Status = target;
        switch (target)
        {
            case OrderStatus.Shipped: order.ShippedAt = now; break;
            case OrderStatus.Delivered: order.DeliveredAt = now; break;
            case OrderStatus.Cancelled:
                order.CancelledAt = now;
                order.CancelledByUserId = userId;
                order.CancellationReason = normalizedReason;
                break;
        }
    }

    private Task<Order?> FindRequestAsync(string userId, Guid requestId, CancellationToken ct) =>
        OrderWithDetails.AsNoTracking().SingleOrDefaultAsync(o => o.UserId == userId && o.RequestId == requestId, ct);

    private async Task<OrderCreateResult> ReplayAsync(Order existing, string hash, CancellationToken ct)
    {
        if (!string.Equals(existing.RequestHash, hash, StringComparison.Ordinal))
            throw new OrderConflictException("This request ID was already used for different checkout inputs. Use a new request ID.");
        return new(await ToDtoAsync(existing, ct), true);
    }

    private async Task<OrderResponseDto> ToDtoAsync(Order order, CancellationToken ct)
    {
        // EF queries share one DbContext and must run sequentially.
        var firstNames = await LoadFirstNamesAsync(order, ct);
        var covers = await LoadProductCoversAsync(order, ct);
        return MapOrderResponse(order, firstNames, covers);
    }

    private async Task<Dictionary<string, string>> LoadFirstNamesAsync(Order order, CancellationToken ct)
    {
        // Resolve all people in one query so history entries show their actual actors.
        var userIds = order.StatusHistory.Select(h => h.ChangedByUserId).Append(order.UserId)
            .Concat(order.CancelledByUserId is { } cancelledBy ? new[] { cancelledBy } : Array.Empty<string>())
            .Distinct().ToArray();
        return await db.Users.AsNoTracking().Where(u => userIds.Contains(u.Id))
            .ToDictionaryAsync(u => u.Id, u => u.FirstName, ct);
    }

    private async Task<Dictionary<int, string>> LoadProductCoversAsync(Order order, CancellationToken ct)
    {
        var productIds = order.Items.Select(i => i.ProductId).ToArray();
        var images = await db.ProductImages.AsNoTracking()
            .Where(i => productIds.Contains(i.ProductId) && i.Product.IsPublished && i.ImageAsset.State == ImageAssetState.Ready)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.ImageAssetId)
            .Select(i => new { i.ProductId, i.ImageAssetId }).ToListAsync(ct);
        return images.GroupBy(i => i.ProductId).ToDictionary(g => g.Key,
            g => $"/api/images/{g.First().ImageAssetId}/content");
    }

    private static OrderResponseDto MapOrderResponse(Order order,
        IReadOnlyDictionary<string, string> firstNames, IReadOnlyDictionary<int, string> covers)
    {
        return new(order.Id, order.OrderNumber, order.UserId, order.Status, order.Currency, order.TotalAmount,
            Utc(order.CreatedAt), Utc(order.ShippedAt), Utc(order.DeliveredAt), Utc(order.CancelledAt),
            order.CancelledByUserId, order.CancellationReason, order.RowVersion,
            order.Items.OrderBy(i => i.Id).Select(i => new OrderItemResponseDto(i.Id, i.ProductId, i.ProductTitle,
                i.UnitPrice, i.Quantity, i.LineTotal, covers.GetValueOrDefault(i.ProductId))).ToList(),
            order.StatusHistory.OrderBy(h => h.ChangedAtUtc).ThenBy(h => h.Id).Select(h =>
                new OrderStatusHistoryResponseDto(h.Id, h.PreviousStatus, h.NewStatus,
                    h.ChangedByUserId, Utc(h.ChangedAtUtc), h.Reason,
                    firstNames.GetValueOrDefault(h.ChangedByUserId))).ToList(),
            firstNames.GetValueOrDefault(order.UserId),
            order.CancelledByUserId is { } actorId ? firstNames.GetValueOrDefault(actorId) : null,
            order.DeliveryAddress is { } address ? new OrderDeliveryAddressDto(address.RecipientName,
                address.AddressLine1, address.AddressLine2, address.City, address.State,
                address.PostalCode, address.CountryCode, address.PhoneNumber) : null);
    }

    private static DateTime Utc(DateTime value) => DateTime.SpecifyKind(value, DateTimeKind.Utc);
    private static DateTime? Utc(DateTime? value) => value.HasValue ? Utc(value.Value) : null;
}
