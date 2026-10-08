# Refresh-token authentication

The backend issues 15-minute access JWTs and a separate seven-day refresh session for each successful login or registration. Refreshing rotates the refresh token but never extends the original session expiry. These defaults are configured in `src/ResourceBooking.Web/appsettings.json` under `AuthSession`.

## HTTP contract

| Endpoint | Request | Success |
| --- | --- | --- |
| `POST /api/auth/register` | Existing registration JSON | Existing auth JSON plus expiry timestamps; sets refresh cookie |
| `POST /api/auth/login` | Existing login JSON | Existing auth JSON plus expiry timestamps; sets refresh cookie |
| `POST /api/auth/refresh` | Refresh cookie and `X-Refresh-Token: 1`; no body or access token required | New access JWT and rotated refresh cookie |
| `POST /api/auth/logout` | Refresh cookie and `X-Refresh-Token: 1`; no body or access token required | Revokes that session, deletes cookie, returns the existing success message |

The `token` JSON field remains the access JWT. Successful authentication responses additionally contain UTC `accessTokenExpiresAt` and `refreshTokenExpiresAt`. The refresh-token secret is **never returned in JSON**. Auth responses use `Cache-Control: no-store`.

The cookie is named `__Secure-ResourceBooking.Refresh`, is host-only, and uses `HttpOnly`, `Secure`, `SameSite=None`, and `Path=/api/auth`. Use HTTPS for the API, including local development (`https://localhost:7030`). SameSite=None supports the existing HTTP frontend calling the HTTPS backend; the origin check and required custom header protect the cookie endpoints against CSRF. Browser third-party-cookie restrictions can still prevent cookies in cross-site deployments; prefer deploying the frontend and API on the same site.

The refresh and logout endpoints allow requests without an access JWT so an expired access token cannot prevent session renewal or logout. Possession and validation of the refresh cookie authorize the session operation. They never use a caller-supplied user ID to choose a session.

Error behavior:

- Missing, malformed, unknown, expired, revoked, or reused refresh token: `401`, generic error, refresh cookie deleted.
- Missing or incorrect `X-Refresh-Token: 1` header: `400`, no session mutation.
- A supplied Origin outside the configured allowlist or the API's own origin: `403`, no session mutation. The literal `null` origin is rejected.
- Logout with no/unknown/already-revoked token: idempotent `200` and cookie deletion, provided the CSRF checks pass.
- Login and registration require `application/json`; existing credential and validation failures retain their existing status codes.

## Later frontend integration

No React files were changed. Future frontend work must:

1. Send `credentials: 'include'` (fetch), or `withCredentials: true` (Axios), for login, registration, refresh and logout so the browser accepts and sends the cookie.
2. Keep sending `Authorization: Bearer <token>` for protected business APIs.
3. Send `X-Refresh-Token: 1` for refresh and logout. JavaScript neither reads nor supplies the raw refresh secret.
4. Coordinate a single refresh request across simultaneous API failures, including browser tabs if they share the cookie. Retry the original API request at most once with the new access token. Do not refresh in response to `403` or loop on failed refreshes.
5. On refresh `401`, clear the client access token and return to login. A lost rotation response can require logging in again: blindly replaying the old cookie intentionally revokes the session.
6. Clear the client access token on logout. Previously issued access JWTs remain valid until expiry; logout revokes future refreshes.

Example refresh request for an API client with a cookie jar:

```http
POST /api/auth/refresh HTTP/1.1
Host: localhost:7030
X-Refresh-Token: 1
Cookie: __Secure-ResourceBooking.Refresh=<value from login Set-Cookie>
```

`AuthSession:AllowedOrigins` controls both credentialed CORS and the explicit Origin check. It defaults to `http://localhost:5173`. Configure exact origins without trailing slashes for other frontend addresses; wildcards are not accepted. Non-browser clients may omit Origin, but must still send the required refresh/logout header.

The existing `rememberMe` request field does not change these approved defaults: every new session lasts at most seven days.

