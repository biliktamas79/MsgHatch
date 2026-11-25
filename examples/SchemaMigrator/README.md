# SchemaMigrator

A console app of a pipeline that applies the MsgHatch schema migrations on SQL Server, PostgreSQL, or MySQL (D320). In an enterprise setup, the pipeline has the rights to change the schema, and the service has only the rights to read and write rows. This app is the migration step of such a pipeline.

- It applies the migrations of `MsgHatch.Data.EFCore.SqlServer` and of `MsgHatch.Subscriptions.Data.EFCore.SqlServer` with `MigrateMsgHatchAsync`, or with the provider `PostgreSql` the migrations of `MsgHatch.Data.EFCore.PostgreSql` and of `MsgHatch.Subscriptions.Data.EFCore.PostgreSql`, or with the provider `MySql` the migrations of `MsgHatch.Data.EFCore.MySql` and of `MsgHatch.Subscriptions.Data.EFCore.MySql`. EF Core locks the database during the migration, and a second run changes nothing.
- With `--script <path>`, it writes the idempotent SQL script of the same migrations with `ScriptMsgHatchMigrationsAsync`, for a database administrator that runs only SQL. The script mode opens no connection. The script of MySQL needs the `mysql` client or another client that knows `DELIMITER`.
- It reads the connection string from the environment variable `SchemaMigrator__ConnectionString`, and the provider from `SchemaMigrator__Provider`, `SqlServer` (the default), `PostgreSql`, or `MySql`, through the options class `SchemaMigratorOptions`. It never writes the connection string to its output.
- It registers MsgHatch with no role and no message type, so it needs no transport and no router.

The exit code is 0 for a successful run, and 2 for a run with no connection string, an unknown provider, or unknown arguments.

## Run

```powershell
$env:SchemaMigrator__ConnectionString = "<connection string with the rights to change the schema>"
dotnet run --project examples/SchemaMigrator
dotnet run --project examples/SchemaMigrator -- --script msghatch-migrations.sql
```

On PostgreSQL:

```powershell
$env:SchemaMigrator__Provider = "PostgreSql"
dotnet run --project examples/SchemaMigrator
```

On MySQL:

```powershell
$env:SchemaMigrator__Provider = "MySql"
dotnet run --project examples/SchemaMigrator
```

## A step of a pipeline

A GitHub Actions step that applies the migrations before the deployment of the service, with the connection string in a secret of the pipeline:

```yaml
- name: Apply the MsgHatch schema migrations
  run: dotnet run --project examples/SchemaMigrator -c Release
  env:
    SchemaMigrator__ConnectionString: ${{ secrets.MSGHATCH_MIGRATION_CONNECTION_STRING }}
```

A step that writes the script as an artifact, for a database administrator:

```yaml
- name: Write the MsgHatch migration script
  run: dotnet run --project examples/SchemaMigrator -c Release -- --script msghatch-migrations.sql
  env:
    SchemaMigrator__ConnectionString: ${{ secrets.MSGHATCH_MIGRATION_CONNECTION_STRING }}
```

## The setup of the service

The service uses a connection string with only the rights to read and write rows. It calls no migration. With `RequireCurrentSchema`, its start fails when a MsgHatch migration is not applied, with a message that names the context and the migration. The check reads only the history tables `__StoreMigrations` and `__SubscriptionMigrations`, or `__MsgHatchStoreMigrations` and `__MsgHatchSubscriptionMigrations` on MySQL:

```csharp
services.AddMsgHatch()
    .UseEntityFrameworkSqlServerStores(connectionString, o => o.RequireCurrentSchema = true)
    .UseSubscriptions()
    .UseEntityFrameworkSqlServerSubscriptionStore(connectionString);
```

On PostgreSQL, the service calls `UseEntityFrameworkPostgreSqlStores` and `UseEntityFrameworkPostgreSqlSubscriptionStore` in the same way, and on MySQL `UseEntityFrameworkMySqlStores` and `UseEntityFrameworkMySqlSubscriptionStore`.

## Tests

`tests/SchemaMigrator.Tests.Integration` runs the migrator on the SQL Server, the PostgreSQL, and the MySQL of the EF Core integration tests, with the settings of `tests/MsgHatch.Data.EFCore.Tests.Integration`:

```powershell
dotnet test tests/SchemaMigrator.Tests.Integration
```
