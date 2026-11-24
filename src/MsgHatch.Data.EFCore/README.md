# MsgHatch.Data.EFCore

The EF Core stores of [MsgHatch](https://github.com/biliktamas79/MsgHatch), with SQL Server as the provider. The package implements `IInboxStore`, `IOutboxStore`, and `IRegistrySettingsStore` of `MsgHatch.Domain` on the tables of `MsgHatchDbContext`, through a pooled `IDbContextFactory`.

The package creates no schema. The host creates the tables with its own EF Core migrations of `MsgHatchDbContext`.

## Install

```shell
dotnet add package MsgHatch.Data.EFCore
dotnet add package Microsoft.EntityFrameworkCore.SqlServer
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkStores(o => o.UseSqlServer(connectionString));
```

The first argument sets the provider and the connection of the contexts. `UseEntityFrameworkStores` binds `EntityFrameworkStoreOptions` from the section `MsgHatch:EntityFramework`, and validates them at the start of the host. A value from code wins over the configuration:

[//]: # (snippet: setup-code)
```csharp
services.AddMsgHatch()
    .UseEntityFrameworkStores(
        o => o.UseSqlServer(connectionString),
        o => o.Schema = "messaging");
```

## Options

[//]: # (options: EntityFrameworkStoreOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `Schema` | `string` | `msghatch` | Required. The database schema of the tables. It starts with an ASCII letter or `_`, and contains only ASCII letters, digits, and `_`. |

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

### 0.2.1 (2026-11-24)

- The package depends on the latest stable version of `Microsoft.EntityFrameworkCore.Relational` within the major 10 at the time of the pack, not on 10.0.14. The API does not change.

### 0.1.0 (2026-11-22)

- The first version: the EF Core inbox, outbox, and registry settings stores with `UseEntityFrameworkStores`, with SQL Server as the provider.
