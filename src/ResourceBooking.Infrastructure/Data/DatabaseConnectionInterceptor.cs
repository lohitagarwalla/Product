using System.Data.Common;
using System.Transactions;
using Microsoft.AspNetCore.Http;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ResourceBooking.Core.Exceptions;

namespace ResourceBooking.Infrastructure.Data;

// Retry physical connection opening only. EF commands, SaveChanges and commits are
// deliberately left to the normal (non-retrying) SQL Server execution strategy.
public sealed class DatabaseConnectionInterceptor(IOptions<DatabaseConnectionRetryOptions> options,
    TimeProvider clock, IHttpContextAccessor http, ILogger<DatabaseConnectionInterceptor> logger)
    : DbConnectionInterceptor
{
    public override InterceptionResult ConnectionOpening(DbConnection connection,
        ConnectionEventData eventData, InterceptionResult result) =>
        ConnectionOpeningAsync(connection, eventData, result).AsTask().GetAwaiter().GetResult();

    public override async ValueTask<InterceptionResult> ConnectionOpeningAsync(DbConnection connection,
        ConnectionEventData eventData, InterceptionResult result, CancellationToken cancellationToken = default)
    {
        // Never try to recover an existing transaction by reopening its connection.
        if (result.IsSuppressed || eventData.Context?.Database.CurrentTransaction is not null
            || Transaction.Current is not null)
            return result;

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(options.Value.TimeoutSeconds), clock);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken,
            http.HttpContext?.RequestAborted ?? CancellationToken.None, timeout.Token);
        var started = clock.GetTimestamp();
        Exception? lastFailure = null;
        int? lastSqlNumber = null;
        var retries = 0;
        try
        {
            while (true)
            {
                linked.Token.ThrowIfCancellationRequested();
                try
                {
                    // Direct ADO.NET open does not recursively invoke EF interception.
                    await connection.OpenAsync(linked.Token).ConfigureAwait(false);
                    if (linked.IsCancellationRequested)
                    {
                        await connection.CloseAsync().ConfigureAwait(false);
                        linked.Token.ThrowIfCancellationRequested();
                    }
                    if (retries > 0)
                        logger.LogInformation("Database connection recovered after {Retries} retries in {ElapsedMs} ms.",
                            retries, clock.GetElapsedTime(started).TotalMilliseconds);
                    // EF still owns normal connection closing and transaction creation.
                    return InterceptionResult.Suppress();
                }
                catch (SqlException ex) when (TransientSqlNumber(ex) is not null)
                {
                    lastFailure = ex;
                    lastSqlNumber = TransientSqlNumber(ex);
                    linked.Token.ThrowIfCancellationRequested();
                    var delay = TimeSpan.FromSeconds(Math.Min(10, Math.Pow(2, Math.Min(retries, 4)))
                        * (0.8 + Random.Shared.NextDouble() * 0.4));
                    retries++;
                    logger.LogWarning("Database connection retry {Retry}; SQL error {SqlErrorNumber}; elapsed {ElapsedMs} ms.",
                        retries, lastSqlNumber, clock.GetElapsedTime(started).TotalMilliseconds);
                    await Task.Delay(delay, clock, linked.Token).ConfigureAwait(false);
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested
            || http.HttpContext?.RequestAborted.IsCancellationRequested == true)
        {
            throw;
        }
        catch (OperationCanceledException) when (timeout.IsCancellationRequested)
        {
            logger.LogWarning("Database connection exhausted its retry budget; SQL error {SqlErrorNumber}; elapsed {ElapsedMs} ms.",
                lastSqlNumber, clock.GetElapsedTime(started).TotalMilliseconds);
            throw new DatabaseUnavailableException(lastFailure);
        }
    }

    private static int? TransientSqlNumber(SqlException exception)
    {
        // Authentication/schema/configuration errors are not retried.
        foreach (SqlError error in exception.Errors)
            if (error.Number is 40613 or 40197 or 40501 or 10928 or 10929
                or 49918 or 49919 or 49920 or 10053 or 10054 or 10060 or 64 or 233 or -2)
                return error.Number;
        return null;
    }
}
