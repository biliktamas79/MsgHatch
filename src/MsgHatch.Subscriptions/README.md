# MsgHatch.Subscriptions

The subscription router of [MsgHatch](https://github.com/biliktamas79/MsgHatch). The package implements `IMessageRouter<TEvent>` of `MsgHatch.Domain` from managed subscriptions on three owner levels: the tenant, the company, and the user. It also has the subscription management API (`ISubscriptionQueries` and `ISubscriptionCommands`), the routing verifier (`IRoutingVerifier`), and the subscription cache wrapper.

The consumer gives the recipients through `IRecipientDirectory`: the users, the companies, their languages, and their addresses. A store package keeps the subscriptions.

## Install

```shell
dotnet add package MsgHatch.Subscriptions
```

A host with a database also adds a store package, for example `MsgHatch.Subscriptions.Data.Sqlite`.

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseSubscriptions()
    .UseRecipientDirectory<ShopRecipients>()
    .UseInMemorySubscriptionStore();
```

`UseSubscriptions` selects the subscription router for each message type with no router of its own. It registers the query, command, and verifier interfaces as scoped tenant services, each with a logging wrapper. `UseInMemorySubscriptionStore` is for a host with no database: it is not durable, and each tenant key has its own instance.

A message type of the subscription router gives the scope of an event with `WithScope`, for example `e => NotificationScope.ForUsers(e.CustomerIds)`, and can give test cases for the routing verifier with `WithTestEvent`.

`UseSubscriptionQueries`, `UseSubscriptionCommands`, and `UseRoutingVerifier` select another implementation of an interface, for example the HTTP client of `MsgHatch.Subscriptions.Api.Client`. A cache package turns on the subscription cache, for example `UseHybridSubscriptionCache` of `MsgHatch.Subscriptions.Caching.Hybrid`.

The example app of the repository shows a complete registration: [`ExampleSetup.cs`](https://github.com/biliktamas79/MsgHatch/blob/main/examples/ExampleApp/ExampleSetup.cs). The subscription domain, with its merge rules and examples, is in [`docs/subscription-domain.md`](https://github.com/biliktamas79/MsgHatch/blob/main/docs/subscription-domain.md).

## Version history

### 0.1.0 (2026-11-22)

- The first version: the subscription router with `UseSubscriptions`, the management API, the routing verifier, the recipient directory contract, the in-memory subscription store, and the subscription cache wrapper.
