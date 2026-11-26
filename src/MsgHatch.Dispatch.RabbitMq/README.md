# MsgHatch.Dispatch.RabbitMq

The dispatch signal of [MsgHatch](https://github.com/biliktamas79/MsgHatch) between the hosts of one system on different machines. The package implements `IDispatchSignal` of `MsgHatch.Domain` with a fanout exchange of RabbitMQ, through `RabbitMQ.Client`, with any store.

Each process consumes one exclusive, auto-delete queue with a name from the broker, and skips its own messages. A lost connection makes the dispatch loops poll, and the signal connects again after 5 seconds. So the dispatch loops must poll.

## Install

```shell
dotnet add package MsgHatch.Dispatch.RabbitMq
```

## Setup

Each process of the system selects the same shared stores, for example the SQLite stores, and calls the setup method with the same broker and the same exchange name:

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseRabbitMqDispatchSignal();
```

`UseRabbitMqDispatchSignal` binds `RabbitMqSignalOptions` from the section `RabbitMqSignal` under the root of `AddMsgHatch`, by default `MsgHatch:RabbitMqSignal`, and validates them at the start of the host. The startup validation fails when `EnablePolling` of the inbox or the outbox is false. A value from code wins over the configuration:

[//]: # (snippet: setup-code)
```csharp
services.AddMsgHatch()
    .UseRabbitMqDispatchSignal(o => o.ExchangeName = "shop.dispatch");
```

## Options

[//]: # (options: RabbitMqSignalOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `Uri` | `Uri` | none | Required. The AMQP URI of the broker, with the scheme `amqp` or `amqps`. |
| `ExchangeName` | `string` | `msghatch.dispatch` | Required. The fanout exchange of the signal. 1 to 255 characters from the ASCII letters, the digits, `_`, `-`, `.`, and `:`, and no start `amq.`. Two systems on one broker use two names. |

[//]: # (snippet: options)
```json
{
  "MsgHatch": {
    "RabbitMqSignal": {
      "Uri": "amqp://127.0.0.1:5672",
      "ExchangeName": "msghatch.dispatch"
    }
  }
}
```

## Version history

### 0.8.0 (2026-11-26)

- Breaking: the constant `RabbitMqSignalOptions.SectionName` is removed. `RabbitMqSignalOptions.SectionKey` is `RabbitMqSignal`, the key of the section under the root of `AddMsgHatch`, by default `MsgHatch:RabbitMqSignal`. The section follows the root of `AddMsgHatch`, which the parameter `configSectionPath` of `MsgHatch.Domain` 0.8.0 sets. A host with the default root changes no setting.

### 0.7.1 (2026-11-26)

- The package has the MsgHatch icon. The code and the API do not change.

### 0.1.0 (2026-11-22)

- The first version: the RabbitMQ dispatch signal between the hosts of one system with `UseRabbitMqDispatchSignal`.
