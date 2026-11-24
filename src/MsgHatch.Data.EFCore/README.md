# MsgHatch.Data.EFCore

The EF Core stores of [MsgHatch](https://github.com/biliktamas79/MsgHatch), on each relational EF Core provider. The package implements `IInboxStore`, `IOutboxStore`, and `IRegistrySettingsStore` of `MsgHatch.Domain` on the tables of `MsgHatchDbContext`, through a pooled `IDbContextFactory`. It has no SQL text and no provider name: the claim, the adds, and the writes of the publish tries are LINQ queries and EF Core writes.

The package creates no schema. The host creates the tables with its own EF Core migrations of `MsgHatchDbContext`.

## Install

```shell
dotnet add package MsgHatch.Data.EFCore
dotnet add package Microsoft.EntityFrameworkCore.Sqlite
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

## Claim

A claim takes the due messages in the order of their due time, and then in the order of their add. The store reads the due candidates with a LINQ query, and claims those that are still pending and due with one `ExecuteUpdate` and a new claim token. When another dispatcher took a candidate first, the store reads the next candidates, until it has the maximum count or no due message is left.

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

### 0.3.0 (2026-11-24)

- Breaking: the schema changes. Each message table gets the columns `ClaimToken` and `ClaimedDueAtUtc`. The due indexes have no filter: `IX_MsgHatchInbox_Due` is on `(Status, NextTryAtUtc, Sequence)`, and `IX_MsgHatchOutbox_Due` is on `(Channel, Status, NextTryAtUtc, Sequence)`. A host with its own migrations adds a migration of `MsgHatchDbContext`.
- The stores run on each relational EF Core provider, for example SQLite. The claim, the adds, and the writes of the publish tries have no SQL text. The startup validation fails only for a provider that is not relational.

### 0.2.1 (2026-11-24)

- The package depends on the latest stable version of `Microsoft.EntityFrameworkCore.Relational` within the major 10 at the time of the pack, not on 10.0.14. The API does not change.

### 0.1.0 (2026-11-22)

- The first version: the EF Core inbox, outbox, and registry settings stores with `UseEntityFrameworkStores`, with SQL Server as the provider.
