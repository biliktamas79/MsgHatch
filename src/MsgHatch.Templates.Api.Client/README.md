# MsgHatch.Templates.Api.Client

The HTTP client of the template management API of [MsgHatch](https://github.com/biliktamas79/MsgHatch). The package implements `ITemplateQueries` and `ITemplateCommands` of `MsgHatch.Templates` over HTTP, against the endpoints of `MsgHatch.Templates.AspNetCore` on another host. The host of the API renders the preview with its engines and its samples, so a client host needs no engine and no payload type.

The client runs the rules of each call first, so an input that breaks a rule gives the error of the rule with no HTTP call.

## Install

```shell
dotnet add package MsgHatch.Templates.Api.Client
```

## Setup

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch(o => o.Roles = MsgHatchRoles.None)
    .UseTemplateApiClient(configureHttp: http => http.AddHttpMessageHandler<TokenHandler>());
```

`UseTemplateApiClient` selects the client for both interfaces. `UseTemplateQueriesApiClient` and `UseTemplateCommandsApiClient` select it for one interface. Each interface has its own named HTTP client, and the host adds its handlers with `configureHttp`. The options bind from the section `MsgHatch:TemplateApiClient`, and a value from code wins.

## Options

[//]: # (options: TemplateApiClientOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `BaseAddress` | `Uri?` | none | Required. The absolute URI of the host of the API. |
| `Prefix` | `string` | `/templates` | Required. The prefix of `MapMsgHatchTemplates` on the host. |
| `Timeout` | `TimeSpan` | `00:00:30` | 1 second to 10 minutes. A request that is too slow gives `Errors.Unavailable`. |

[//]: # (snippet: options)
```json
{
  "MsgHatch": {
    "TemplateApiClient": {
      "BaseAddress": "https://api.example.com/"
    }
  }
}
```

## Version history

### 0.2.1 (2026-11-24)

- The package depends on the latest stable version of `Microsoft.Extensions.Http` within the major 10 at the time of the pack, not on 10.0.14. The API does not change.

### 0.1.0 (2026-11-22)

- The first version: the HTTP client of the template management API with `UseTemplateApiClient`, with the preview of a template.
