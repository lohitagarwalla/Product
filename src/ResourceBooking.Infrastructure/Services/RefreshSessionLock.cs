using Microsoft.EntityFrameworkCore;
using ResourceBooking.Infrastructure.Data;

namespace ResourceBooking.Infrastructure.Services;

internal static class RefreshSessionLock
{
    // Bounded process-local locks for the non-relational test provider only.
    private static readonly SemaphoreSlim[] TestLocks = Enumerable.Range(0, 64)
        .Select(_ => new SemaphoreSlim(1, 1)).ToArray();

    public static async Task<T> RunAsync<T>(ApplicationDbContext db, Guid sessionId,
        Func<Task<T>> action, CancellationToken ct)
    {
        if (db.Database.IsSqlServer())
        {
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            // Refresh, replay revocation and logout serialize across all API instances.
            await db.Database.SqlQuery<Guid>($"SELECT [Id] AS [Value] FROM [RefreshSessions] WITH (UPDLOCK, HOLDLOCK) WHERE [Id] = {sessionId}")
                .ToListAsync(ct);
            var result = await action();
            await transaction.CommitAsync(ct);
            return result;
        }

        if (db.Database.IsRelational())
            throw new NotSupportedException("Refresh sessions require SQL Server locking for relational storage.");

        var gate = TestLocks[(uint)sessionId.GetHashCode() % (uint)TestLocks.Length];
        await gate.WaitAsync(ct);
        try { return await action(); }
        finally { gate.Release(); }
    }
}
