# Database connection resilience

The application's SQL Server `ApplicationDbContext` registers `DatabaseConnectionInterceptor`.
It opens the ADO.NET connection before EF executes a command and suppresses EF's duplicate open.
EF retains connection lifetime management. Both synchronous and asynchronous EF opens are covered.
This applies to catalog reads, Identity login/registration, refresh/logout, cart/address/order
operations, and background jobs that resolve the configured context through dependency injection.
Separately constructed contexts or direct ADO.NET connections need their own configuration.

## Retry scope

- Retry only selected temporary SQL errors thrown by `OpenAsync`, including Azure SQL serverless
  auto-resume error 40613, throttling, connection timeouts and connection resets.
- Use increasing delays (1, 2, 4, 8, then 10 seconds) with jitter.
- Never retry a command, SaveChanges, or transaction commit. Global EF execution retries remain disabled.
- Do not intercept/retry opening when EF already has a transaction or an ambient transaction exists.
- Invalid SQL credentials and configuration/schema errors fail promptly.
- The original refresh-specific lookup loop and its option have been removed.

## Configuration and cancellation

`DatabaseConnectionRetry:TimeoutSeconds` defaults to 90 and accepts 1-90 seconds.
Set `DatabaseConnectionRetry__TimeoutSeconds` in Azure App Service to override it.
The budget is **per connection-opening operation**, including attempts and waits, not a total
HTTP request deadline. Cancellation is cooperative; provider cleanup can add latency.
EF cancellation and HTTP request-abort tokens both stop retries, including service methods
that do not explicitly forward a request token. Background jobs use the EF caller's token.

Clients/proxies must allow enough time for wake-up plus normal processing. A 120-second client
timeout is a starting point for a 90-second open budget, not a guarantee for multi-step requests.
The React auth client currently allows 120 seconds for refresh but only 30 seconds for login and
registration. Those requests can still time out before wake-up completes; coordinate client
timeouts separately before expecting them to wait through a full cold start.

## Failures and safe replay

Exhausted connection-opening budgets return generic HTTP 503 ProblemDetails through the global
middleware. Refresh does not delete its cookie for this exception. Logs include retry counts,
SQL error numbers and elapsed time, not connection strings or tokens.

A 503 does not promise that an entire multi-step request made no earlier changes. Registration,
for example, performs several saves. Do not automatically replay writes based solely on status.
Retain checkout request IDs and refresh replay protection. Command/commit failures continue to
use the existing server/concurrency error handling; their outcome may require verification.

## Validation and deployment

No database migration is required. Tests inject real SqlException instances into a simulated
ADO.NET connection and exercise EF connection ownership, sync/async recovery, cancellation,
timeouts and the no-command/no-commit-replay boundary. Refresh HTTP regression tests verify
cookie preservation and 503 responses. These do not prove a real Azure pause/resume or network
failure: after deployment, exercise catalog/login/refresh against an auto-paused test database
and inspect timings and SQL retry logs. Do not fault-inject against production writes.
