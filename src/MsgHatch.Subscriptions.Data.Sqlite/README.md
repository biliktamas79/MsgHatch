# MsgHatch.Subscriptions.Data.Sqlite

The SQLite subscription store of [MsgHatch.Subscriptions](https://github.com/biliktamas79/MsgHatch). The package keeps the subscriptions in the database of `MsgHatch.Data.Sqlite`, with one store instance for each tenant key. The store creates its tables at the first use of a connection string.

## Install

```shell
dotnet add package MsgHatch.Subscriptions.Data.Sqlite
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseSqliteStores()
    .UseSubscriptions()
    .UseSqliteSubscriptionStore();
```

`UseSqliteSubscriptionStore` shares `SqliteStoreOptions` with `UseSqliteStores`: the options bind from the section `MsgHatch:Sqlite`. A value from code here and in `UseSqliteStores` fails the startup validation, so set a value from code in one place only.

With one database file for each tenant, `UseSqliteTenantSubscriptionStore` keeps the subscriptions of each tenant in the file of that tenant. It shares `SqliteTenantStoreOptions` with `UseSqliteTenantStores` of `MsgHatch.Data.Sqlite`.

## Options

The options are `SqliteStoreOptions` of `MsgHatch.Data.Sqlite`:

[//]: # (snippet: options)
```json
{
  "MsgHatch": {
    "Sqlite": {
      "ConnectionString": "Data Source=msghatch.db"
    }
  }
}
```

## Version history

### 0.1.0 (2026-11-22)

- The first version: the SQLite subscription store with `UseSqliteSubscriptionStore`, and one file for each tenant with `UseSqliteTenantSubscriptionStore`.
