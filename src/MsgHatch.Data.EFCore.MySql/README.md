# MsgHatch.Data.EFCore.MySql

The MySQL package of the EF Core stores of [MsgHatch](https://github.com/biliktamas79/MsgHatch). It adds to `MsgHatch.Data.EFCore`:

- The collation `utf8mb4_bin` of the ids and the names, so an id is case-sensitive and sorts in binary order. MariaDB also has this collation.
- The tables with the prefix `MsgHatch` in the database of the connection, because a schema of MySQL is a database: `MsgHatchInbox`, `MsgHatchOutbox`, `MsgHatchChannelState`, `MsgHatchChannelEntry`, and `MsgHatchMessageTypeEntry`, with the history table `__MsgHatchStoreMigrations`.
- The schema migrations of `MsgHatchDbContext` for MySQL.
- A setup with one call.

MySQL has no `UPDATE ... RETURNING`, so the claim of the base package runs: a LINQ read of the due candidates and one `ExecuteUpdate` with a claim token. Each time is a `datetime(6)`, which keeps microseconds. A time comes back cut down to the microsecond. The tests of the package run on MySQL 8.4.

## Provider

The package uses `Microting.EntityFrameworkCore.MySql`, a fork of Pomelo on `MySqlConnector`, for these reasons:

- It has the MIT license.
- It supports EF Core 10, and its tests run on MySQL and on MariaDB.
- The Oracle provider `MySql.EntityFrameworkCore` has the license `GPL-2.0-only WITH Universal-FOSS-exception-1.0`, which limits a closed-source host that ships its binaries. It also supports MySQL only.
- `Pomelo.EntityFrameworkCore.MySql` has no build for EF Core 10.

The public API of the package has no type of the provider, so a later move to another provider does not break a host. The setup sets the server version MySQL 8.4, because the detection of the version opens a connection, and the setup and the startup validation open none.

## Install

```shell
dotnet add package MsgHatch.Data.EFCore.MySql
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkMySqlStores(connectionString);
```

`UseEntityFrameworkMySqlStores` calls `UseEntityFrameworkStores` of `MsgHatch.Data.EFCore` with `UseMySql`, the dialect `MySqlStoreDialect`, and the migrations of this package. The options are `EntityFrameworkStoreOptions` of `MsgHatch.Data.EFCore`, from the section `EntityFramework` under the root of `AddMsgHatch`, by default `MsgHatch:EntityFramework`. The option `Schema` has no effect on MySQL, so the startup validation fails when it is not the default `msghatch`. An optional action changes the options in code:

[//]: # (snippet: setup-options)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkMySqlStores(
        connectionString,
        o => o.JsonColumnType = "json");
```

## Character set

Each table has the character set `utf8mb4`, and a column with no character set of its own takes the one of its table. The migrations do not change the default character set of the database of the host. An id with a fixed length, for example the id of a message, is `varchar(36)`, not `char(36)`, because MySqlConnector reads a `CHAR(36)` column as a `Guid`.

## JSON columns

Each JSON column is `longtext`, the text type of the provider. The MySQL type `json` checks each write, but it sorts the keys of an object and drops duplicate keys, so an event type of a host that needs `$type` as the first key can fail to read. A host selects `json` with the option `JsonColumnType`, as the example above shows.

## Errors

The dialect gives `Errors.Rejected` for a missing permission or a failed login (the error numbers `1044`, `1045`, `1142`, and `1143`), and `Errors.ValidationFailed` for a missing table or column and for a value that is too long (`1146`, `1054`, and `1406`). So the dispatcher does not retry these errors. Each other database failure stays `Errors.Unavailable`. A classifier of the host comes before the dialect, for example a classifier that rejects a write to a server with the option `read_only`:

[//]: # (snippet: classifier)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkMySqlStores(connectionString)
    .AddExceptionClassifier<MsgHatchDbContext, MySqlReadOnlyClassifier>();
```

[//]: # (snippet: classifier-type)
```csharp
public sealed class MySqlReadOnlyClassifier : IExceptionClassifier<MsgHatchDbContext>
{
    public ErrorType? Classify(Exception exception) =>
        exception.GetBaseException() is MySqlException { ErrorCode: MySqlErrorCode.OptionPreventsStatement } ? Errors.Rejected : null;
}
```

## Schema migrations

The package creates the tables only in the migration step. `MigrateMsgHatchAsync` applies the migrations of the package in the database of the connection, with the history table `__MsgHatchStoreMigrations`:

[//]: # (snippet: migrate)
```csharp
await serviceProvider.MigrateMsgHatchAsync(ct: cancellationToken);
```

The name of the database has at most 45 characters. The provider locks the migrations with `GET_LOCK('__{database}_EFMigrationsLock')`, and MySQL refuses a lock name with more than 64 characters.

In a setup where only the pipeline has the rights to change the schema, a console app of the pipeline calls the method with a connection string of its own, and the service gets only the rights to read and write rows. The provider setup of the migration also needs the server version:

[//]: # (snippet: migrate-own-setup)
```csharp
await serviceProvider.MigrateMsgHatchAsync(
    o => o.UseMySql(migrationConnectionString, ServerVersion.Parse("8.4.0-mysql")), cancellationToken);
```

A pipeline or a database administrator that runs only SQL gets an idempotent script. The script runs each migration in a procedure between `DELIMITER` lines, so it needs the `mysql` client or another client that knows `DELIMITER`, and the right to create a procedure:

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

### 0.7.1 (2026-11-26)

- The package has the MsgHatch icon. The code and the API do not change.

### 0.7.0 (2026-11-25)

- The first version: `UseEntityFrameworkMySqlStores`, the dialect `MySqlStoreDialect` with the collation `utf8mb4_bin` and the tables with the prefix `MsgHatch`, and the migrations of `MsgHatchDbContext` for MySQL.
