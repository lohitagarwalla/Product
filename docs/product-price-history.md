# Product price history

Product creation records a `Created` entry. Updates add a `PriceChanged` entry only when the
numeric price changes. Currency-only, title, description, publication, and image changes do
not add entries. Currency is not stored in the history.

The API takes the administrator ID from the authenticated user claims. The product write and
its history entry are saved in one EF Core transaction; a failed write or concurrency conflict
does not commit either. Existing request bodies and product responses remain compatible.
The existing product `Version` protects overlapping backend updates; it is not a client-supplied
version for detecting edits based on an old browser form.

## Read history

Administrators can call:

```http
GET /api/products/42/price-history?page=1&pageSize=20
Authorization: Bearer <admin-token>
```

The response has `items`, `totalCount`, `page`, and `pageSize`. Each entry has `id`, `productId`,
`previousPrice`, `newPrice`, `changedByUserId`, `changedByUserName`, `changedAtUtc`, and `entryType` (`Created`,
`PriceChanged`, or `Baseline`). Entries sort by timestamp descending, then ID descending.
Page numbers start at 1; page size is 1–100. A missing product returns 404. Anonymous callers
receive 401 and non-admin callers receive 403. History remains readable after soft deletion.

History is append-only through `ApplicationDbContext`: modifying/deleting tracked entries is
rejected for both synchronous and asynchronous saves. There are no history write endpoints.
Foreign keys restrict physical deletion of referenced products and users. These application
guards do not prevent someone with direct SQL write access from editing audit rows.

## Existing products and migration

`20261004153219_AddProductPriceHistory` adds the table, decimal(18,2) prices, foreign keys,
constraints, and a product/timestamp/ID index. It inserts one baseline for every existing
product, including soft-deleted products, with its current price, migration-time UTC timestamp,
null previous price, and null actor. It does not invent older prices or attribution. Rolling
this migration back drops the history table and all recorded history.

The application already applies pending migrations at startup. To apply explicitly:

```powershell
dotnet ef database update --project src/ResourceBooking.Infrastructure --startup-project src/ResourceBooking.Web --configuration Release --no-build
```

The companion `product-price-history-migration.sql` is an idempotent upgrade script from the
orders migration to the price-history migration. Generate a new script if later migrations are added.
SQL Server integration tests use uniquely named disposable databases to verify backfilling,
authorization, precision, unchanged-price behavior, retention, transactions, and concurrency.

The Admin-only response resolves changedByUserName to the actor's current first and last name,
falling back to the Identity username when both are blank. Unknown actors have a null name.
Names are read-time values, not historical snapshots; actor IDs remain stored for auditing.
This additive DTO change requires no database migration.
