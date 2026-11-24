# MsgHatch.Data.EFCore.SqlServer

The SQL Server package of the EF Core stores of [MsgHatch](https://github.com/biliktamas79/MsgHatch). It adds to `MsgHatch.Data.EFCore` what SQL Server does better:

- The claim with `READPAST`: one `UPDATE` with an `OUTPUT` clause of the rows of a `TOP` subquery with `ROWLOCK, READPAST, UPDLOCK`, in the order of the due time and the add. A parallel claim skips the rows that another claim locked.
- The collation `Latin1_General_100_BIN2` of the ids and the names, so an id is case-sensitive and sorts in ordinal order.
- The schema migrations of `MsgHatchDbContext` for SQL Server.
- A setup with one call.

## Install

```shell
dotnet add package MsgHatch.Data.EFCore.SqlServer
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkSqlServerStores(connectionString);
```

`UseEntityFrameworkSqlServerStores` calls `UseEntityFrameworkStores` of `MsgHatch.Data.EFCore` with `UseSqlServer`, the dialect `SqlServerStoreDialect`, and the migrations of this package. The options are `EntityFrameworkStoreOptions` of `MsgHatch.Data.EFCore`, from the section `MsgHatch:EntityFramework`. An optional action changes them in code, and another one changes the SQL Server options, for example a retry on a transient failure:

[//]: # (snippet: setup-options)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkSqlServerStores(
        connectionString,
        o => o.Schema = "messaging",
        sql => sql.EnableRetryOnFailure());
```

## Schema migrations

The package creates the schema only in the migration step. `MigrateMsgHatchAsync` applies the migrations of the package in the schema of the options, with the history table `__StoreMigrations` in that schema:

[//]: # (snippet: migrate)
```csharp
await serviceProvider.MigrateMsgHatchAsync(ct: cancellationToken);
```

In a setup where only the pipeline has the rights to change the schema, a console app of the pipeline calls the method with a connection string of its own, and the service gets only the rights to read and write rows:

[//]: # (snippet: migrate-own-setup)
```csharp
await serviceProvider.MigrateMsgHatchAsync(o => o.UseSqlServer(migrationConnectionString), cancellationToken);
```

A pipeline or a database administrator that runs only SQL gets an idempotent script:

[//]: # (snippet: script)
```csharp
var script = await serviceProvider.ScriptMsgHatchMigrationsAsync(cancellationToken);
```

With `RequireCurrentSchema`, the start of the service fails when a migration is not applied. The check reads only the history table:

[//]: # (snippet: options)
```json
{
  "MsgHatch": {
    "EntityFramework": {
      "RequireCurrentSchema": true
    }
  }
}
```

## Version history

### 0.5.0 (2026-11-24)

- Breaking: the tables, the indexes, and the history table drop the prefix `MsgHatch`, because the schema of the options keeps them apart from the tables of the host: `Inbox`, `Outbox`, `ChannelState`, `ChannelEntry`, `MessageTypeEntry`, `IX_Outbox_Due`, and `__StoreMigrations`. The SQLite and MongoDB stores keep the prefix. The `Initial` migration of the package creates the new names.

### 0.3.0 (2026-11-24)

- The first version: `UseEntityFrameworkSqlServerStores`, the dialect `SqlServerStoreDialect` with the claim with `READPAST` and the collation `Latin1_General_100_BIN2`, and the migrations of `MsgHatchDbContext` for SQL Server.
