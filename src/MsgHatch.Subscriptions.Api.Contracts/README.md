# MsgHatch.Subscriptions.Api.Contracts

The HTTP contracts of the subscription management API of [MsgHatch.Subscriptions](https://github.com/biliktamas79/MsgHatch): the request and response bodies, their mappings to the types of `MsgHatch.Subscriptions`, their source-generated JSON context, and `JsonChannelSettings` for the channel settings of a client with no settings type.

The endpoints of `MsgHatch.Subscriptions.AspNetCore` and the client of `MsgHatch.Subscriptions.Api.Client` share these bodies. The package has no HTTP concern, no setup method, and no options.

## Install

```shell
dotnet add package MsgHatch.Subscriptions.Api.Contracts
```

## Version history

### 0.1.0 (2026-11-22)

- The first version: the bodies of the subscription management API with their JSON context, and `JsonChannelSettings`.
