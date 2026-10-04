# Orders

OrdersController -> IOrderService / OrderService -> ApplicationDbContext.
This backend supports order creation, user/admin searches, shipping, delivery,
cancellation, and an append-only status history. Payments, refunds, stock,
shipping-provider integration, and frontend pages are not included.

## Database and setup

`AddOrdersAndStatusHistory` adds `Orders`, `OrderItems`, and
`OrderStatusHistory`. It does not rewrite existing data. The existing
DbInitializer applies pending migrations when the API starts. To apply explicitly:

```powershell
dotnet build ResourceBookingApp.slnx
dotnet ef database update --project src/ResourceBooking.Infrastructure --startup-project src/ResourceBooking.Web
```

For production, review and apply an idempotent migration script through a
controlled deployment step, with a backup and recovery plan. The app's existing
automatic startup migration/default admin setup needs a separate production
configuration review before deployment.

```powershell
dotnet ef migrations script --idempotent --project src/ResourceBooking.Infrastructure --startup-project src/ResourceBooking.Web --output artifacts/orders-migrations.sql
```

`Order.RowVersion` is a SQL Server-generated `rowversion` (`byte[]` with
`IsRowVersion()`). The JSON value is Base64. It is a concurrency marker, not a
timestamp. This implementation targets SQL Server, including hosted SQL Server;
changing the database engine requires changing this mapping.

Orders and items do not implement ISoftDeletable. Cancellation retains the order.
Foreign keys restrict hard deletion of referenced products, users, and orders.
Order history has no edit/delete API; tracked updates and deletes are also rejected
by ApplicationDbContext. This guard does not prevent a privileged database
operator from using direct SQL.

## Authentication and API

Every endpoint requires a bearer JWT. Ownership and admin access come from JWT
claims, never from client-supplied user IDs. Responses disable output/browser
caching. Another user's order returns 404 unless the caller is an admin.

| Method | Route | Access / result |
| --- | --- | --- |
| POST | `/api/orders` | Create for current user; 201 + Location, or 200 on replay |
| GET | `/api/orders` | Current user's orders (including when caller is admin) |
| GET | `/api/orders/manage` | Admin: all users' orders |
| GET | `/api/orders/{id}` | Owner or admin: items, current status and full history |
| POST | `/api/orders/{id}/cancel` | Owner or admin: optional reason |
| POST | `/api/orders/{id}/ship` | Admin only |
| POST | `/api/orders/{id}/deliver` | Admin only |

### Create

```json
{
  "requestId": "1a193494-a7d3-4393-994c-23cbd90e6295",
  "items": [
    { "productId": 1, "quantity": 2 },
    { "productId": 2, "quantity": 1 }
  ]
}
```

- Generate a new UUID for each checkout attempt. Retain it while retrying that
  same checkout after a lost response. A deliberate new order uses a new UUID.
- A request must contain 1–100 distinct product IDs with quantity 1–1000 each.
  Null items, duplicates, and missing/empty request IDs are invalid.
- Products must be published and not deleted, and all must share one currency.
- The server reads current prices and calculates totals. Clients cannot set
  prices, totals, ownership, status, or history. Money has two decimal places;
  the maximum supported order total is 9999999999999999.99.
- Title and unit price are saved on each item. Subsequent catalog edits and
  deletion do not change order totals, title searches, or detail visibility.
- Current published product images may be included as `imageUrl`. This is not
  an image snapshot: if absent or the URL no longer works, show a placeholder.
- The unique `(UserId, RequestId)` index prevents concurrent duplicate orders.
  Reusing the key with the same products/quantities returns the existing order
  in its current state, even if catalog data changed. Item order is irrelevant.
  Reusing it with different products/quantities returns 409.
- Creation uses a transaction with repeatable-read catalog access, so catalog
  edits cannot invalidate the product snapshots between validation and commit.

