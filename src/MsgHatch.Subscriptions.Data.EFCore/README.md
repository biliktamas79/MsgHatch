# MsgHatch.Subscriptions.Data.EFCore

The EF Core subscription store of [MsgHatch.Subscriptions](https://github.com/biliktamas79/MsgHatch), on each relational EF Core provider. The package keeps the subscriptions on the tables of `MsgHatchSubscriptionDbContext`, through a pooled `IDbContextFactory`, with one store instance for each tenant key.

For SQL Server, use the package `MsgHatch.Subscriptions.Data.EFCore.SqlServer`. It adds the binary collation, the schema migrations, and a setup with one call. For PostgreSQL, use the package `MsgHatch.Subscriptions.Data.EFCore.PostgreSql`, with the collation `C`. For another provider, add the EF Core provider and the migrations of the host.

## Install

```shell
dotnet add package MsgHatch.Subscriptions.Data.EFCore
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
```

For SQL Server:

```shell
dotnet add package MsgHatch.Subscriptions.Data.EFCore.SqlServer
```

For PostgreSQL:

```shell
dotnet add package MsgHatch.Subscriptions.Data.EFCore.PostgreSql
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkStores(o => o.UseSqlite(connectionString))
    .UseSubscriptions()
    .UseEntityFrameworkSubscriptionStore(o => o.UseSqlite(connectionString));
```

The argument sets the provider and the connection of the contexts. The startup validation fails for a provider that is not relational. Use a case-sensitive collation of the text columns, so that two tenants that differ only in case stay apart: the option `TextCollation` sets it, or else the dialect of a provider package, or else the database. `UseEntityFrameworkSubscriptionStore` shares `EntityFrameworkStoreOptions` with `UseEntityFrameworkStores` of `MsgHatch.Data.EFCore`: the options bind from the section `MsgHatch:EntityFramework`.

With the SQL Server packages, one call for each context selects the store, the dialect of SQL Server, and the migrations of the package:

[//]: # (snippet: setup-sqlserver)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkSqlServerStores(connectionString)
    .UseSubscriptions()
    .UseEntityFrameworkSqlServerSubscriptionStore(connectionString);
```

The PostgreSQL packages do the same with the dialect of PostgreSQL:

[//]: # (snippet: setup-postgresql)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkPostgreSqlStores(connectionString)
    .UseSubscriptions()
    .UseEntityFrameworkPostgreSqlSubscriptionStore(connectionString);
```

## JSON columns

The columns `Settings` and `NamedUsers` hold JSON documents. They get the type of the option `JsonColumnType`, or else of the dialect, or else the text type of the provider, as the JSON columns of `MsgHatch.Data.EFCore`. A JSON column has no collation.

## Errors

The store classifies an exception in the order of the EF Core stores of `MsgHatch.Data.EFCore`: a cancelled token, the classifiers of the host, the dialect of the provider package, and then the built-in mapping. The client of a classifier is the subscription context, so `IExceptionClassifier<MsgHatchSubscriptionDbContext>` serves only the subscription store:

[//]: # (snippet: classifier)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkStores(o => o.UseSqlite(connectionString))
    .UseSubscriptions()
    .UseEntityFrameworkSubscriptionStore(o => o.UseSqlite(connectionString))
    .AddExceptionClassifier<MsgHatchSubscriptionDbContext, SqliteReadOnlySubscriptionsClassifier>();
```

[//]: # (snippet: classifier-type)
```csharp
public sealed class SqliteReadOnlySubscriptionsClassifier : IExceptionClassifier<MsgHatchSubscriptionDbContext>
{
    private const int _readOnly = 8;

    public ErrorType? Classify(Exception exception) =>
        exception.GetBaseException() is SqliteException { SqliteErrorCode: _readOnly } ? Errors.Rejected : null;
}
```

## Schema migrations

`MigrateMsgHatchAsync` of `MsgHatch.Data.EFCore` applies the migrations of each registered MsgHatch context, and only it changes the schema. The subscription context has the history table `__SubscriptionMigrations` in the schema of the options. With the dialect of a database with no schemas, the table is `MsgHatchSubscription` and the history table is `__MsgHatchSubscriptionMigrations`, with no schema:

[//]: # (snippet: migrate)
```csharp
await serviceProvider.MigrateMsgHatchAsync(ct: cancellationToken);
```

The README of `MsgHatch.Data.EFCore` describes the migration step: a migration with its own provider setup, the idempotent script, the option `RequireCurrentSchema`, and the migrations that a host generates for a provider with no MsgHatch package.

## Options

The options are `EntityFrameworkStoreOptions` of `MsgHatch.Data.EFCore`:

[//]: # (snippet: options)
```json
{
  "MsgHatch": {
    "EntityFramework": {
      "Schema": "msghatch"
    }
  }
}
```

## Version history

### 0.7.0 (2026-11-25)

- With the dialect of a database with no schemas, the table is `MsgHatchSubscription` and the history table is `__MsgHatchSubscriptionMigrations`, with no schema.

### 0.5.0 (2026-11-24)

- Breaking: the columns `Settings` and `NamedUsers` get the JSON type of the option `JsonColumnType` or of the dialect, or else the text type of the provider, and no collation.
- The store asks the classifiers `IExceptionClassifier<MsgHatchSubscriptionDbContext>` of the host, then the dialect, and then the built-in mapping. Its constructor gets the optional parameter `classifiers`, and the setup method gives it the registered classifiers.
- Breaking: the table, the index, and the history table drop the prefix `MsgHatch`: `Subscription`, `UX_Subscription_Owner`, and `__SubscriptionMigrations`. The SQLite and MongoDB stores keep the prefix. A host with migrations of its own makes them again.

### 0.4.0 (2026-11-24)

- The new package `MsgHatch.Subscriptions.Data.EFCore.PostgreSql` ships the migrations for PostgreSQL. This package does not change.

### 0.3.0 (2026-11-24)

- Breaking: the package no longer gives the text columns the collation `Latin1_General_100_BIN2` on SQL Server. The collation comes from the option `TextCollation` of `EntityFrameworkStoreOptions`, or else from the dialect of a provider package, or else from the database.
- The store runs on each relational EF Core provider, for example SQLite. The startup validation fails only for a provider that is not relational.
- The migration step of `MsgHatch.Data.EFCore` applies the migrations of `MsgHatchSubscriptionDbContext` with the history table `__MsgHatchSubscriptionMigrations`. The new package `MsgHatch.Subscriptions.Data.EFCore.SqlServer` ships the migrations for SQL Server.

### 0.1.0 (2026-11-22)

- The first version: the EF Core subscription store with `UseEntityFrameworkSubscriptionStore`, with SQL Server as the provider.
