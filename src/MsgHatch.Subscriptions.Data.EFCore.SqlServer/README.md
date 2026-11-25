# MsgHatch.Subscriptions.Data.EFCore.SqlServer

The SQL Server package of the EF Core subscription store of [MsgHatch.Subscriptions](https://github.com/biliktamas79/MsgHatch). It adds to `MsgHatch.Subscriptions.Data.EFCore`:

- The collation `Latin1_General_100_BIN2` of the ids and the names, from the dialect of `MsgHatch.Data.EFCore.SqlServer`, so an id is case-sensitive.
- The schema migrations of `MsgHatchSubscriptionDbContext` for SQL Server.
- A setup with one call.

## Install

```shell
dotnet add package MsgHatch.Subscriptions.Data.EFCore.SqlServer
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkSqlServerStores(connectionString)
    .UseSubscriptions()
    .UseEntityFrameworkSqlServerSubscriptionStore(connectionString);
```

`UseEntityFrameworkSqlServerSubscriptionStore` calls `UseEntityFrameworkSubscriptionStore` of `MsgHatch.Subscriptions.Data.EFCore` with `UseSqlServer`, the dialect of `MsgHatch.Data.EFCore.SqlServer`, and the migrations of this package. The store shares `EntityFrameworkStoreOptions` with the EF Core stores, from the section `MsgHatch:EntityFramework`. An optional action changes the SQL Server options, for example a retry on a transient failure.

## Schema migrations

`MigrateMsgHatchAsync` of `MsgHatch.Data.EFCore` applies the migrations of each registered MsgHatch context. The subscription context has the history table `__SubscriptionMigrations` in the schema of the options, next to `__StoreMigrations` of the stores:

[//]: # (snippet: migrate)
```csharp
await serviceProvider.MigrateMsgHatchAsync(ct: cancellationToken);
```

`ScriptMsgHatchMigrationsAsync` gives one idempotent script of the migrations of both contexts, and the option `RequireCurrentSchema` checks the history tables of both contexts at the start of the host.

## Version history

### 0.7.0 (2026-11-25)

- Breaking: the new column `OwnerKey` holds the SHA-256 of the 7 key values of a subscription, and the unique index `UX_Subscription_Owner` has only this column. The new index `IX_Subscription_Type` on the tenant and the message type serves the delivery read. The new `Initial` migration of the package creates them, so a database of an earlier version needs a new database or a manual change.

### 0.5.0 (2026-11-24)

- The option `JsonColumnType = "json"` gives the columns `Settings` and `NamedUsers` the type `json` of SQL Server 2025 and Azure SQL. With no option, they stay `nvarchar(max)`. Breaking: a JSON column has no collation.
- Breaking: the table, the index, and the history table drop the prefix `MsgHatch`: `Subscription`, `UX_Subscription_Owner`, and `__SubscriptionMigrations`. The SQLite and MongoDB stores keep the prefix. The `Initial` migration of the package creates the new names.

### 0.3.0 (2026-11-24)

- The first version: `UseEntityFrameworkSqlServerSubscriptionStore`, the collation of the dialect of `MsgHatch.Data.EFCore.SqlServer`, and the migrations of `MsgHatchSubscriptionDbContext` for SQL Server.
