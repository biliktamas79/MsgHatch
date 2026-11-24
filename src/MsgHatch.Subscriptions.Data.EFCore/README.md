# MsgHatch.Subscriptions.Data.EFCore

The EF Core subscription store of [MsgHatch.Subscriptions](https://github.com/biliktamas79/MsgHatch), on each relational EF Core provider. The package keeps the subscriptions on the tables of `MsgHatchSubscriptionDbContext`, through a pooled `IDbContextFactory`, with one store instance for each tenant key.

The package creates no schema. The host creates the tables with its own EF Core migrations of `MsgHatchSubscriptionDbContext`.

## Install

```shell
dotnet add package MsgHatch.Subscriptions.Data.EFCore
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
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

### 0.3.0 (2026-11-24)

- Breaking: the package no longer gives the text columns the collation `Latin1_General_100_BIN2` on SQL Server. The collation comes from the option `TextCollation` of `EntityFrameworkStoreOptions`, or else from the dialect of a provider package, or else from the database.
- The store runs on each relational EF Core provider, for example SQLite. The startup validation fails only for a provider that is not relational.

### 0.1.0 (2026-11-22)

- The first version: the EF Core subscription store with `UseEntityFrameworkSubscriptionStore`, with SQL Server as the provider.
