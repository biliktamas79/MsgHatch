# MsgHatch.Data.EFCore.PostgreSql

The PostgreSQL package of the EF Core stores of [MsgHatch](https://github.com/biliktamas79/MsgHatch). It adds to `MsgHatch.Data.EFCore` what PostgreSQL does better:

- The claim with `SKIP LOCKED`: one `UPDATE ... FROM` a subquery that reads the due rows in the order of the due time and the add, stops with `LIMIT`, and locks them with `FOR UPDATE SKIP LOCKED`. A parallel claim skips the rows that another claim locked. `RETURNING` gives the claimed rows.
- The collation `C` of the ids and the names, so an id is case-sensitive and sorts in ordinal order.
- The schema migrations of `MsgHatchDbContext` for PostgreSQL.
- A setup with one call.

Each time is a `timestamptz`, which keeps microseconds. A time comes back cut down to the microsecond.

## Install

```shell
dotnet add package MsgHatch.Data.EFCore.PostgreSql
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkPostgreSqlStores(connectionString);
```

`UseEntityFrameworkPostgreSqlStores` calls `UseEntityFrameworkStores` of `MsgHatch.Data.EFCore` with `UseNpgsql`, the dialect `PostgreSqlStoreDialect`, and the migrations of this package. The options are `EntityFrameworkStoreOptions` of `MsgHatch.Data.EFCore`, from the section `MsgHatch:EntityFramework`. An optional action changes them in code, and another one changes the Npgsql options, for example a retry on a transient failure:

[//]: # (snippet: setup-options)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkPostgreSqlStores(
        connectionString,
        o => o.Schema = "messaging",
        npgsql => npgsql.EnableRetryOnFailure());
```

## Errors

The dialect gives `Errors.Rejected` for a missing permission or a failed login (the states `42501`, `28000`, and `28P01`), and `Errors.ValidationFailed` for a missing table or column and for a value that is too long (`42P01`, `42703`, and `22001`). So the dispatcher does not retry these errors. Each other database failure stays `Errors.Unavailable`. A classifier of the host comes before the dialect, for example a classifier that rejects a write to a read-only database:

[//]: # (snippet: classifier)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkPostgreSqlStores(connectionString)
    .AddExceptionClassifier<MsgHatchDbContext, PostgreSqlReadOnlyClassifier>();
```

[//]: # (snippet: classifier-type)
```csharp
public sealed class PostgreSqlReadOnlyClassifier : IExceptionClassifier<MsgHatchDbContext>
{
    public ErrorType? Classify(Exception exception) =>
        exception.GetBaseException() is PostgresException { SqlState: PostgresErrorCodes.ReadOnlySqlTransaction } ? Errors.Rejected : null;
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
await serviceProvider.MigrateMsgHatchAsync(o => o.UseNpgsql(migrationConnectionString), cancellationToken);
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

- Breaking: the dialect gives `Errors.Rejected` for a missing permission or a failed login, and `Errors.ValidationFailed` for a missing table or column and for a value that is too long, not `Errors.Unavailable`. So the dispatcher does not retry these errors.
- Breaking: the tables, the indexes, and the history table drop the prefix `MsgHatch`, because the schema of the options keeps them apart from the tables of the host: `Inbox`, `Outbox`, `ChannelState`, `ChannelEntry`, `MessageTypeEntry`, `IX_Outbox_Due`, and `__StoreMigrations`. The SQLite and MongoDB stores keep the prefix. The `Initial` migration of the package creates the new names.

### 0.4.0 (2026-11-24)

- The first version: `UseEntityFrameworkPostgreSqlStores`, the dialect `PostgreSqlStoreDialect` with the claim with `SKIP LOCKED` and the collation `C`, and the migrations of `MsgHatchDbContext` for PostgreSQL.
