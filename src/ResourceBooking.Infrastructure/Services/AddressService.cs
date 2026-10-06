using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ResourceBooking.Core.DTOs;
using ResourceBooking.Core.Entities;
using ResourceBooking.Core.Exceptions;
using ResourceBooking.Core.Interfaces;
using ResourceBooking.Infrastructure.Data;

namespace ResourceBooking.Infrastructure.Services;

public class AddressService(ApplicationDbContext db) : IAddressService
{
    public async Task<IReadOnlyList<AddressResponseDto>> ListAsync(string userId, CancellationToken ct) =>
        (await db.UserAddresses.AsNoTracking().Where(a => a.UserId == userId)
            .OrderByDescending(a => a.IsDefault).ThenBy(a => a.CreatedAt).ThenBy(a => a.Id)
            .ToListAsync(ct)).Select(ToDto).ToList();

    public async Task<AddressResponseDto> GetAsync(int id, string userId, CancellationToken ct) =>
        ToDto(await db.UserAddresses.AsNoTracking().SingleOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct)
            ?? throw new KeyNotFoundException("Address not found."));

    public Task<AddressResponseDto> CreateAsync(AddressCreateDto dto, string userId, CancellationToken ct) =>
        WithUserLockAsync(userId, async () =>
        {
            var hasAddresses = await db.UserAddresses.AnyAsync(a => a.UserId == userId, ct);
            var address = new UserAddress { UserId = userId, IsDefault = !hasAddresses || dto.MakeDefault };
            ApplyDetails(address, dto);
            if (address.IsDefault) await ClearDefaultAsync(userId, ct);
            db.UserAddresses.Add(address);
            await db.SaveChangesAsync(ct);
            return ToDto(address);
        }, ct);

    public Task<AddressResponseDto> UpdateAsync(int id, AddressUpdateDto dto, string userId, CancellationToken ct) =>
        WithUserLockAsync(userId, async () =>
        {
            var address = await FindOwnedAsync(id, userId, ct);
            CheckVersion(address, dto.RowVersion);
            ApplyDetails(address, dto);
            await db.SaveChangesAsync(ct);
            return ToDto(address);
        }, ct);

    public Task<AddressResponseDto> SetDefaultAsync(int id, byte[] rowVersion, string userId, CancellationToken ct) =>
        WithUserLockAsync(userId, async () =>
        {
            var address = await FindOwnedAsync(id, userId, ct);
            CheckVersion(address, rowVersion);
            if (!address.IsDefault)
            {
                // Save the old default first so SQL Server never sees two defaults.
                await ClearDefaultAsync(userId, ct);
                address.IsDefault = true;
                await db.SaveChangesAsync(ct);
            }
            return ToDto(address);
        }, ct);

    public async Task DeleteAsync(int id, byte[] rowVersion, string userId, CancellationToken ct) =>
        await WithUserLockAsync(userId, async () =>
        {
            var address = await FindOwnedAsync(id, userId, ct);
            CheckVersion(address, rowVersion);
            var wasDefault = address.IsDefault;
            var cart = await db.Carts.SingleOrDefaultAsync(c => c.UserId == userId && c.SelectedAddressId == id, ct);
            if (cart is not null)
            {
                cart.SelectedAddressId = null;
                cart.SelectedAddress = null;
                db.Entry(cart).Property(c => c.UpdatedAt).IsModified = true;
                // Clear the restrictive FK before deleting the address; both writes share a transaction.
                await db.SaveChangesAsync(ct);
            }
            db.UserAddresses.Remove(address);
            await db.SaveChangesAsync(ct);
            if (wasDefault)
            {
                var replacement = await db.UserAddresses.Where(a => a.UserId == userId)
                    .OrderBy(a => a.CreatedAt).ThenBy(a => a.Id).FirstOrDefaultAsync(ct);
                if (replacement is not null)
                {
                    replacement.IsDefault = true;
                    await db.SaveChangesAsync(ct);
                }
            }
            return true;
        }, ct);

    private async Task<T> WithUserLockAsync<T>(string userId, Func<Task<T>> action, CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(ct) : null;
        try
        {
            if (db.Database.IsSqlServer())
            {
                // Lock the existing parent row, including when the user has no addresses yet.
                // All address writes acquire this lock before reading or changing addresses.
                var users = await db.Database.SqlQuery<string>($"SELECT [Id] AS [Value] FROM [AspNetUsers] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {userId}")
                    .ToListAsync(ct);
                if (users.Count == 0) throw new KeyNotFoundException("User not found.");
            }
            var result = await action();
            if (transaction is not null) await transaction.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 or 1205 or 1222 })
        {
            throw new AddressConflictException("Addresses changed during your request. Reload them and retry.");
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222)
        {
            throw new AddressConflictException("Addresses are being changed by another request. Reload them and retry.");
        }
        // Disposing an uncommitted transaction rolls back both default changes together.
    }

    private async Task ClearDefaultAsync(string userId, CancellationToken ct)
    {
        var previous = await db.UserAddresses.Where(a => a.UserId == userId && a.IsDefault).ToListAsync(ct);
        foreach (var address in previous) address.IsDefault = false;
        if (previous.Count > 0) await db.SaveChangesAsync(ct);
    }

    private async Task<UserAddress> FindOwnedAsync(int id, string userId, CancellationToken ct) =>
        await db.UserAddresses.SingleOrDefaultAsync(a => a.Id == id && a.UserId == userId, ct)
        ?? throw new KeyNotFoundException("Address not found.");

    private static void CheckVersion(UserAddress address, byte[] rowVersion)
    {
        if (rowVersion is null || rowVersion.Length != 8)
            throw new InvalidOperationException("Supply the eight-byte row version returned by the address API.");
        if (!address.RowVersion.SequenceEqual(rowVersion)) throw new DbUpdateConcurrencyException();
    }

    private static void ApplyDetails(UserAddress address, AddressWriteDto dto)
    {
        address.Label = Optional(dto.Label);
        address.RecipientName = dto.RecipientName.Trim();
        address.PhoneNumber = AddressPhoneNumber.Normalize(dto.PhoneNumber);
        address.AddressLine1 = dto.AddressLine1.Trim();
        address.AddressLine2 = Optional(dto.AddressLine2);
        address.City = dto.City.Trim();
        address.State = dto.State.Trim();
        address.PostalCode = dto.PostalCode.Trim();
        address.CountryCode = dto.CountryCode.ToUpperInvariant();
    }

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static AddressResponseDto ToDto(UserAddress a) => new(a.Id, a.Label, a.RecipientName,
        a.AddressLine1, a.AddressLine2, a.City, a.State, a.PostalCode, a.CountryCode, a.IsDefault,
        a.RowVersion, DateTime.SpecifyKind(a.CreatedAt, DateTimeKind.Utc),
        a.UpdatedAt.HasValue ? DateTime.SpecifyKind(a.UpdatedAt.Value, DateTimeKind.Utc) : null, a.PhoneNumber);
}
