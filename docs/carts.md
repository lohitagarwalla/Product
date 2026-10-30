# Saved carts and checkout

Saved carts restore a user's items and selected address when they return. Checkout
uses the submitted items and an owned saved address identified by `addressId` in
`POST /api/orders`, independently of the cart selection. Cart requests and responses no longer expose a cart row version.

## Cart API

All routes require authentication and return the current user's complete cart.

| Method | Route | Request |
| --- | --- | --- |
| GET | `/api/cart` | None |
| PUT | `/api/cart/items/{productId}` | `{ "quantity": 3 }` |
| DELETE | `/api/cart/items/{productId}` | No body |
| DELETE | `/api/cart/items` | No body |
| PUT | `/api/cart/address` | `{ "addressId": 12 }` or `{ "addressId": null }` |

Quantities are absolute values, from 1 to 1000. A cart holds at most 100 distinct
products. Subsequent writes use the latest saved state without requiring a client
version token. Products must be published when added/updated. Unavailable saved
products remain visible with `available: false`. Address selection is restricted
to addresses owned by the caller. Editing a saved address does not update the cart;
deleting the selected address clears its selection. Changing the profile default
does not replace an explicit selection.

Responses contain `id`, `selectedAddressId`, `selectedAddress`, and `items`.
The selected address uses AddressResponseDto, including its own address-edit
version token. Address-edit and order-status version checks remain unchanged.

## Checkout

`POST /api/cart/checkout` has been removed. Use `POST /api/orders`:

```json
{
  "requestId": "1a193494-a7d3-4393-994c-23cbd90e6295",
  "items": [{ "productId": 10, "quantity": 2 }],
  "addressId": 12
}
```

Missing/nonpositive address IDs, empty/null items, invalid quantities/IDs,
duplicates, unavailable products, mixed currencies, and excessive totals return
400. A nonexistent address or an address belonging to another user returns 404.
The service queries UserAddresses by both address ID and the authenticated user ID.
It snapshots the saved address (including its phone) and current product prices.
Client-supplied deliveryAddress fields are not used. A saved cart is optional;
a saved address owned by the caller is required.

Order creation commits first. Purchased products are then deleted from the cart in
a separate transaction without quantity or cart row-version checks. Matching
lines are removed entirely and selectedAddressId resets to the caller's current
default address (or null if none exists). Other products remain. Both cleanup
writes commit together; a replay does not reset a later address selection. Cleanup
failure is logged and leaves the order committed. Cleanup is best effort, with no
background retry. Order creation no longer acquires the shared user lock or uses
RepeatableRead; ordinary cart/address operations keep their existing locking.

201 creates the order; retrying the same requestId with the same addressId and
items returns 200 with the original order. Item order is irrelevant. Changing the
addressId or items with the same requestId returns 409. Saved-address edits or
deletion after success do not change the order or prevent replay. A replay never
removes cart items again. New orders always verify current address ownership.

The frontend stores address text for display, but sends only addressId, requestId,
and items. Older pending attempts without addressId cannot be automatically
retried: review order history before starting anew. Existing orders with the old
address-content request hash remain readable; reusing their key with the new
request format returns 409. No database migration is required for this contract change.

## Database and verification

`AddPhoneNumbersToAddresses` adds nullable nvarchar(13) columns to UserAddresses and
the delivery snapshot in Orders. Existing rows keep null; account phones are not
backfilled. Apply the migration before running this version of the API.
Existing cart rowversion
and nullable order address columns remain. The existing saved-cart migration must
still be applied on databases that do not have those tables/columns.

CartEndpointsTests uses InMemory with a test rowversion simulator for contract
checks. CartSqlServerTests additionally verifies simultaneous retries, quantity
writes, address ownership, and cleanup-failure isolation on a dedicated SQL Server database. InMemory tests cannot
verify SQL Server constraints, locks or rollback. Set ORDER_TEST_SQLSERVER_CONNECTION
to a working test server when LocalDB is unavailable.
