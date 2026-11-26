# MsgHatch.Subscriptions.AdminUI

The Blazor admin pages of the subscriptions of [MsgHatch.Subscriptions](https://github.com/biliktamas79/MsgHatch): the list and the edit page of a subscription, the page of the routing verification, and a column "Subscriptions" of the message types page of `MsgHatch.AdminUI`.

The pages call `ISubscriptionQueries`, `ISubscriptionCommands`, and `IRoutingVerifier` of the host, usually the HTTP client of `MsgHatch.Subscriptions.Api.Client`. The pages use the layout of `MsgHatch.AdminUI`.

## Install

```shell
dotnet add package MsgHatch.Subscriptions.AdminUI
```

## Setup

The host selects the API client, for example `UseSubscriptionApiClient` of `MsgHatch.Subscriptions.Api.Client` with `MsgHatchRoles.None`, and adds the pages:

[//]: # (snippet: setup)
```csharp
services.AddMsgHatchSubscriptionsAdminUI();
```

The host maps the pages with the assembly of `SubscriptionAdminRoutes` as an additional assembly of its router, as `MsgHatch.AdminUI` describes. The package has no options.

## Version history

### 0.7.1 (2026-11-26)

- The package has the MsgHatch icon. The code and the API do not change.

### 0.1.0 (2026-11-22)

- The first version: the subscription list and edit pages, the routing verification page, and the column of the message types page, with `AddMsgHatchSubscriptionsAdminUI`.
