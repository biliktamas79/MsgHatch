# MsgHatch.Subscriptions.Data.FileSystem

The JSON file subscription store of [MsgHatch.Subscriptions](https://github.com/biliktamas79/MsgHatch). The package keeps the subscriptions as JSON files under `{RootPath}/subscriptions`, with one store instance for each tenant key. It shares the folder and the options of `MsgHatch.Data.FileSystem`.

## Install

```shell
dotnet add package MsgHatch.Subscriptions.Data.FileSystem
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseFileStores()
    .UseSubscriptions()
    .UseFileSubscriptionStore();
```

`UseFileSubscriptionStore` shares `FileStoreOptions` with `UseFileStores`: the options bind from the section `MsgHatch:FileStores`. A value from code here and in `UseFileStores` fails the startup validation, so set a value from code in one place only.

## Options

The options are `FileStoreOptions` of `MsgHatch.Data.FileSystem`:

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

### 0.1.0 (2026-11-22)

- The first version: the JSON file subscription store with `UseFileSubscriptionStore`.
