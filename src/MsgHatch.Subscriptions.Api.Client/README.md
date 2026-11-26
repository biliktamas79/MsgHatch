# MsgHatch.Subscriptions.Api.Client

The HTTP client of the subscription management API of [MsgHatch.Subscriptions](https://github.com/biliktamas79/MsgHatch). The package implements `ISubscriptionQueries`, `ISubscriptionCommands`, and `IRoutingVerifier` over HTTP, against the endpoints of `MsgHatch.Subscriptions.AspNetCore` on another host. It calls the routes with no `/me`, for an admin UI.

The client runs the rules of each call first, so an input that breaks a rule gives the error of the rule with no HTTP call. A create is not idempotent, so a host does not add a retry handler.

## Install

```shell
dotnet add package MsgHatch.Subscriptions.Api.Client
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch(o => o.Roles = MsgHatchRoles.None)
    .UseSubscriptionApiClient(configureHttp: http => http.AddHttpMessageHandler<TokenHandler>());
```

`UseSubscriptionApiClient` selects the client for the three interfaces. `UseSubscriptionQueriesApiClient` selects it for the queries and the verifier, and `UseSubscriptionCommandsApiClient` for the commands. Each interface has its own named HTTP client, and the host adds its handlers with `configureHttp`. The options bind from the section `SubscriptionApiClient` under the root of `AddMsgHatch`, by default `MsgHatch:SubscriptionApiClient`, and a value from code wins.

## Options

[//]: # (options: SubscriptionApiClientOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `BaseAddress` | `Uri?` | none | Required. The absolute URI of the host of the API. |
| `Prefix` | `string` | `/subscriptions` | Required. The prefix of `MapMsgHatchSubscriptions` on the host. |
| `Timeout` | `TimeSpan` | `00:00:30` | 1 second to 10 minutes. A request that is too slow gives `Errors.Unavailable`. |

[//]: # (snippet: options)
```json
{
  "MsgHatch": {
    "SubscriptionApiClient": {
      "BaseAddress": "https://api.example.com/"
    }
  }
}
```

## Version history

### 0.8.0 (2026-11-26)

- Breaking: the constant `SubscriptionApiClientOptions.SectionName` is removed. `SubscriptionApiClientOptions.SectionKey` is `SubscriptionApiClient`, the key of the section under the root of `AddMsgHatch`, by default `MsgHatch:SubscriptionApiClient`. The section follows the root of `AddMsgHatch`, which the parameter `configSectionPath` of `MsgHatch.Domain` 0.8.0 sets. A host with the default root changes no setting.

### 0.7.1 (2026-11-26)

- The package has the MsgHatch icon. The code and the API do not change.

### 0.2.1 (2026-11-24)

- The package depends on the latest stable version of `Microsoft.Extensions.Http` within the major 10 at the time of the pack, not on 10.0.14. The API does not change.

### 0.1.0 (2026-11-22)

- The first version: the HTTP client of the subscription management API and of the routing verification with `UseSubscriptionApiClient`.
