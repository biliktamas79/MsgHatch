# Consume the SignalR notifications in a browser

This guide tells a front-end developer how a browser page gets the MsgHatch notifications of the SignalR channel at the three target levels: a tenant, a company, and a user. The server side is in the README of [`MsgHatch.SignalR`](../src/MsgHatch.SignalR/README.md). The working example is the [SignalR demo](../examples/SignalRDemo/README.md).

The TypeScript sample is [`msghatch-signalr-client.ts`](../examples/SignalRDemo/client-ts/msghatch-signalr-client.ts). It uses the npm package `@microsoft/signalr`, and it is the same flow as `wwwroot/js/session.js` of the demo.

## The contract between the server and the client

| Item | Value |
|---|---|
| Client method name | The message type name, for example `DemoNotice`. |
| Arguments | One argument: the payload of the message, which the payload loader of the host gives. |
| Payload names | camelCase, because the SignalR JSON protocol of ASP.NET Core uses camelCase by default. |
| Tenant target | The group `msghatch:tenant:{tenant}`. |
| Company target | The group `msghatch:{tenant}:company:{company}`. |
| User target | The SignalR user name `{tenant}:{user}`. The `IUserIdProvider` of the host gives it. |

The client never uses a group name or a user name. The hub adds the connection to its groups, and the token gives the user name. A tenant id, a company id, and a user id are escaped in these names: `%` becomes `%25`, and `:` becomes `%3A`.

Add one handler for each message type on the SignalR channel. In TypeScript, add one entry to `NotificationMap` for each message type, so that each handler gets a typed payload.

## The steps of a page

1. Find the tenant, and the company if the page has one. The domain or the landing page gives them. The demo README describes the three production layouts.
2. Make a connection with `withAutomaticReconnect()`.
3. Add the handlers of the message types before `start()`. A notification that arrives with no handler is lost.
4. Start the connection.
5. On a page with no login, call the hub method that joins the tenant. In the demo, this method is `JoinTenant`. The hub then sends the tenant notifications, and the company notifications of a tenant with one company.
6. After the login, stop the anonymous connection, and make a new connection with the token. The hub joins the tenant and the company of the token. SignalR sends the user notifications to this connection.

## The token

A browser WebSocket cannot send an `Authorization` header. The SignalR client sends the token as the query `access_token` when you give `accessTokenFactory`:

```ts
new HubConnectionBuilder()
  .withUrl("/hubs/demo", { accessTokenFactory: () => token })
```

The host must read this query on the hub path only. `Sessions/DemoTokens.cs` of the demo shows the `OnMessageReceived` event of the JWT bearer handler that does this. Keep the token in memory. A token in a URL can go into the access logs of a proxy, so give it a short lifetime.

## Reconnect

A reconnect gives the connection a new connection id, and a new connection id has no groups:

- A connection with a token gets its groups again from `OnConnectedAsync` of the hub. The client does nothing.
- An anonymous connection must call `JoinTenant` again in `onreconnected`. The sample does this in `#rejoinAnonymousTenant`.

When `withAutomaticReconnect()` stops its tries, the connection closes. Then `onclose` runs, and the page must start a new connection.

## Missed notifications

SignalR keeps no message for a disconnected client. A notification that the server sends while the client is disconnected is lost for that client. The outbox records it as sent, because the hub accepted it.

If a page must show each notification, keep the notifications in a store of the host, and give the client a way to read the ones that it missed after a reconnect. The hub `NotificationHub` of `examples/WolverineExample.Consumer` shows this with the hub method `GetMissedNotifications`.

## Security

- Send only public notifications to a tenant group that anonymous pages can join.
- An anonymous page can name any tenant in `JoinTenant`. Do not give a tenant group data that only a user of that tenant can see.
- A company group and a user name need a token, because the hub takes the company and the user from the claims of the token.

## Run the sample

The sample is a module for a bundler, for example Vite. It is not part of the build of the demo. Install the client package of the same major version as ASP.NET Core:

```bash
npm install @microsoft/signalr@10
```

Start the demo with `dotnet run --project examples/SignalRDemo`. Then call `example()` from a page that the demo serves, or change `hubUrl` to the full URL of the hub and allow the origin of your page with CORS on the host.
