# MsgHatch.Broker.Wolverine

The Wolverine client of the [MsgHatch](https://github.com/biliktamas79/MsgHatch) broker channel. The package implements `IBrokerClient` of `MsgHatch.Broker` with the Wolverine of the host. The host selects the transport of Wolverine, for example RabbitMQ or Amazon SQS.

## Install

```shell
dotnet add package MsgHatch.Broker.Wolverine
```

## Setup

The host registers Wolverine with `Durability.UseSyncRetryBlock` true, and then selects the client:

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseWolverineBroker();
```

The startup validation fails when the host did not register Wolverine, or when `Durability.UseSyncRetryBlock` is false. The package has no options of its own: the routes and the transport of each message are settings of Wolverine.

## Version history

### 0.2.1 (2026-11-24)

- The package depends on the latest stable version of `WolverineFx` within the major 6 at the time of the pack, not on 6.61.0. The API does not change.

### 0.1.0 (2026-11-22)

- The first version: the Wolverine broker client with `UseWolverineBroker`.
