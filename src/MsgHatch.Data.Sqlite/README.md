# MsgHatch.Data.Sqlite

The SQLite stores of [MsgHatch](https://github.com/biliktamas79/MsgHatch). The package implements `IInboxStore`, `IOutboxStore`, and `IRegistrySettingsStore` of `MsgHatch.Domain` in one SQLite database file, or in one file for each tenant. The stores create their tables at the first use of a connection string.

SQLite allows one writer at a time, so the claims and the try records of all dispatchers wait for each other. The stores suit one machine and a moderate volume. The database file must be on a local disk.

## Install

```shell
dotnet add package MsgHatch.Data.Sqlite
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseSqliteStores();
```

`UseSqliteStores` binds `SqliteStoreOptions` from the section `MsgHatch:Sqlite`, and validates them at the start of the host. A value from code wins over the configuration:

[//]: # (snippet: setup-code)
```csharp
services.AddMsgHatch()
    .UseSqliteStores("Data Source=msghatch.db");
```

## Options

[//]: # (options: SqliteStoreOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `ConnectionString` | `string` | none | Required. The connection string of the one database of the stores. It must not contain `{tenant}`. |

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

## One database file for each tenant

`UseSqliteTenantStores<TTenantSource>` keeps the messages of each tenant in a file of its own. The tenant source gives the tenants that the dispatch loops ask. The system file keeps the messages with no tenant, the down states of the channels, and the registry settings.

[//]: # (snippet: tenants)
```csharp
services.AddMsgHatch()
    .UseSqliteTenantStores<ShopTenants>();
```

[//]: # (options: SqliteTenantStoreOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `TenantConnectionString` | `string` | none | Required. It must contain `{tenant}`, which the stores replace with the tenant id, escaped as a file name. The folder must exist. |
| `SystemConnectionString` | `string` | none | Required. The connection string of the system database. It must not contain `{tenant}`. |

[//]: # (snippet: tenant-options)
```json
{
  "MsgHatch": {
    "SqliteTenants": {
      "TenantConnectionString": "Data Source=data/msghatch-{tenant}.db",
      "SystemConnectionString": "Data Source=data/msghatch.db"
    }
  }
}
```

## Version history

### 0.2.1 (2026-11-24)

- The package depends on the latest stable version of `Microsoft.Data.Sqlite` within the major 10 at the time of the pack, not on 10.0.14. The API does not change.

### 0.1.0 (2026-11-22)

- The first version: the SQLite inbox, outbox, and registry settings stores on one database file with `UseSqliteStores`, and one database file for each tenant with `UseSqliteTenantStores<TTenantSource>`.
