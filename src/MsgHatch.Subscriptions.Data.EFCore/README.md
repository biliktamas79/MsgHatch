# MsgHatch.Subscriptions.Data.EFCore

The EF Core subscription store of [MsgHatch.Subscriptions](https://github.com/biliktamas79/MsgHatch), with SQL Server as the provider. The package keeps the subscriptions on the tables of `MsgHatchSubscriptionDbContext`, through a pooled `IDbContextFactory`, with one store instance for each tenant key.

The package creates no schema. The host creates the tables with its own EF Core migrations of `MsgHatchSubscriptionDbContext`.

## Install

```shell
dotnet add package MsgHatch.Subscriptions.Data.EFCore
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkStores(o => o.UseSqlServer(connectionString))
    .UseSubscriptions()
    .UseEntityFrameworkSubscriptionStore(o => o.UseSqlServer(connectionString));
```

The argument sets the provider and the connection of the contexts. `UseEntityFrameworkSubscriptionStore` shares `EntityFrameworkStoreOptions` with `UseEntityFrameworkStores` of `MsgHatch.Data.EFCore`: the options bind from the section `MsgHatch:EntityFramework`.

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

### 0.1.0 (2026-11-22)

- The first version: the EF Core subscription store with `UseEntityFrameworkSubscriptionStore`, with SQL Server as the provider.