## Database and runtime flow

`AuthController -> IAccountService/AccountService -> ApplicationDbContext -> SQL Server`.

- `RefreshSessions` stores user ownership, the Identity security stamp at login, creation/absolute expiry timestamps, and revocation timestamp.
- `RefreshTokens` stores a SHA-256 hash of a cryptographically random 64-byte secret, session ownership, creation time, and consumption time. It has a unique hash index.
- A successful refresh marks its old token consumed and inserts its replacement in one transaction. The access JWT uses the user's current roles.
- SQL Server `UPDLOCK, HOLDLOCK` on the session row serializes refresh, replay revocation and logout across API instances. The non-relational test provider uses bounded process-local locks; it is not the production concurrency mechanism.
- Reusing any consumed token revokes that entire session, including all descendants, but leaves other login sessions alone. Simultaneous duplicate refreshes intentionally have the same outcome.
- A deleted user, active Identity lockout, or changed Identity security stamp prevents further refreshes. Password-reset/change flows must use Identity operations that update the security stamp.
- Retain consumed-token records while their session is unexpired to detect replay. Expired sessions can later be removed by an operational retention job; deleting a session cascades to its tokens. This change does not add a background cleanup job.

## Migration and local commands

`20261008131701_AddRefreshSessions` adds the two tables, their foreign keys, and indexes. It does not alter existing user records or invalidate existing access JWTs. Existing users obtain a refresh cookie the next time they log in. Existing eight-hour access tokens keep their original expiration.

From `D:\CSharp\ResourceBookingApp`, build and apply migrations to your configured database:

```powershell
dotnet build ResourceBookingApp.slnx --configuration Release --disable-build-servers -m:1 -p:UseSharedCompilation=false
dotnet ef database update --project src/ResourceBooking.Infrastructure --startup-project src/ResourceBooking.Web --configuration Release --no-build
dotnet run --project src/ResourceBooking.Web --configuration Release --no-build --launch-profile https
```

The existing startup initializer also applies pending migrations when it can connect to SQL Server. A migration being generated or a build succeeding does not prove it was applied to your application database.

The JWT signing key still uses the project's existing configuration and fallback behavior. Supply a private `JwtSettings:Secret` before deployment; signing-key hardening is a separate existing concern.

## Verification

Verification on 2026-10-08: Release compilation passed, and EF reported no pending model changes. All 17 new in-memory authentication tests passed. The broader non-SQL run passed 144 of 145 tests; the existing `BookingEndpointsTests.GetAllResources_ReturnsSuccessAndJsonContent` failed because its resource catalog was empty. All four SQL refresh tests failed during setup before their target assertions because LocalDB reported `SQL Server process failed to start`. Real migration application, locking, constraints and rollback remain unverified. The application's database was not updated as part of this work.

```powershell
# HTTP/service behavior with the isolated EF InMemory provider
dotnet test tests/ResourceBooking.IntegrationTests --configuration Release --filter FullyQualifiedName~RefreshTokenTests

# Real migration constraints, duplicate-refresh locks, logout races and rollback
dotnet test tests/ResourceBooking.IntegrationTests --configuration Release --filter FullyQualifiedName~RefreshTokenSqlServerTests
```

SQL tests reuse `OrderWebApplicationFactory`: they create and delete only a unique `ResourceBookingOrderTests_<guid>` database, never the application database. They use LocalDB by default, or the server connection in `ORDER_TEST_SQLSERVER_CONNECTION`; the factory replaces the database name. The server account needs permission to create/drop test databases. In-memory tests and manual cookie-header tests do not verify browser cookie storage policies or real SQL Server locking.

Design references: [OAuth refresh-token rotation](https://www.rfc-editor.org/rfc/rfc9700.html#section-4.14) and [ASP.NET Core credentialed CORS](https://learn.microsoft.com/en-us/aspnet/core/security/cors?view=aspnetcore-10.0#credentials-in-cross-origin-requests).
