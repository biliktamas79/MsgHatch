# MsgHatch.Channels.Email

The email channel of [MsgHatch](https://github.com/biliktamas79/MsgHatch). The package has the email composer, which renders an email from a template of `MsgHatch.Templates` and a payload, the email transport, and the `IEmailClient` contract. A client package sends the email: `MsgHatch.Channels.Email.AmazonSes`, `MsgHatch.Channels.Email.Resend`, or a client of the host.

## Install

```shell
dotnet add package MsgHatch.Channels.Email
```

A host that sends the emails also adds a client package, for example `MsgHatch.Channels.Email.AmazonSes`.

## Setup

A publisher host selects an email client. A client package has its own setup method, for example `UseAmazonSes`. A client of the host implements `IEmailClient`:

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseEmailClient<SmtpEmailClient>();
```

`UseEmailClient` also selects the email transport, with a logging wrapper for the client and the transport. The package has no options of its own. The retries, the down state, and the test delivery of the channel `Email` come from the options of `MsgHatch.Domain`.

## Register a message type on the email channel

In the payload builder of a message type, `AsEmail(name)` registers the email of one payload type. The content type is `Email:{name}`, and the name selects the template. The default engine is the placeholder engine of `MsgHatch.Templates`, so the payload type has the attribute `[PlaceholderPayload]`, and the generator of the package writes its placeholder writer. `AsEmail(name, engine)` selects another engine, for example `LiquidTemplates.Engine` of `MsgHatch.Templates.Liquid`.

The settings of a route on the email channel are `EmailChannelSettings`: the language of the email, and the format `Html` or `Text`.

The example app of the repository shows a complete registration: [`ExampleSetup.cs`](https://github.com/biliktamas79/MsgHatch/blob/main/examples/ExampleApp/ExampleSetup.cs).

## Version history

### 0.7.1 (2026-11-26)

- The package has the MsgHatch icon. The code and the API do not change.

### 0.6.0 (2026-11-25)

- Breaking: the package id is `MsgHatch.Channels.Email`, not `MsgHatch.Email`. Each namespace starts with `MsgHatch.Channels.Email`, not `MsgHatch.Email`. The class names and the setup methods do not change, so a host changes only its package reference and its `using` directives.

### 0.1.0 (2026-11-22)

- The first version: the email channel, the email composer on the template library, the email transport, and `IEmailClient` with `UseEmailClient`.
