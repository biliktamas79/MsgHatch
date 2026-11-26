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

`UseEntityFrameworkSqlServerStores` calls `UseEntityFrameworkStores` of `MsgHatch.Data.EFCore` with `UseSqlServer`, the dialect `SqlServerStoreDialect`, and the migrations of this package. The options are `EntityFrameworkStoreOptions` of `MsgHatch.Data.EFCore`, from the section `EntityFramework` under the root of `AddMsgHatch`, by default `MsgHatch:EntityFramework`. An optional action changes them in code, and another one changes the SQL Server options, for example a retry on a transient failure:

[//]: # (snippet: setup-options)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkSqlServerStores(
        connectionString,
        o => o.Schema = "messaging",
        sql => sql.EnableRetryOnFailure());
```

## Inbox in the transaction of the host

An app with its own `DbContext` can add an event to the inbox in the transaction of that context (D331). The app maps the inbox with `modelBuilder.MapMsgHatchInbox(this)` in `OnModelCreating` of its context, registers the context as a scoped service on the same database, and selects the stores with the type of the context. Here `TestHostContext` is the context of the app:

[//]: # (snippet: host)
```csharp
services.AddDbContext<TestHostContext>(o => o.UseSqlServer(connectionString));
services.AddMsgHatch()
    .UseEntityFrameworkSqlServerStores<TestHostContext>(connectionString);
```

The context of the app gets the dialect of SQL Server and the MsgHatch options of the store contexts. `AcceptAsync` adds the inbox row to the context, and does not save, so the `SaveChangesAsync` of the app writes it with the rows of the app. The migrations of the app never contain the inbox table, so `MigrateMsgHatchAsync` still creates it. The README of `MsgHatch.Data.EFCore` describes the mode and its startup checks.

## JSON columns

The JSON columns stay `nvarchar(max)`, because the type `json` needs SQL Server 2025 or Azure SQL. A host on such a server selects it with the option `JsonColumnType`, for example `o => o.JsonColumnType = "json"`. Then SQL Server checks each write of a document. The migrations of the package take the type of the option at run time.

## Errors

The dialect gives `Errors.Rejected` for a missing permission or a failed login (the numbers 229, 230, 262, and 18456), and `Errors.ValidationFailed` for a missing table or column and for a value that is too long (207, 208, 2628, and 8152). So the dispatcher does not retry these errors. Each other database failure stays `Errors.Unavailable`. A classifier of the host comes before the dialect, for example a classifier that rejects a write to a read-only database:

[//]: # (snippet: classifier)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkSqlServerStores(connectionString)
    .AddExceptionClassifier<MsgHatchDbContext, SqlServerReadOnlyClassifier>();
```

[//]: # (snippet: classifier-type)
```csharp
public sealed class SqlServerReadOnlyClassifier : IExceptionClassifier<MsgHatchDbContext>
{
    private const int _readOnlyDatabase = 3906;

    public ErrorType? Classify(Exception exception) =>
        exception.GetBaseException() is SqlException { Number: _readOnlyDatabase } ? Errors.Rejected : null;
}
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

### 0.9.0 (2026-11-26)

- `UseEntityFrameworkSqlServerStores<TContext>` selects the stores with the inbox in the transaction of the context `TContext` of the host (D331, D332). The context gets the dialect of SQL Server and the MsgHatch options of the store contexts.

### 0.7.1 (2026-11-26)

- The package has the MsgHatch icon. The code and the API do not change.

### 0.5.0 (2026-11-24)

- The option `JsonColumnType = "json"` gives the JSON columns the type `json` of SQL Server 2025 and Azure SQL. With no option, they stay `nvarchar(max)`. Breaking: a JSON column has no collation.
- Breaking: the dialect gives `Errors.Rejected` for a missing permission or a failed login, and `Errors.ValidationFailed` for a missing table or column and for a value that is too long, not `Errors.Unavailable`. So the dispatcher does not retry these errors.
- Breaking: the tables, the indexes, and the history table drop the prefix `MsgHatch`, because the schema of the options keeps them apart from the tables of the host: `Inbox`, `Outbox`, `ChannelState`, `ChannelEntry`, `MessageTypeEntry`, `IX_Outbox_Due`, and `__StoreMigrations`. The SQLite and MongoDB stores keep the prefix. The `Initial` migration of the package creates the new names.

### 0.3.0 (2026-11-24)

- The first version: `UseEntityFrameworkSqlServerStores`, the dialect `SqlServerStoreDialect` with the claim with `READPAST` and the collation `Latin1_General_100_BIN2`, and the migrations of `MsgHatchDbContext` for SQL Server.
