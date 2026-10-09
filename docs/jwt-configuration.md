# JWT signing-key configuration

The backend requires `JwtSettings:Secret` in every environment. Startup rejects a missing, blank, or shorter-than-32-UTF-8-byte secret before database initialization. Generate a random value; length validation cannot establish randomness. Never reuse the previous public fallback key.

Token issuance and bearer validation use the same validated `IOptions<JwtSettings>` configuration. Issuer and audience retain the existing defaults (`ResourceBookingApi` and `ResourceBookingClients`); explicit blank values are rejected.

## Local development (PowerShell)

Run from `D:\CSharp\ResourceBookingApp` once to create a local development secret:

```powershell
$jwtKeyBytes = New-Object byte[] 32
$jwtKeyGenerator = [System.Security.Cryptography.RandomNumberGenerator]::Create()
$jwtKeyGenerator.GetBytes($jwtKeyBytes)
$jwtKeyGenerator.Dispose()
dotnet user-secrets set "JwtSettings:Secret" ([Convert]::ToBase64String($jwtKeyBytes)) --project src/ResourceBooking.Web
Remove-Variable jwtKeyBytes, jwtKeyGenerator
```

User Secrets are loaded in Development and kept outside the repository. They are a development convenience, not an encrypted production secret store. The project contains only a UserSecretsId, not a signing key. Existing local credentials are not created or overwritten automatically by this change.

## Production

Generate a separate random key and supply it through your hosting secret store/configuration provider as `JwtSettings:Secret`, or securely inject the environment variable `JwtSettings__Secret`. All API instances must use the same key, issuer, and audience. Do not put the key in appsettings files, launch profiles, source control, logs, or container images.

Restart/redeploy all instances when changing these settings. This implementation uses a single key and does not implement overlapping-key rotation. Replacing the key invalidates existing access JWTs; valid refresh sessions can obtain JWTs signed with the new key. Replacing a key does not revoke refresh sessions.

## Tests

Both in-memory and SQL test factories generate private, per-host random keys in memory. Tests require no developer or deployment secrets.

```powershell
dotnet test tests/ResourceBooking.IntegrationTests --configuration Release --filter 'FullyQualifiedName~JwtConfigurationTests|FullyQualifiedName~RefreshTokenTests'
```
