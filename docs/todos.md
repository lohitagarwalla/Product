# Todo API

All todo endpoints require `Authorization: Bearer <token>` from the existing
`POST /api/auth/register` or `POST /api/auth/login` flow. Ownership comes from
the token; clients cannot choose another user. Even admins can only access their own todos.

| Method | Route | Result |
| --- | --- | --- |
| GET | `/api/todos` | All your undeleted todos |
| GET | `/api/todos?isDone=true` | Your completed todos |
| GET | `/api/todos?isDone=false` | Your incomplete todos |
| POST | `/api/todos` | Create an incomplete todo; returns 201 |
| PATCH | `/api/todos/{id}/done` | Mark your todo done; returns 204 |
| DELETE | `/api/todos/{id}` | Soft-delete your todo; returns 204 |

Create request:

```json
{ "title": "Buy milk" }
```

Response example:

```json
{ "id": 1, "title": "Buy milk", "isDone": false }
```

Titles must contain non-whitespace text and be at most 250 characters.
Leading and trailing whitespace is trimmed. Lists return arrays, including `[]`
when empty, ordered by ID. Marking an already completed todo done succeeds.
Marking done requires no request body.

Unauthenticated requests return 401; invalid input returns 400. Missing,
deleted, and other users' item IDs return 404 for completion and deletion.
Deleted items never appear in any list.

The `AddTodoItems` EF Core migration creates the table, user foreign key, and
user/completion index. The existing startup initializer applies pending
migrations when the configured SQL Server database is available. To apply manually:

```powershell
dotnet ef database update --project src/ResourceBooking.Infrastructure --startup-project src/ResourceBooking.Web
```

Run integration tests (isolated EF in-memory database, real JWT authentication):

```powershell
dotnet test ResourceBookingApp.slnx --no-restore
```
