# MsgHatch.AdminUI

The Blazor admin pages of [MsgHatch](https://github.com/biliktamas79/MsgHatch): the pages of the channels and the message types, with their edit pages, the check, the pause, and the resume of a channel, and the delivery pages of the traffic, the outbox, and the inbox. It also has the shared parts of all MsgHatch admin pages: the list and edit layouts, the pager, the dialogs, and the text of each error type.

The pages call `IRegistryQueries`, `IRegistryCommands`, `IDeliveryQueries`, and `IDeliveryCommands` of the host, usually the HTTP clients of `MsgHatch.Api.Client`. A page sets no render mode, so the host selects Interactive Server or WebAssembly. The pages use the CSS classes and the color variables of Bootstrap 5, which the host loads, so the host also selects the dark mode.

## Install

```shell
dotnet add package MsgHatch.AdminUI
```

## Setup

The host selects the API clients, for example `UseRegistryApiClient` and `UseDeliveryApiClient` of `MsgHatch.Api.Client` with `MsgHatchRoles.None`, and adds the pages:

[//]: # (snippet: setup)
```csharp
services.AddMsgHatchAdminUI();
```

The host maps the pages with the assembly of the pages as an additional assembly of its router: `MapRazorComponents<App>().AddAdditionalAssemblies(typeof(RegistryAdminRoutes).Assembly)`.

`RegistryAdminRoutes` and `DeliveryAdminRoutes` hold the routes of the pages, for a menu of the host. The host adds the authorization, and loads the bundle of the scoped styles `{assembly}.styles.css`. `AddMessageTypeColumn<TColumn>` adds a column of another package to the message types page. The package has no options.

The admin UI example of the repository shows a complete host: [`AdminUiApplication.cs`](https://github.com/biliktamas79/MsgHatch/blob/main/examples/AdminUiExample/AdminUiApplication.cs).

## Version history

### 0.1.0 (2026-11-22)

- The first version: the pages of the channels, the message types, and the delivery with `AddMsgHatchAdminUI`, and the shared layout of the admin pages.
