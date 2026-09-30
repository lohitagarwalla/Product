# Products and local images

## Implementation steps

1. Add Product, ImageAsset and ProductImage models and EF mappings.
2. Store files locally through IImageStorage; store relative keys and metadata in SQL.
3. Add product CRUD, draft/public visibility and admin authorization.
4. Add upload validation, ordering, alt text, viewing and downloading.
5. Schedule cleanup for interrupted uploads and removed images.
6. Add the migration and integration tests, then verify the model and build.

## Local setup

```powershell
dotnet run --project src/ResourceBooking.Web
```

The existing DbInitializer applies pending migrations at startup. The new
AddProductsAndImages migration adds Products, ImageAssets and ProductImages;
it does not rewrite existing business data. To apply it explicitly:

```powershell
dotnet ef database update --project src/ResourceBooking.Infrastructure --startup-project src/ResourceBooking.Web
```

ImageStorage in appsettings.json controls storage and limits. The default
App_Data/images is relative to the Web project's content root, outside wwwroot.
Files are ignored by Git and excluded from publishing. For a permanent local
location, override ImageStorage:RootPath with an absolute writable directory,
for example D:/ResourceBookingData/images. Changing the path does not move
existing images: copy files preserving their relative keys. Back up the files
together with the database.

Defaults: 5 MiB per file, 6000 pixels per dimension, 16 million total pixels,
10 images per product. JPEG, PNG and WebP are accepted; animation is rejected.
SkiaSharp detects the actual format and decodes the image to reject corrupt data.
Originals are retained, including embedded metadata. Thumbnails and metadata
stripping are not implemented in this version.

## API contract

All write endpoints and /manage require a JWT with the Admin role. Public reads
only expose published, non-deleted products. Admins can read individual drafts.
New products default to IsPublished=false. PUT replaces the editable fields;
send the full product payload, including IsPublished.

| Method and route | Request / result |
| --- | --- |
| GET /api/products?page=1&pageSize=20&search=phone | Published catalog: items, totalCount, page, pageSize; searches title or brand |
| GET /api/products/manage | Admin catalog including drafts; same query options |
| GET /api/products/{id} | Product details and ordered images |
| POST /api/products | Create product; returns 201 and Location |
| PUT /api/products/{id} | Replace editable product fields |
| DELETE /api/products/{id} | Soft delete; returns 204 |
| POST /api/products/{id}/images | multipart/form-data: file and optional altText; returns image metadata |
| PUT /api/products/{id}/images/order | JSON: {"imageIds":[12,10,11]}; include every image exactly once |
| PUT /api/products/{id}/images/{imageId} | JSON: {"altText":"Front view"} |
| DELETE /api/products/{id}/images/{imageId} | Detach immediately; cleanup removes bytes later |
| GET /api/images/{id}/content | Image bytes with correct Content-Type |
| GET /api/images/{id}/download | Same bytes with Content-Disposition: attachment |

Example product body:

```json
{
  "title": "Wireless headphones",
  "description": "Over-ear headphones with charging case",
  "brand": "Example",
  "price": 2499.00,
  "currency": "INR",
  "isPublished": false
}
```

Title and brand are required. Price is nonnegative with at most two decimal
places. Currency is a three-letter code normalized to uppercase; exchange rates
and a currency registry are outside this feature. IDs and timestamps are server generated.

Images contain id, sortOrder, altText, width, height, contentType, size, url and
downloadUrl. The first image is the cover. Storage keys and disk paths are never
returned to React. Errors use the existing ProblemDetails middleware: 400 for
invalid input, 401/403 for authentication/permissions, 404 for missing or hidden
objects, and 409 for overlapping changes. Reload before retrying a 409. The
concurrency token protects changes overlapping on the server; client ETags for
stale forms are not implemented.

## React workflow

1. Create a draft with JSON and Authorization: Bearer <token>.
2. For each selected image, send one multipart request. Upload sequentially for
   a product to avoid concurrent-ordering conflicts. Keep failed files for retry;
   each upload is independent, not an atomic batch.
3. Show the returned images, reorder them and edit alt text.
4. Publish using PUT with isPublished=true and the complete product payload.

```javascript
const form = new FormData();
form.append('file', file);
form.append('altText', 'Front view');
const response = await fetch(`${apiOrigin}/api/products/${productId}/images`, {
  method: 'POST',
  headers: { Authorization: `Bearer ${token}` },
  body: form // Let the browser set the multipart boundary.
});
if (!response.ok) throw new Error(await response.text());
const image = await response.json();
const imageUrl = new URL(image.url, apiOrigin).href;
```

For published images use the resolved URL in an img element. Resolve relative
URLs against the API origin, not React's origin. Draft images need an authenticated
fetch followed by URL.createObjectURL(blob); revoke it when replacing the image
or unmounting the component. Use authenticated fetch for draft downloads too.
A normal img element does not attach a JWT. The existing CORS policy allows
http://localhost:5173; configure other origins if your React address changes.

Product and image endpoints disable the global output cache. Image responses
also use Cache-Control: no-store so unpublished/deleted content is not subsequently
served from a browser cache. Previously downloaded copies cannot be revoked.

## Storage and cleanup

Core defines interfaces. Infrastructure handles local storage, validation, EF
operations and cleanup. Web owns HTTP and JWT authorization. No AWS is needed.

BaseEntity contains only Id, CreatedAt and UpdatedAt. Product, Resource, Booking
and TodoItem implement ISoftDeletable and declare IsDeleted themselves. The
DbContext converts deletion into an update only for entities implementing that
interface; their existing query filters hide deleted rows. ImageAsset inherits
BaseEntity for shared fields but does not implement ISoftDeletable, so cleanup
permanently removes its row. Both synchronous and asynchronous saves apply these
rules and update timestamps. SeparateSoftDeleteFromBaseEntity adds only the
nullable ImageAssets.UpdatedAt column; existing soft-delete columns are retained.

Before writing a file, the service saves a Pending asset with a cleanup deadline.
After upload, one database save attaches it and marks it Ready. Failed/interrupted
uploads stay Pending and are removed after 24 hours. Removed images become
inaccessible immediately and are scheduled for physical deletion. Soft-deleted
products retain images for 30 days by default. There is no restore endpoint yet.

The worker runs at startup and every five minutes, processing up to 100 due
assets per pass. Deletion failures are logged and retried; missing files are
handled idempotently. Live product references prevent deletion. Storage rejects
path traversal; keep its directory application-owned without filesystem links
to other directories.

Notes and profiles can reuse ImageAsset, IImageStorage and upload validation.
Add their relationships and extend access policies and cleanup reference checks
before enabling those features. Product visibility must not authorize private notes.

For AWS, implement IImageStorage using S3 and migrate files under the same keys.
Image IDs and API contracts can stay stable. Signed URLs can be added later.
This implementation and its native SkiaSharp dependency are tested on Windows;
add matching Linux native assets and verify decoding if choosing Linux hosting.

## Verification

```powershell
dotnet test ResourceBookingApp.slnx
dotnet ef migrations has-pending-model-changes --project src/ResourceBooking.Infrastructure --startup-project src/ResourceBooking.Web --no-build
```

Integration tests use isolated EF InMemory databases and temporary local image
directories. They cover JWT permissions, visibility, galleries, validation,
ordering, downloads, deletion, cleanup and concurrency. These tests do not
replace migration verification on SQL Server or browser testing in the React project.
