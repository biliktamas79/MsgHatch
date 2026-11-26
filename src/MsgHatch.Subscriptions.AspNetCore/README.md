# MsgHatch.Subscriptions.AspNetCore

The ASP.NET Core HTTP endpoints of the subscription management API of [MsgHatch.Subscriptions](https://github.com/biliktamas79/MsgHatch). The endpoints call `ISubscriptionQueries`, `ISubscriptionCommands`, and `IRoutingVerifier` of the host, as minimal API endpoints with a source-generated request delegate.

The endpoints accept each company id and user id in their filters and drafts. Protect them with an authorization policy, or give the end users only the `/me` routes.

## Install

```shell
dotnet add package MsgHatch.Subscriptions.AspNetCore
```

## Setup

The host registers the subscription router with `UseSubscriptions` of `MsgHatch.Subscriptions`, and then maps the endpoints:

[//]: # (snippet: setup)
```csharp
app.MapMsgHatchSubscriptions()
    .RequireAuthorization("SubscriptionAdmin");
```

The default prefix is `/subscriptions`. With `CurrentUser`, the endpoints also exist under `{prefix}/me`, for the user-level subscriptions of the signed-in user only:

[//]: # (snippet: setup-me)
```csharp
app.MapMsgHatchSubscriptions("/subscriptions", o =>
{
    o.CurrentUser = user => user.FindFirstValue("sub") is { } id ? UserId.Create(id).Value : null;
});
```

## Options

The options of the mapping are code only, with no configuration section:

[//]: # (options: SubscriptionEndpointOptions)
| Option | Type | Default | Rule |
|---|---|---|---|
| `CurrentUser` | `Func<ClaimsPrincipal, UserId?>?` | none | Gives the user id of the signed-in user. With a value, the `/me` routes exist. A null user id there gives 403. |
| `CurrentTenant` | `Func<ClaimsPrincipal, TenantId?>?` | none | Gives the tenant of the signed-in user for the `/me` routes. It needs `CurrentUser`. With no function, the `/me` routes use no tenant. |

## Version history

### 0.7.1 (2026-11-26)

- The package has the MsgHatch icon. The code and the API do not change.

### 0.1.0 (2026-11-22)

- The first version: the HTTP endpoints of the subscription management API and of the routing verification with `MapMsgHatchSubscriptions`.
