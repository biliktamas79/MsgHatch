# MsgHatch.Domain

The core of [MsgHatch](https://github.com/biliktamas79/MsgHatch), a .NET library for message routing, message templates, and delivery through an inbox and an outbox. The package has:
- The DI builder `AddMsgHatch` and the registration of the message types.
- The router contract `IMessageRouter<TEvent>`, the payload loader contract, and the composer contract of the channels.
- The inbox processor and the outbox dispatcher: dispatch loops with leases, retries, expiry, and the down state of a channel.
- The store contracts, the in-memory stores, the registries of the channels and the message types, and the dispatch signal in one process.
- The delivery management API and the registry API (`IDeliveryQueries`, `IDeliveryCommands`, `IRegistryQueries`, and `IRegistryCommands`).
- The traces and the metrics of `MsgHatchTelemetry`.

A host adds a channel package (for example `MsgHatch.Email`), a store package (for example `MsgHatch.Data.Sqlite`), and a router (for example `MsgHatch.Subscriptions`).

## Install

```shell
dotnet add package MsgHatch.Domain
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch(o => o.DefaultLanguage = "hu")
    .UseInMemoryStores();
```

`AddMsgHatch` binds `MsgHatchOptions` from the section `MsgHatch`, and validates all options at the start of the host. A value from code wins over the configuration, also when the host calls `AddMsgHatch` more than one time. The in-memory stores are not durable: a restart loses each message in the inbox or the outbox, so use them only for tests and demos.

A host with only one role sets `Roles`: a consumer host takes the events into the inbox and routes them, and a publisher host sends the outgoing messages. A host that only calls the API clients sets `MsgHatchRoles.None`.

The example app of the repository shows a complete host with message types, channels, and stores: [`ExampleSetup.cs`](https://github.com/biliktamas79/MsgHatch/blob/main/examples/ExampleApp/ExampleSetup.cs).

## Options

[//]: # (options: MsgHatchOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `DefaultLanguage` | `string` | `en` | Required. A culture that .NET knows. The language when no other rule gives one. |
| `EnableLogging` | `bool` | `true` | Adds or removes all logging wrappers. |
| `Roles` | `MsgHatchRoles` | `Consumer, Publisher` | The roles of the host. |

The dispatch loops of the inbox and the outbox have the named options `Inbox` and `Outbox` in the section `MsgHatch:Dispatch`:

[//]: # (options: DispatchOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `MaxDegreeOfParallelism` | `int` | `8` | 1 to 256. The send workers of each dispatch loop. |
| `QueueCapacity` | `int` | `16` | 1 to 4096, and at least `MaxDegreeOfParallelism`. |
| `Lease` | `TimeSpan` | `00:01:00` | 5 seconds to 1 hour. The lease of a claimed message. |
| `LeaseSafetyMargin` | `TimeSpan` | `00:00:10` | 0 to 30 minutes, and less than `Lease`. A worker skips a message whose lease ends within this time. |
| `PollInterval` | `TimeSpan` | `00:00:01` | 10 milliseconds to 10 minutes. The first poll delay after an empty claim. |
| `MaxPollInterval` | `TimeSpan` | `00:00:30` | 10 milliseconds to 1 hour, and not less than `PollInterval`. |
| `EnablePolling` | `bool` | `true` | Set it to false only where a signal reaches each loop, for example in one process. |

The retries have the named options `Inbox`, a channel name, or the content type of one registration in the section `MsgHatch:Retry`:

[//]: # (options: RetryOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `MaxTryCount` | `int` | `8` | 1 to 100. The maximum number of tries of one message. |
| `BaseDelay` | `TimeSpan` | `00:00:05` | 1 millisecond to 1 hour. The delay after the first failed try. |
| `MaxDelay` | `TimeSpan` | `00:15:00` | 1 second to 7 days, and not less than `BaseDelay`. |
| `Expiry` | `TimeSpan?` | none | 1 second to 30 days. The time after the accept of the event after which a message is not sent. Not for `Inbox`. |

The automatic down state of a channel has the named options of the channel name in the section `MsgHatch:ChannelDown`:

[//]: # (options: ChannelDownOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `ErrorThreshold` | `int` | `5` | 1 to 1000. The transient errors in a row after which a dispatch loop marks the channel down. |
| `CheckInterval` | `TimeSpan` | `00:00:30` | 1 second to 1 hour. The time between two health checks of a down channel. |

A test message, with `MessageFlags.Test`, follows the named options of the channel name in the section `MsgHatch:TestDelivery`:

[//]: # (options: TestDeliveryOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `Mode` | `TestDeliveryMode` | `Suppress` | How the channel handles a test message. |
| `RedirectTo` | `string?` | none | At most 256 characters. The test address or group of the mode `Redirect`, which needs it. |

The stored registry, of `UseStoredRegistry`, reads its settings again after an interval:

[//]: # (options: RegistryOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `RefreshInterval` | `TimeSpan` | `00:00:30` | 1 second to 1 hour. A write of another process is visible after it. |

[//]: # (snippet: options)
```json
{
  "MsgHatch": {
    "DefaultLanguage": "en",
    "Dispatch": {
      "Outbox": {
        "MaxDegreeOfParallelism": 16,
        "PollInterval": "00:00:02"
      }
    },
    "Retry": {
      "Inbox": {
        "MaxTryCount": 10,
        "MaxDelay": "00:30:00"
      }
    },
    "Registry": {
      "RefreshInterval": "00:01:00"
    }
  }
}
```

The sections of a channel, for example `MsgHatch:Retry:Email`, `MsgHatch:ChannelDown:Email`, and `MsgHatch:TestDelivery:Email`, bind for each message type that the host registers on that channel.

## Version history

### 0.2.1 (2026-11-24)

- The package depends on the latest stable versions of the `Microsoft.Extensions` packages within the major 10 at the time of the pack, not on 10.0.14. The API does not change.

### 0.2.0 (2026-11-23)

- The delivery target kind `Tenant`: a target for all users of the tenant of the message, with an empty target id. A router of the host can return it, for example for a SignalR message to a whole tenant. The deterministic id of a tenant target differs from the id of a user target and of a company target.

### 0.1.2 (2026-11-23)

- Fix: a second call of `AddMsgHatch` bound the section `MsgHatch` again, so a value of the configuration replaced a value from code of an earlier call. Now only the first call binds the options, and a value from code wins in each order of the calls.

### 0.1.0 (2026-11-22)

- The first version: the DI builder, the message registration, the inbox processor and the outbox dispatcher, the stores and the in-memory stores, the registries, the dispatch signal in one process, a storage for each tenant, the delivery and registry APIs, and the telemetry.
