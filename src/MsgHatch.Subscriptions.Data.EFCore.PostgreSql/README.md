# MsgHatch.Subscriptions.Data.EFCore.PostgreSql

The PostgreSQL package of the EF Core subscription store of [MsgHatch.Subscriptions](https://github.com/biliktamas79/MsgHatch). It adds to `MsgHatch.Subscriptions.Data.EFCore`:

- The collation `C` of the ids and the names, from the dialect of `MsgHatch.Data.EFCore.PostgreSql`, so an id is case-sensitive.
- The schema migrations of `MsgHatchSubscriptionDbContext` for PostgreSQL.
- A setup with one call.

## Install

```shell
dotnet add package MsgHatch.Subscriptions.Data.EFCore.PostgreSql
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkPostgreSqlStores(connectionString)
    .UseSubscriptions()
    .UseEntityFrameworkPostgreSqlSubscriptionStore(connectionString);
```

`UseEntityFrameworkPostgreSqlSubscriptionStore` calls `UseEntityFrameworkSubscriptionStore` of `MsgHatch.Subscriptions.Data.EFCore` with `UseNpgsql`, the dialect of `MsgHatch.Data.EFCore.PostgreSql`, and the migrations of this package. The store shares `EntityFrameworkStoreOptions` with the EF Core stores, from the section `MsgHatch:EntityFramework`. An optional action changes the Npgsql options, for example a retry on a transient failure.

## Schema migrations

`MigrateMsgHatchAsync` of `MsgHatch.Data.EFCore` applies the migrations of each registered MsgHatch context. The subscription context has the history table `__MsgHatchSubscriptionMigrations` in the schema of the options, next to `__MsgHatchMigrations` of the stores:

[//]: # (snippet: migrate)
```csharp
await serviceProvider.MigrateMsgHatchAsync(ct: cancellationToken);
```

`ScriptMsgHatchMigrationsAsync` gives one idempotent script of the migrations of both contexts, and the option `RequireCurrentSchema` checks the history tables of both contexts at the start of the host.

## Version history

### 0.4.0 (2026-11-24)

- The first version: `UseEntityFrameworkPostgreSqlSubscriptionStore`, the collation of the dialect of `MsgHatch.Data.EFCore.PostgreSql`, and the migrations of `MsgHatchSubscriptionDbContext` for PostgreSQL.
