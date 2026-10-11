# Automatic database migrations on Azure App Service

At every application startup, `Program.cs` awaits `DbInitializer.SeedAsync` before
`app.Run()`. For SQL Server, the initializer calls `Database.MigrateAsync()` before
seeding roles. EF Core uses `__EFMigrationsHistory` to apply only pending migrations
included in the deployed assembly. Restarts with no pending migrations do not
reapply them. The EF InMemory provider used by integration tests skips migration.

If migration or seeding throws, startup logs a critical error and stops. Check
App Service application logs for the failure; the API will not serve requests
until initialization succeeds. Demo admin creation remains Development-only.

## Azure configuration

Create the target Azure SQL database and configure the App Service environment
variable `ConnectionStrings__DefaultConnection` with its connection string.
Alternatively, configure an App Service connection string named
`DefaultConnection` with type SQLAzure. Keep credentials outside source control.
Ensure SQL network/firewall rules allow the App Service to connect and that its
database identity has permissions to apply the migration DDL and seed roles.

## Future model changes

Create and review an EF migration locally, from the repository root:

```powershell
dotnet ef migrations add DescribeYourChange --project src/ResourceBooking.Infrastructure --startup-project src/ResourceBooking.Web
```

Commit the migration and model snapshot, then publish/deploy the API. On startup,
the deployed application applies that migration to its configured database.
Changing an entity alone does not create a migration at runtime. No separate
`dotnet ef database update` command or EF CLI installation on App Service is needed.

## Deployment considerations

Test migrations against a representative SQL database before production deployment.
EF Core 10 provides a migration lock for concurrent migration execution, but this
does not prevent an older app instance or deployment slot from using the database
while its schema changes. Use compatible schema changes and coordinate deployments.
Long migrations also delay app startup. For deployments requiring tighter control,
apply a reviewed migration script or bundle in the release pipeline instead.

Reference: [EF Core applying migrations](https://learn.microsoft.com/ef/core/managing-schemas/migrations/applying).
