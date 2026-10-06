# Saved carts and checkout

Saved carts restore a user's items and selected address when they return. Checkout
uses the items and full address submitted to `POST /api/orders`, independently of
the saved cart. Cart requests and responses no longer expose a cart row version.

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
  "deliveryAddress": {
    "recipientName": "Lohit", "phoneNumber": "9876543210", "addressLine1": "House 10", "addressLine2": null,
    "city": "Hyderabad", "state": "Telangana", "postalCode": "500001", "countryCode": "IN"
  }
}
```

Missing/invalid address details, empty/null items, invalid quantities/IDs,
duplicates, unavailable products, mixed currencies, and excessive totals return
400. The service uses current database prices and saves product and address
snapshots. No saved cart or saved-address record is required. Submitted products
and quantities need not match the saved cart. Editing/deleting the saved address
does not invalidate the submitted snapshot.

Order creation and removal of purchased cart products share one SQL transaction.
Each saved cart item whose product ID appears in the order is removed entirely,
even when its saved quantity differs. Other products and selectedAddressId remain.
Failures roll back both order creation and cart removal. Existing server-side user
locks serialize cart, address and checkout writes; clients do not manage versions.

201 creates the order; retrying the same requestId with the same normalized address
and items returns 200 with the existing order. Item order is irrelevant. Changed
items or address details with the same requestId return 409. A replay never removes
cart items again. Keep the original request snapshot for retries after a lost
response. This replaces the previous items-only/cart-version request hashes;
old in-flight attempts should be reviewed against order history before starting anew.

The optional phoneNumber follows the [address phone rules](USER-ADDRESSES.md).
Cart selectedAddress and order deliveryAddress responses include the normalized phone.
Checkout snapshots the submitted phone independently of the account or saved address.
Equivalent local/+91 inputs replay the same order; changing the phone with the same
requestId returns 409. Absent/blank phones preserve the existing address-based hash
format, allowing retries for orders created before phone support.

## Database and verification

`AddPhoneNumbersToAddresses` adds nullable nvarchar(13) columns to UserAddresses and
the delivery snapshot in Orders. Existing rows keep null; account phones are not
backfilled. Apply the migration before running this version of the API.
Existing cart rowversion
and nullable order address columns remain. The existing saved-cart migration must
still be applied on databases that do not have those tables/columns.

CartEndpointsTests uses InMemory with a test rowversion simulator for contract
checks. CartSqlServerTests additionally verifies simultaneous retries, quantity
writes and rollback on a dedicated SQL Server database. InMemory tests cannot
verify SQL Server constraints, locks or rollback. Set ORDER_TEST_SQLSERVER_CONNECTION
to a working test server when LocalDB is unavailable.
