# MsgHatch.Channels.Broker

The message broker channel of [MsgHatch](https://github.com/biliktamas79/MsgHatch). The package has the broker composer, which puts a payload into a `BrokerEnvelope<TPayload>`, the broker transport, and the `IBrokerClient` contract. A client package publishes the message: `MsgHatch.Channels.Broker.Wolverine`, `MsgHatch.Channels.Broker.AmazonSns`, or a client of the host.

## Install

```shell
dotnet add package MsgHatch.Channels.Broker
```

A host that publishes the messages also adds a client package, for example `MsgHatch.Channels.Broker.AmazonSns`.

## Setup

A publisher host selects a broker client. A client package has its own setup method, for example `UseAmazonSnsBroker`. A client of the host implements `IBrokerClient`:

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseBrokerClient<KafkaBrokerClient>();
```

`UseBrokerClient` also selects the broker transport, with a logging wrapper for the client and the transport. The package has no options of its own. The retries, the down state, and the test delivery of the channel `Broker` come from the options of `MsgHatch.Domain`.

## Register a message type on the broker channel

In the payload builder of a message type, `AsBroker(name, version, json)` registers the broker message of one payload type. The content type is `Broker:{name}`, and the JSON type information of `BrokerEnvelope<TPayload>` comes from a source-generated JSON context of the host. A broker message has no expiry by default. The example app of the repository shows a complete registration: [`ExampleSetup.cs`](https://github.com/biliktamas79/MsgHatch/blob/main/examples/ExampleApp/ExampleSetup.cs).

## Version history

### 0.7.1 (2026-11-26)

- The package has the MsgHatch icon. The code and the API do not change.

### 0.6.0 (2026-11-25)

- Breaking: the package id is `MsgHatch.Channels.Broker`, not `MsgHatch.Broker`. Each namespace starts with `MsgHatch.Channels.Broker`, not `MsgHatch.Broker`. The class names and the setup methods do not change, so a host changes only its package reference and its `using` directives.

### 0.1.0 (2026-11-22)

- The first version: the broker channel, the broker composer, the broker transport, and `IBrokerClient` with `UseBrokerClient`.
