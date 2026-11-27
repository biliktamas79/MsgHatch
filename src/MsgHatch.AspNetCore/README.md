# MsgHatch.AspNetCore

The ASP.NET Core HTTP endpoints of the delivery management API and of the registry administration of [MsgHatch](https://github.com/biliktamas79/MsgHatch). The endpoints call `IDeliveryQueries`, `IDeliveryCommands`, `IRegistryQueries`, and `IRegistryCommands` of `MsgHatch.Domain`.

The delivery endpoints can retry, cancel, and pause for the whole outbox, and the error texts of a provider can contain an address. Protect each group with an authorization policy, and the content group with a stricter one.

## Install

```shell
dotnet add package MsgHatch.AspNetCore
```

## Setup

The host registers MsgHatch with `AddMsgHatch` of `MsgHatch.Domain`, and then maps the three route groups:

[//]: # (snippet: setup)
```csharp
app.MapMsgHatchDelivery()
    .RequireAuthorization("DeliveryAdmin");
app.MapMsgHatchDeliveryContent()
    .RequireAuthorization("DeliveryContent");
app.MapMsgHatchRegistry()
    .RequireAuthorization("RegistryAdmin");
```

`MapMsgHatchDelivery` maps the overview: the traffic, the lists of the inbox and the outbox, the retry, the cancel, the purge, and the pause and the resume of a channel. `MapMsgHatchDeliveryContent` maps the content of a message. Both groups have the default prefix `/delivery`, and a host maps both with the same prefix. `MapMsgHatchRegistry` maps the channels and the message types under `/registry`. The HTTP client of `MsgHatch.Api.Client` calls the same routes. The package has no options.

## Version history

### 0.9.0 (2026-11-26)

- The package compiles only the half of the internal error mapping that gives the HTTP status of an error type, so it has no code that it does not call. The API and the behavior do not change.

### 0.7.1 (2026-11-26)

- The package has the MsgHatch icon. The code and the API do not change.

### 0.2.0 (2026-11-23)

- The outbox filter of the query and of the bodies of a retry and a cancel accepts the target kind `Tenant` with no `targetId` or an empty one. A tenant target with a `targetId`, and each other target with no `targetId`, give 400.

### 0.1.0 (2026-11-22)

- The first version: the HTTP endpoints of the delivery management API with `MapMsgHatchDelivery` and `MapMsgHatchDeliveryContent`, and of the registry administration with `MapMsgHatchRegistry`.
