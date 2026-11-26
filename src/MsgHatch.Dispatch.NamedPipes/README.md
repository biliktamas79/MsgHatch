# MsgHatch.Dispatch.NamedPipes

The dispatch signal of [MsgHatch](https://github.com/biliktamas79/MsgHatch) between the processes of one OS user on one machine. The package implements `IDispatchSignal` of `MsgHatch.Domain` with named pipes: a publisher process wakes the dispatch loops of the other processes at once, so a send does not wait for the next poll.

A lost signal only delays a send until the next poll, so the dispatch loops must poll.

## Install

```shell
dotnet add package MsgHatch.Dispatch.NamedPipes
```

## Setup

Each process of the system selects the same shared stores, for example the SQLite stores, and calls the setup method with the same pipe name prefix:

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseNamedPipeDispatchSignal();
```

`UseNamedPipeDispatchSignal` binds `NamedPipeSignalOptions` from the section `MsgHatch:NamedPipeSignal`, and validates them at the start of the host. The startup validation fails when `EnablePolling` of the inbox or the outbox is false. A value from code wins over the configuration:

[//]: # (snippet: setup-code)
```csharp
services.AddMsgHatch()
    .UseNamedPipeDispatchSignal(o => o.PipeNamePrefix = "shop");
```

## Options

[//]: # (options: NamedPipeSignalOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `PipeNamePrefix` | `string` | none | Required. The start of the name of each pipe. 1 to 40 characters from the ASCII letters, the digits, `_`, and `-`. Two systems on one machine use two prefixes. |

[//]: # (snippet: options)
```json
{
  "MsgHatch": {
    "NamedPipeSignal": {
      "PipeNamePrefix": "shop"
    }
  }
}
```

## Version history

### 0.7.1 (2026-11-26)

- The package has the MsgHatch icon. The code and the API do not change.

### 0.1.0 (2026-11-22)

- The first version: the named pipe dispatch signal between the processes of one machine with `UseNamedPipeDispatchSignal`.