An order response contains `id`, `orderNumber`, `userId`, `status`, `currency`,
`totalAmount`, UTC lifecycle timestamps, cancellation actor/reason, `rowVersion`,
`items`, and `statusHistory`. Monetary values are JSON numbers; frontend clients
must preserve decimal precision and use the server's totals as authoritative.

### Search

```text
GET /api/orders?search=phone&status=Placed&page=1&pageSize=20
GET /api/orders/manage?fromUtc=2026-10-01T00:00:00Z&toUtc=2026-11-01T00:00:00Z
```

`search` matches order number or saved product title, up to 200 characters.
`status` accepts Placed, Shipped, Delivered, or Cancelled. Omit filters to include
all matching orders, including cancelled ones. `fromUtc` is inclusive and `toUtc`
exclusive; they filter creation time. Use ISO 8601 timestamps with an explicit
offset or Z. Invalid or reversed ranges return 400.

Results use `items`, `totalCount`, `page`, and `pageSize`. Page starts at 1,
pageSize defaults to 20 and is capped at 100. Results sort by CreatedAt descending,
then Id descending. Summaries include identity, owner, status, currency, total,
total quantity and creation time. Fetch details for items, history and rowVersion.

### Status changes

| Current status | Owner cancellation | Admin cancellation | Admin progression |
| --- | --- | --- | --- |
| Placed | Yes | Yes | Shipped |
| Shipped | Yes | Yes | Delivered |
| Delivered | No | No | None |
| Cancelled | Already cancelled | Already cancelled | None |

Send the latest `rowVersion` from an order detail or mutation response:

```json
{
  "rowVersion": "AAAAAAAAB9E=",
  "reason": "Customer requested cancellation"
}
```

`reason` is only accepted by the cancel DTO, is optional for both owners and
admins, and is capped at 1000 characters. Omit it for ship/deliver requests.
Blank cancellation reasons become null. Delivered and Cancelled are terminal;
delivery requires the order to have been shipped first.

Successful mutations return the updated order and new rowVersion. Repeating a
transition to the current status returns the current order, even with the old
well-formed version, without adding history or replacing the original actor,
timestamp, or reason. Other stale updates return 409: reload the order and have
the caller reassess the intended action rather than blindly retrying it.

Shipping/delivery/cancellation record their timestamps. Cancellation also records
CancelledByUserId and CancellationReason. Cancelling a shipped order currently
changes application state only; it does not recall a shipment or issue a refund.

### History and atomicity

Each `statusHistory` entry includes `id`, `previousStatus`, `newStatus`,
`changedByUserId`, `changedAtUtc`, and optional `reason`. Creation records
null -> Placed. Entries are returned oldest first, ordered by timestamp then ID.

Every status change and its history entry are saved in one transaction. If the
rowversion check or a history insert fails, neither change commits. Simultaneous
delivery and cancellation cannot both succeed. The loser receives 409 and adds
no history. There is no separate client endpoint for writing history.

### Errors

- 400: malformed input, invalid quantities/filters, unavailable products, mixed
  currencies, excessive amounts, missing or malformed eight-byte rowVersion.
- 401: authentication missing or invalid.
- 403: non-admin calling an admin action.
- 404: missing order or another user's order.
- 409: stale rowVersion, forbidden status transition, or changed checkout payload
  using an existing request ID.

## Tests

```powershell
dotnet test ResourceBookingApp.slnx
```

Order tests use SQL Server LocalDB on Windows by default. On another machine,
set `ORDER_TEST_SQLSERVER_CONNECTION` to a SQL Server connection string with
permission to create and drop test databases. The fixture replaces InitialCatalog
with a unique `ResourceBookingOrderTests_<guid>` database, applies real migrations,
and deletes only that database on disposal. It never targets the application DB.

The suite covers real JWT permissions, totals, snapshots after product deletion,
search/date/status/pagination validation, optional cancellation reasons, terminal
states, malformed/stale versions, simultaneous duplicate creation, simultaneous
delivery/cancellation, history immutability, and rollback after a failed history
insert. Existing tests for other features retain their in-memory provider.
