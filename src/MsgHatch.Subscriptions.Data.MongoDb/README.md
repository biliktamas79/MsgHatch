# MsgHatch.Subscriptions.Data.MongoDb

The MongoDB subscription store of [MsgHatch.Subscriptions](https://github.com/biliktamas79/MsgHatch). The package keeps the subscriptions in the database of `MsgHatch.Data.MongoDb`, with one store instance for each tenant key. The store creates its indexes at the first use of a database.

## Install

```shell
dotnet add package MsgHatch.Subscriptions.Data.MongoDb
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseMongoDbStores()
    .UseSubscriptions()
    .UseMongoDbSubscriptionStore();
```

`UseMongoDbSubscriptionStore` shares `MongoDbStoreOptions` and the `IMongoClient` with `UseMongoDbStores`: the options bind from the section `MsgHatch:MongoDb`.

## Options

The options are `MongoDbStoreOptions` of `MsgHatch.Data.MongoDb`:

[//]: # (snippet: options)
```json
{
  "MsgHatch": {
    "MongoDb": {
      "ConnectionString": "mongodb://localhost:27017",
      "DatabaseName": "msghatch"
    }
  }
}
```

## Version history

### 0.1.0 (2026-11-22)

- The first version: the MongoDB subscription store with `UseMongoDbSubscriptionStore`.
