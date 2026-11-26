# MsgHatch.Data.FileSystem

The JSON file stores of [MsgHatch](https://github.com/biliktamas79/MsgHatch). The package implements `IInboxStore`, `IOutboxStore`, and `IRegistrySettingsStore` of `MsgHatch.Domain` as JSON files in one folder: the inbox under `{RootPath}/inbox`, the outbox under `{RootPath}/outbox`, and the registry settings under `{RootPath}/registry`.

The stores depend on an atomic rename of a file. On Linux and macOS, they also need hard links in the folder. Some network file systems do not guarantee them, so use a database store there.

## Install

```shell
dotnet add package MsgHatch.Data.FileSystem
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseFileStores();
```

`UseFileStores` binds `FileStoreOptions` from the section `MsgHatch:FileStores`, and validates them at the start of the host. A value from code wins over the configuration:

[//]: # (snippet: setup-code)
```csharp
services.AddMsgHatch()
    .UseFileStores("data/msghatch");
```

## Options

[//]: # (options: FileStoreOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `RootPath` | `string` | none | Required. The folder of the stores. |

[//]: # (snippet: options)
```json
{
  "MsgHatch": {
    "FileStores": {
      "RootPath": "data/msghatch"
    }
  }
}
```

## Version history

### 0.7.1 (2026-11-26)

- The package has the MsgHatch icon. The code and the API do not change.

### 0.1.1 (2026-11-23)

- Fix: on Linux and macOS, two parallel changes of the state of one channel could both report success, and the second state row replaced the first. So the pause of an operator could be lost. A new state row now gets its name with a hard link, which fails when the row exists.

### 0.1.0 (2026-11-22)

- The first version: the JSON file inbox, outbox, and registry settings stores in one folder with `UseFileStores`.
