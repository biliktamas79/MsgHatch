# MsgHatch.Subscriptions.Data.EFCore.MySql

The MySQL package of the EF Core subscription store of [MsgHatch.Subscriptions](https://github.com/biliktamas79/MsgHatch). It adds to `MsgHatch.Subscriptions.Data.EFCore`:

- The collation `utf8mb4_bin` of the ids and the names, from the dialect of `MsgHatch.Data.EFCore.MySql`, so an id is case-sensitive.
- The table `MsgHatchSubscription` in the database of the connection, because a schema of MySQL is a database.
- The schema migrations of `MsgHatchSubscriptionDbContext` for MySQL, with the unique index of the hash `OwnerKey`. MySQL refuses an index key of more than 3,072 bytes, and the 7 values of the key of a subscription have more.
- A setup with one call.

The package uses the provider `Microting.EntityFrameworkCore.MySql` through `MsgHatch.Data.EFCore.MySql`, for these reasons:

- It has the MIT license.
- It supports EF Core 10, and its tests run on MySQL and on MariaDB.
- The Oracle provider `MySql.EntityFrameworkCore` has the license `GPL-2.0-only WITH Universal-FOSS-exception-1.0`, which limits a closed-source host that ships its binaries. It also supports MySQL only.
- `Pomelo.EntityFrameworkCore.MySql` has no build for EF Core 10.

The public API of the package has no type of the provider, so a later move to another provider does not break a host.

## Install

```shell
dotnet add package MsgHatch.Subscriptions.Data.EFCore.MySql
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkMySqlStores(connectionString)
    .UseSubscriptions()
    .UseEntityFrameworkMySqlSubscriptionStore(connectionString);
```

`UseEntityFrameworkMySqlSubscriptionStore` calls `UseEntityFrameworkSubscriptionStore` of `MsgHatch.Subscriptions.Data.EFCore` with `UseMySql`, the dialect of `MsgHatch.Data.EFCore.MySql`, and the migrations of this package. The store shares `EntityFrameworkStoreOptions` with the EF Core stores, from the section `EntityFramework` under the root of `AddMsgHatch`, by default `MsgHatch:EntityFramework`. The option `Schema` has no effect on MySQL, so the startup validation fails when it is not the default `msghatch`.

## Schema migrations

`MigrateMsgHatchAsync` of `MsgHatch.Data.EFCore` applies the migrations of each registered MsgHatch context. The subscription context has the history table `__MsgHatchSubscriptionMigrations` in the database of the connection, next to `__MsgHatchStoreMigrations` of the stores:

[//]: # (snippet: migrate)
```csharp
await serviceProvider.MigrateMsgHatchAsync(ct: cancellationToken);
```

`ScriptMsgHatchMigrationsAsync` gives one idempotent script of the migrations of both contexts, which needs the `mysql` client or another client that knows `DELIMITER`. The option `RequireCurrentSchema` checks the history tables of both contexts at the start of the host.

## Version history

### 0.7.1 (2026-11-26)

- The package has the MsgHatch icon. The code and the API do not change.

### 0.7.0 (2026-11-25)

- The first version: `UseEntityFrameworkMySqlSubscriptionStore`, the collation of the dialect of `MsgHatch.Data.EFCore.MySql`, and the migrations of `MsgHatchSubscriptionDbContext` for MySQL.
