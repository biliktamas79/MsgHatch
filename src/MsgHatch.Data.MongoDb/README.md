# MsgHatch.Data.MongoDb

The MongoDB stores of [MsgHatch](https://github.com/biliktamas79/MsgHatch). The package implements `IInboxStore`, `IOutboxStore`, and `IRegistrySettingsStore` of `MsgHatch.Domain` on one MongoDB database. It also has a dispatch signal: a change stream on the same database wakes the dispatch loops of each host at an insert into the inbox or the outbox.

## Install

```shell
dotnet add package MsgHatch.Data.MongoDb
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseMongoDbStores();
```

`UseMongoDbStores` binds `MongoDbStoreOptions` from the section `MsgHatch:MongoDb`, and validates them at the start of the host. It registers an `IMongoClient` for the connection string when the host has not registered one. A value from code wins over the configuration:

[//]: # (snippet: setup-code)
```csharp
services.AddMsgHatch()
    .UseMongoDbStores(o => o.DatabaseName = "shop-messages");
```

## Options

[//]: # (options: MongoDbStoreOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `ConnectionString` | `string` | none | Required. The connection string of the MongoDB server. |
| `DatabaseName` | `string` | `msghatch` | Required. Only ASCII letters, digits, `_`, and `-`, and at most 63 characters. |

[//]: # (snippet: options)
```json
{
  "MsgHatch": {
    "MongoDb": {
      "ConnectionString": "mongodb://localhost:27017/?replicaSet=rs0",
      "DatabaseName": "msghatch"
    }
  }
}
```

## Dispatch signal between hosts

`UseMongoDbDispatchSignal` wakes the dispatch loops of each host of the system, also on another machine, through one change stream on the database of the stores. A change stream needs a replica set. The startup validation fails when the host does not use the MongoDB stores, or when `EnablePolling` of the inbox or the outbox is false. A broken change stream only delays a send until the next poll.

[//]: # (snippet: signal)
```csharp
services.AddMsgHatch()
    .UseMongoDbStores()
    .UseMongoDbDispatchSignal();
```

## Version history

### 0.1.0 (2026-11-22)

- The first version: the MongoDB inbox, outbox, and registry settings stores with `UseMongoDbStores`, and the change stream dispatch signal with `UseMongoDbDispatchSignal`.
