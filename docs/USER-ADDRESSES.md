# User addresses

All endpoints require a JWT bearer token and operate only on the current user's addresses.
Ownership comes from the token, not the request body. Another user's address returns 404.

| Method | Route | Result |
| --- | --- | --- |
| GET | /api/profile/addresses | 200, default first, then oldest first |
| GET | /api/profile/addresses/{id} | 200, one owned address |
| POST | /api/profile/addresses | 201, address and Location header |
| PUT | /api/profile/addresses/{id} | 200, updated address |
| PUT | /api/profile/addresses/{id}/default | 200, selected default |
| DELETE | /api/profile/addresses/{id} | 204 |

## Create an address

```json
{
  "label": "Home",
  "recipientName": "Lohit",
  "addressLine1": "12 Main Road",
  "addressLine2": "Apartment 4",
  "city": "Bengaluru",
  "state": "Karnataka",
  "postalCode": "560001",
  "countryCode": "IN",
  "makeDefault": false
}
```

`label` and `addressLine2` are optional. Other address details are required.
`countryCode` defaults to IN if omitted, accepts two ASCII letters, and is stored uppercase.
For IN, the postal code must contain six ASCII digits and cannot start with zero.
Other countries accept a nonblank postal code of up to 20 characters; country-specific
postal validation beyond India is not implemented. Country codes are validated for format,
not against a country registry. Text is trimmed before storage.

The first address becomes default regardless of `makeDefault`. Additional addresses
become default only when `makeDefault` is true or the default endpoint is called.
An ordinary update cannot change default status. Deleting a default promotes the oldest
remaining address (CreatedAt, then Id); deleting the last address leaves no default.
Deletion is permanent. Addresses are not yet connected to checkout or existing orders.

## Update, select default, and delete

Responses contain `rowVersion`, a Base64 string. Supply that exact value in the update
body together with address details. For default selection and deletion, send a JSON body:

```json
{
  "rowVersion": "<value returned by GET>"
}
```

DELETE also requires this body and `Content-Type: application/json`.
Missing or malformed versions return 400. Stale versions return 409; reload the address
list and retry with the latest data. Switching defaults or promoting a replacement changes
affected rows' versions, so refresh the list after these operations.

## SQL Server guarantees

The model declares a unique filtered index on UserId WHERE IsDefault = 1.
All address mutations acquire a transaction-scoped UPDLOCK/HOLDLOCK on the owner's
AspNetUsers row before reading addresses. This serializes writes for a user even when
there are initially no address rows. Old defaults are saved as false before new defaults
are saved as true, within one transaction. The service maintains one default for users
with addresses; the database index enforces at most one default. Rowversion checks prevent
stale edits. Deadlocks, lock timeouts, and duplicate-key conflicts return 409.

## Migration commands (run manually)

No address migration has been generated or applied by this implementation.
Run PowerShell in the solution root:

```powershell
Set-Location 'D:\CSharp\ResourceBookingApp'
dotnet build ResourceBookingApp.slnx --configuration Release
dotnet ef migrations add AddUserAddresses --project src/ResourceBooking.Infrastructure --startup-project src/ResourceBooking.Web --context ApplicationDbContext --configuration Release --no-build
```

Review the generated migration in src/ResourceBooking.Infrastructure/Migrations. It should
create UserAddresses, its Identity user foreign key, rowversion, and both address indexes.
Then either run the following (which rebuilds to include the new migration):

```powershell
dotnet ef database update --project src/ResourceBooking.Infrastructure --startup-project src/ResourceBooking.Web --context ApplicationDbContext --configuration Release
```

Or rebuild and restart the API: DbInitializer applies pending migrations on startup.
Check startup logs for failures. Existing users start with no addresses.

## Verification

The address endpoint tests use an in-memory database to check authentication, ownership,
validation, lifecycle/default rules, and simulated stale-version handling. A SQL Server
schema-generation test checks the filtered unique index, foreign key, and rowversion
mapping without opening a connection. These tests do not verify SQL Server execution,
transaction rollback, generated rowversions, or simultaneous database writes. Run SQL-backed
verification after creating and applying the migration.
