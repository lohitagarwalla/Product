using Microsoft.EntityFrameworkCore;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Entities;
using ResourceBooking.Core.Interfaces;
using ResourceBooking.Infrastructure.Data;

namespace ResourceBooking.Infrastructure.Services;

public class CartService(ApplicationDbContext db) : ICartService
{
    public Task<CartResponseDto> GetAsync(string userId, CancellationToken ct) =>
        UserCartLock.RunAsync(db, userId, async () =>
        {
            var cart = await db.Carts.Include(c => c.Items).Include(c => c.SelectedAddress)
                .SingleOrDefaultAsync(c => c.UserId == userId, ct);
            if (cart is null)
            {
                cart = new Cart { UserId = userId };
                db.Carts.Add(cart);
                await db.SaveChangesAsync(ct);
            }
            return await ToDtoAsync(cart, ct);
        }, ct);

    public Task<CartResponseDto> SetItemAsync(int productId, CartItemWriteDto dto, string userId, CancellationToken ct) =>
        MutateAsync(userId, async cart =>
        {
            if (productId <= 0 || dto.Quantity is < 1 or > 1000)
                throw new InvalidOperationException("Supply a product and a quantity between 1 and 1000.");
            if (!await db.Products.AnyAsync(p => p.Id == productId && p.IsPublished, ct))
                throw new KeyNotFoundException("Product is unavailable.");
            var item = cart.Items.SingleOrDefault(i => i.ProductId == productId);
            if (item is null)
            {
                if (cart.Items.Count >= 100) throw new InvalidOperationException("A cart may contain at most 100 distinct products.");
                cart.Items.Add(new CartItem { ProductId = productId, Quantity = dto.Quantity });
            }
            else item.Quantity = dto.Quantity;
        }, ct);

    public Task<CartResponseDto> RemoveItemAsync(int productId, string userId, CancellationToken ct) =>
        MutateAsync(userId, cart =>
        {
            var item = cart.Items.SingleOrDefault(i => i.ProductId == productId);
            if (item is not null) db.CartItems.Remove(item);
            if (item is not null) cart.Items.Remove(item);
            return Task.CompletedTask;
        }, ct);

    public Task<CartResponseDto> ClearItemsAsync(string userId, CancellationToken ct) =>
        MutateAsync(userId, cart =>
        {
            db.CartItems.RemoveRange(cart.Items);
            cart.Items.Clear();
            return Task.CompletedTask;
        }, ct);

    public Task<CartResponseDto> SetAddressAsync(CartAddressWriteDto dto, string userId, CancellationToken ct) =>
        MutateAsync(userId, async cart =>
        {
            cart.SelectedAddress = dto.AddressId is { } id
                ? await db.UserAddresses.SingleOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct)
                    ?? throw new KeyNotFoundException("Address not found.")
                : null;
            cart.SelectedAddressId = dto.AddressId;
        }, ct);

    private Task<CartResponseDto> MutateAsync(string userId, Func<Cart, Task> action, CancellationToken ct) =>
        UserCartLock.RunAsync(db, userId, async () =>
        {
            var cart = await db.Carts.Include(c => c.Items).Include(c => c.SelectedAddress)
                .SingleOrDefaultAsync(c => c.UserId == userId, ct)
                ?? throw new KeyNotFoundException("Cart not found. Load the cart first.");
            await action(cart);
            // Record activity for cart edits, including item-only changes.
            db.Entry(cart).Property(c => c.UpdatedAt).IsModified = true;
            await db.SaveChangesAsync(ct);
            return await ToDtoAsync(cart, ct);
        }, ct);

    private async Task<CartResponseDto> ToDtoAsync(Cart cart, CancellationToken ct)
    {
        var ids = cart.Items.Select(i => i.ProductId).ToArray();
        // Unavailable products remain represented, without exposing draft catalogue details.
        var products = await db.Products.AsNoTracking().Where(p => ids.Contains(p.Id) && p.IsPublished).ToDictionaryAsync(p => p.Id, ct);
        var images = await db.ProductImages.AsNoTracking()
            .Where(i => ids.Contains(i.ProductId) && i.Product.IsPublished && i.ImageAsset.State == ImageAssetState.Ready)
            .OrderBy(i => i.SortOrder).ThenBy(i => i.ImageAssetId)
            .Select(i => new { i.ProductId, i.ImageAssetId }).ToListAsync(ct);
        var covers = images.GroupBy(i => i.ProductId).ToDictionary(g => g.Key, g => $"/api/images/{g.First().ImageAssetId}/content");
        var a = cart.SelectedAddress;
        var address = a is null ? null : new AddressResponseDto(a.Id, a.Label, a.RecipientName,
            a.AddressLine1, a.AddressLine2, a.City, a.State, a.PostalCode, a.CountryCode, a.IsDefault,
            a.RowVersion, DateTime.SpecifyKind(a.CreatedAt, DateTimeKind.Utc),
            a.UpdatedAt.HasValue ? DateTime.SpecifyKind(a.UpdatedAt.Value, DateTimeKind.Utc) : null, a.PhoneNumber);
        return new(cart.Id, cart.SelectedAddressId, address,
            cart.Items.OrderBy(i => i.ProductId).Select(i =>
            {
                products.TryGetValue(i.ProductId, out var product);
                return new CartItemResponseDto(i.ProductId, i.Quantity, product is not null,
                    product?.Title, product?.Price, product?.Currency, covers.GetValueOrDefault(i.ProductId));
            }).ToList());
    }
}
