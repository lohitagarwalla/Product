using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ResourceBooking.Infrastructure.Data;

namespace ResourceBooking.Infrastructure.Services;

// Cart writes and address writes lock the same user row, including first-cart creation.
internal static class UserCartLock
{
    public static async Task<T> RunAsync<T>(ApplicationDbContext db, string userId, Func<Task<T>> action, CancellationToken ct)
    {
        await using var transaction = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.RepeatableRead, ct) : null;
        try
        {
            if (db.Database.IsSqlServer())
            {
                var users = await db.Database.SqlQuery<string>($"SELECT [Id] AS [Value] FROM [AspNetUsers] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {userId}").ToListAsync(ct);
                if (users.Count == 0) throw new KeyNotFoundException("User not found.");
            }
            else if (!await db.Users.AnyAsync(u => u.Id == userId, ct))
                throw new KeyNotFoundException("User not found.");
            var result = await action();
            if (transaction is not null) await transaction.CommitAsync(ct);
            return result;
        }
        catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 or 1205 or 1222 })
        {
            throw new DbUpdateConcurrencyException("The cart changed during your request. Reload it and retry.", ex);
        }
        catch (SqlException ex) when (ex.Number is 1205 or 1222)
        {
            throw new DbUpdateConcurrencyException("The cart is being changed by another request. Reload it and retry.", ex);
        }
    }

}
