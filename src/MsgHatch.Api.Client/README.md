# MsgHatch.Api.Client

The HTTP client of the registry API and of the delivery API of [MsgHatch](https://github.com/biliktamas79/MsgHatch). The package implements `IRegistryQueries`, `IRegistryCommands`, `IDeliveryQueries`, and `IDeliveryCommands` of `MsgHatch.Domain` over HTTP, against the endpoints of `MsgHatch.AspNetCore` on another host. An admin UI host uses it with `MsgHatchRoles.None`.

The client runs the rules of each call first, so an input that breaks a rule gives the error of the rule with no HTTP call. A create is not idempotent, so a host does not add a retry handler.

## Install

```shell
dotnet add package MsgHatch.Api.Client
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch(o => o.Roles = MsgHatchRoles.None)
    .UseRegistryApiClient(configureHttp: http => http.AddHttpMessageHandler<TokenHandler>())
    .UseDeliveryApiClient(configureHttp: http => http.AddHttpMessageHandler<TokenHandler>());
```

Each interface has its own named HTTP client. The host adds its handlers with `configureHttp`, for example a handler that adds a token. Each setup method binds its options from its own section under the root of `AddMsgHatch`, by default `MsgHatch:RegistryApiClient` and `MsgHatch:DeliveryApiClient`, and a value from code wins. An overload takes the base address: `UseRegistryApiClient(new Uri("https://api.example.com/"))`.

## Options

[//]: # (options: RegistryApiClientOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `BaseAddress` | `Uri?` | none | Required. The absolute URI of the host of the API. |
| `Prefix` | `string` | `/registry` | Required. The prefix of `MapMsgHatchRegistry` on the host. |
| `Timeout` | `TimeSpan` | `00:00:30` | 1 second to 10 minutes. A request that is too slow gives `Errors.Unavailable`. |

[//]: # (options: DeliveryApiClientOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `BaseAddress` | `Uri?` | none | Required. The absolute URI of the host of the API. |
| `Prefix` | `string` | `/delivery` | Required. The prefix of `MapMsgHatchDelivery` and `MapMsgHatchDeliveryContent` on the host. |
| `Timeout` | `TimeSpan` | `00:00:30` | 1 second to 10 minutes. A request that is too slow gives `Errors.Unavailable`. |

[//]: # (snippet: options)
```json
{
  "MsgHatch": {
    "RegistryApiClient": {
      "BaseAddress": "https://api.example.com/"
    },
    "DeliveryApiClient": {
      "BaseAddress": "https://api.example.com/",
      "Timeout": "00:01:00"
    }
  }
}
```

## Version history

### 0.8.0 (2026-11-26)

- Breaking: the constants `RegistryApiClientOptions.SectionName` and `DeliveryApiClientOptions.SectionName` are removed. `RegistryApiClientOptions.SectionKey` is `RegistryApiClient`, and `DeliveryApiClientOptions.SectionKey` is `DeliveryApiClient`, the keys of the sections under the root of `AddMsgHatch`, by default `MsgHatch:RegistryApiClient` and `MsgHatch:DeliveryApiClient`. The sections follow the root of `AddMsgHatch`, which the parameter `configSectionPath` of `MsgHatch.Domain` 0.8.0 sets. A host with the default root changes no setting.

### 0.7.1 (2026-11-26)

- The package has the MsgHatch icon. The code and the API do not change.

### 0.2.1 (2026-11-24)

- The package depends on the latest stable version of `Microsoft.Extensions.Http` within the major 10 at the time of the pack, not on 10.0.14. The API does not change.

### 0.1.0 (2026-11-22)

- The first version: the HTTP clients of the registry API with `UseRegistryApiClient` and of the delivery API with `UseDeliveryApiClient`.
