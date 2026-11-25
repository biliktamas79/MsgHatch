# MsgHatch.Data.EFCore

The EF Core stores of [MsgHatch](https://github.com/biliktamas79/MsgHatch), on each relational EF Core provider. The package implements `IInboxStore`, `IOutboxStore`, and `IRegistrySettingsStore` of `MsgHatch.Domain` on the tables of `MsgHatchDbContext`, through a pooled `IDbContextFactory`. It has no SQL text and no provider name: the claim, the adds, and the writes of the publish tries are LINQ queries and EF Core writes.

For SQL Server, use the package `MsgHatch.Data.EFCore.SqlServer`. It adds the claim with `READPAST`, the binary collation, the schema migrations, and a setup with one call. For PostgreSQL, use the package `MsgHatch.Data.EFCore.PostgreSql`, with the claim with `SKIP LOCKED` and the collation `C`. For another provider, add the EF Core provider and the migrations of the host.

## Install

```shell
dotnet add package MsgHatch.Data.EFCore
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
```

For SQL Server:

```shell
dotnet add package MsgHatch.Data.EFCore.SqlServer
```

For PostgreSQL:

```shell
dotnet add package MsgHatch.Data.EFCore.PostgreSql
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkStores(o => o.UseSqlite(connectionString));
```

The first argument sets the provider and the connection of the contexts. The startup validation fails for a provider that is not relational. `UseEntityFrameworkStores` binds `EntityFrameworkStoreOptions` from the section `MsgHatch:EntityFramework`, and validates them at the start of the host. A value from code wins over the configuration:

[//]: # (snippet: setup-code)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkStores(
        o => o.UseSqlite(connectionString),
        o => o.Schema = "messaging");
```

With the SQL Server package, one call selects the stores, the dialect of SQL Server, and the migrations of the package:

[//]: # (snippet: setup-sqlserver)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkSqlServerStores(connectionString);
```

The PostgreSQL package does the same with the dialect of PostgreSQL. Each time is a `timestamptz` there, which keeps microseconds, so a time comes back cut down to the microsecond:

[//]: # (snippet: setup-postgresql)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkPostgreSqlStores(connectionString);
```

## Claim

A claim takes the due messages in the order of their due time, and then in the order of their add. The store reads the due candidates with a LINQ query, and claims those that are still pending and due with one `ExecuteUpdate` and a new claim token. When another dispatcher took a candidate first, the store reads the next candidates, until it has the maximum count or no due message is left.

## Collation

An id of a tenant, a company, or a user is case-sensitive, so `t1` and `T1` are two tenants. Use a case-sensitive collation of the text columns: the option `TextCollation` sets it, or else the dialect of a provider package, or else the database. The default collation of SQLite, `BINARY`, is case-sensitive.

## JSON columns

The columns `Content`, `Trace`, `Tries`, and `DisabledChannels` hold JSON documents. Their type comes from the option `JsonColumnType`, or else from the dialect of a provider package, or else it is the text type of the provider. The PostgreSQL package gives `json`, which checks each write. The SQL Server package keeps `nvarchar(max)`, because the type `json` needs SQL Server 2025 or Azure SQL. The stores keep the JSON work in C#, and a JSON column has no collation, because no query compares or sorts a document.

## Errors

A store gives each failure as an `Error` value. It classifies an exception in this order, and the first result that is not null wins:

1. A cancelled token gives `Errors.OperationCancelled`.
2. The exception classifiers of the host, in the order of their registration.
3. The dialect of the provider package. The SQL Server and PostgreSQL packages give `Errors.Rejected` for a missing permission or a failed login, and `Errors.ValidationFailed` for a missing table or column and for a value that is too long.
4. The built-in mapping: `Errors.Unavailable` for each other database failure, and `Errors.SerializationFailed` for a stored value that a person changed into an invalid value.

The client of a classifier is the context of the stores, so `IExceptionClassifier<MsgHatchDbContext>` serves the inbox, the outbox, and the registry settings. A type that is not transient stops the retries of the message. For example, a classifier that rejects a write to a read-only SQLite file:

[//]: # (snippet: classifier)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkStores(o => o.UseSqlite(connectionString))
    .AddExceptionClassifier<MsgHatchDbContext, SqliteReadOnlyClassifier>();
```

[//]: # (snippet: classifier-type)
```csharp
public sealed class SqliteReadOnlyClassifier : IExceptionClassifier<MsgHatchDbContext>
{
    private const int _readOnly = 8;

    public ErrorType? Classify(Exception exception) =>
        exception.GetBaseException() is SqliteException { SqliteErrorCode: _readOnly } ? Errors.Rejected : null;
}
```

## Schema migrations

Only `MigrateMsgHatchAsync` changes the schema. It applies the migrations of each registered MsgHatch context, `MsgHatchDbContext` and the subscription context. Each context has a history table of its own in the schema of the options, `__StoreMigrations` and `__SubscriptionMigrations`, so the migration history of the host does not change. On a database with no schemas, the history tables are `__MsgHatchStoreMigrations` and `__MsgHatchSubscriptionMigrations`. EF Core locks the database during the migration, so two hosts that start at the same time apply each migration one time.

[//]: # (snippet: migrate)
```csharp
await serviceProvider.MigrateMsgHatchAsync(ct: cancellationToken);
```

- A host calls the method at its start. Where only the pipeline has the rights to change the schema, a separate console app of the pipeline calls it, and the service gets only the rights to read and write rows. The example `examples/SchemaMigrator` is such a console app.
- An optional action gives the migration its own provider setup on top of the setup of the stores, for example `o => o.UseSqlServer(migrationConnectionString)`. With no action, the migration uses the setup of the stores. MsgHatch never logs a connection string.
- `ScriptMsgHatchMigrationsAsync` gives an idempotent SQL script of the migrations, for a pipeline or a database administrator that runs only SQL.
- With the option `RequireCurrentSchema`, the start of the host fails when a migration is not applied. The message names the context and the migration. The check reads only the history tables.
- A provider package ships the migrations of its context. For a provider with no MsgHatch package, the host generates the migrations of the MsgHatch contexts in its own assembly, for example `o => o.UseSqlite(connectionString, sqlite => sqlite.MigrationsAssembly("MyHost"))` and `dotnet ef migrations add InitialMsgHatch --context MsgHatchDbContext`. `MigrateMsgHatchAsync` applies them.

## Provider packages

A provider package implements `IEntityFrameworkStoreDialect` of the namespace `MsgHatch.Data.EFCore.Providers`: the name of the EF Core provider that it serves, the default collation, the error type of a database exception, and an optional claim statement. Its migrations derive from `EntityFrameworkStoreMigration` and `EntityFrameworkStoreModelSnapshot` of the namespace `MsgHatch.Data.EFCore.Migrations`, so each migration takes the schema and the collation of the options at run time. The package gives the dialect to the contexts with `UseEntityFrameworkStoreDialect`. The base package gives the claim statement the quoted names of the table, the columns, and the parameters in `ClaimStatementNames`, and runs it with `FromSqlRaw`. The startup validation fails when the dialect serves another provider than the provider of the context.

A database with no schemas inside it, for example MySQL, where a schema is a database, has a dialect with `HasSchemas` false. Then each table and each history table gets the prefix `MsgHatch` and no schema, for example `MsgHatchOutbox` and `__MsgHatchStoreMigrations`, in the database of the connection. The names of the indexes do not change. The option `Schema` then has no effect, so the startup validation fails when it is not the default `msghatch`.

## Options

[//]: # (options: EntityFrameworkStoreOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `Schema` | `string` | `msghatch` | Required. The database schema of the tables. It starts with an ASCII letter or `_`, and contains only ASCII letters, digits, and `_`. Only the default with the dialect of a database with no schemas. |
| `TextCollation` | `string?` | the collation of the dialect, or else of the database | The collation of each text column, so of each id and name, for example `Latin1_General_100_BIN2`. Only ASCII letters, digits, `_`, `.`, and `-`. |
| `JsonColumnType` | `string?` | the JSON type of the dialect, or else the text type of the provider | The type of each JSON column, for example `json`, `jsonb`, or `nvarchar(max)`. It starts with an ASCII letter, contains only ASCII letters, digits, and `_`, and can end with a size in parentheses. |
| `RequireCurrentSchema` | `bool` | `false` | The start of the host fails when a migration of a MsgHatch context is not applied. |

[//]: # (snippet: options)
```json
{
  "MsgHatch": {
    "EntityFramework": {
      "Schema": "msghatch",
      "RequireCurrentSchema": true
    }
  }
}
```

## Version history

### 0.7.0 (2026-11-25)

- `IEntityFrameworkStoreDialect` gets the default property `HasSchemas`, which gives true. With false, each table and each history table gets the prefix `MsgHatch` and no schema, for example `MsgHatchOutbox` and `__MsgHatchStoreMigrations`, and the startup validation fails when the option `Schema` is not the default `msghatch`.
- A retry of a failed message and the start of a channel check also work on MySQL. The retry sets the new try limit before the status, because MySQL applies the assignments of an `UPDATE` from left to right. The channel check reads the latest version first, because MySQL refuses a subquery on the table of an `UPDATE` (error 1093).

### 0.5.0 (2026-11-24)

- Breaking: the JSON columns get the type of the new option `JsonColumnType`, or else the new default property `JsonColumnType` of `IEntityFrameworkStoreDialect`, or else the text type of the provider. A JSON column has no collation. `EntityFrameworkStoreMigration` and `EntityFrameworkStoreModelSnapshot` get the property `JsonColumnType`, which a migration of a provider package uses in place of the type of the design time.
- Breaking: `IEntityFrameworkStoreDialect` gets the default method `ErrorTypeOf`. The stores ask the classifiers `IExceptionClassifier<MsgHatchDbContext>` of the host, then the dialect, and then the built-in mapping. The constructors of the stores get the optional parameter `classifiers`, and the setup method gives them the registered classifiers.
- Breaking: the tables, the indexes, and the history table drop the prefix `MsgHatch`, because the schema of the options keeps them apart from the tables of the host: `Inbox`, `Outbox`, `ChannelState`, `ChannelEntry`, `MessageTypeEntry`, `IX_Outbox_Due`, and `__StoreMigrations`. The SQLite and MongoDB stores keep the prefix. A host with migrations of its own makes them again.
- Breaking: the API of a provider package moves into its own namespaces. `IEntityFrameworkStoreDialect`, `EntityFrameworkStoreDialectExtensions`, and `ClaimStatementNames` are in `MsgHatch.Data.EFCore.Providers`, and `EntityFrameworkStoreMigration` and `EntityFrameworkStoreModelSnapshot` are in `MsgHatch.Data.EFCore.Migrations`. A host that uses only the stores, the options, and `MsgHatchDbContext` needs no change.

### 0.4.0 (2026-11-24)

- The new package `MsgHatch.Data.EFCore.PostgreSql` ships the dialect and the migrations for PostgreSQL. This package does not change.

### 0.3.0 (2026-11-24)

- Breaking: the schema changes. Each message table gets the columns `ClaimToken` and `ClaimedDueAtUtc`. The due indexes have no filter: `IX_MsgHatchInbox_Due` is on `(Status, NextTryAtUtc, Sequence)`, and `IX_MsgHatchOutbox_Due` is on `(Channel, Status, NextTryAtUtc, Sequence)`. A host with its own migrations adds a migration of `MsgHatchDbContext`.
- Breaking: the package no longer gives the text columns the collation `Latin1_General_100_BIN2` on SQL Server. The collation comes from the new option `TextCollation`, or else from the dialect of a provider package, or else from the database.
- The stores run on each relational EF Core provider, for example SQLite. The claim, the adds, and the writes of the publish tries have no SQL text. The startup validation fails only for a provider that is not relational.
- The contract `IEntityFrameworkStoreDialect` of a provider package, with `ClaimStatementNames` and `UseEntityFrameworkStoreDialect`. The startup validation fails for a dialect of another provider.
- Each time gets the kind UTC also on a write, because a provider can refuse a time with no kind.
- The migration step: `MigrateMsgHatchAsync`, `ScriptMsgHatchMigrationsAsync`, the option `RequireCurrentSchema`, and the history table `__MsgHatchMigrations`. The new package `MsgHatch.Data.EFCore.SqlServer` ships the migrations for SQL Server.

### 0.2.1 (2026-11-24)

- The package depends on the latest stable version of `Microsoft.EntityFrameworkCore.Relational` within the major 10 at the time of the pack, not on 10.0.14. The API does not change.

### 0.1.0 (2026-11-22)

- The first version: the EF Core inbox, outbox, and registry settings stores with `UseEntityFrameworkStores`, with SQL Server as the provider.
