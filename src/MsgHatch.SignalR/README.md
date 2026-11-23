# MsgHatch.SignalR

The SignalR channel of [MsgHatch](https://github.com/biliktamas79/MsgHatch). The package has the SignalR composer, which puts a payload into a `SignalREnvelope<TPayload>`, and the transport, which sends the envelope through a SignalR hub of the host to a user, to the group of a company, or to the group of a tenant. Each SignalR name carries the tenant.

## Install

```shell
dotnet add package MsgHatch.SignalR
```

## Setup

A publisher host selects its hub. The setup method also calls `AddSignalR`:

[//]: # (snippet: setup)
```csharp
services.AddMsgHatch()
    .UseSignalR<NotificationHub>();
```

The hub of the host must add each connection to the group of the company of its user, and to the group of its tenant, because a connection in no group gets no company message and no tenant message. `SignalRGroups` gives the names of the groups. For example, a hub that reads the tenant and the company from the claims of the user:

[//]: # (snippet: hub)
```csharp
public override async Task OnConnectedAsync()
{
    var tenant = new TenantId(Context.User!.FindFirstValue("tenant")!);
    var company = new CompanyId(Context.User!.FindFirstValue("company")!);
    await Groups.AddToGroupAsync(Context.ConnectionId, SignalRGroups.ForTenant(tenant), Context.ConnectionAborted);
    await Groups.AddToGroupAsync(Context.ConnectionId, SignalRGroups.ForCompany(tenant, company), Context.ConnectionAborted);
    await base.OnConnectedAsync();
}
```

A router of the host sends a message to all connected users of the tenant of the message with the target kind `Tenant` and an empty target id. A host with no tenant uses `SignalRGroups.ForTenant(null)`, the group of all users. The package has no options of its own. The retries, the down state, and the test delivery of the channel `SignalR` come from the options of `MsgHatch.Domain`.

## Register a message type on the SignalR channel

In the payload builder of a message type, `AsSignalR(name, version, json)` registers the SignalR message of one payload type. The content type is `SignalR:{name}`, and the JSON type information of `SignalREnvelope<TPayload>` comes from a source-generated JSON context of the host. With no retry configuration, a message has 3 tries and expires after 5 minutes. The example app of the repository shows a complete registration: [`ExampleSetup.cs`](https://github.com/biliktamas79/MsgHatch/blob/main/examples/ExampleApp/ExampleSetup.cs).

## Version history

### 0.2.0 (2026-11-23)

- The transport sends a message with the target kind `Tenant` to the group of the tenant of the message, which `SignalRGroups.ForTenant` gives: `msghatch:tenant:{TenantId}`, or `msghatch:all` with no tenant. A tenant target with an id gives `Errors.ValidationFailed`.

### 0.1.0 (2026-11-22)

- The first version: the SignalR channel, the SignalR composer, and the transport through a hub of the host with `UseSignalR<THub>`.
