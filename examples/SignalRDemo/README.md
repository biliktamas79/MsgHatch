# SignalR demo: tenants, companies, and users

The SignalR demo shows on one screen how a web page finds its tenant, its company, and its user, which SignalR groups it joins at each step, and which open session gets each notification (D316 of the [design](../../docs/design.md)).

The app needs no database, no broker, and no network. MsgHatch keeps its messages in the in-memory stores. A router of the host sends each notification through the outbox to the SignalR transport, as in a real host.

## Run

```bash
dotnet run --project examples/SignalRDemo
```

Open `http://localhost:5091`. The app listens only on that address. A browser resolves each name under `localhost`, for example `acme.localhost`, to the local machine with no hosts file, so the panes and the real tabs can use these names.

The app signs its tokens with a random key that it makes at each start. A restart makes each token invalid, so log in again after a restart.

## The demo data

`demo-data.json` has the tenants, the companies, and the users. A user has an account in each company of its list.

| Tenant | Companies | Users and their accounts |
|---|---|---|
| `eu` | `acme`, `initech` | `anna`: `acme` and `initech`. `bela`: `acme`. `cecil`: `initech`. |
| `us` | `globex` | `anna`: `globex`. `dora`: `globex`. |
| `testing` | `sandbox` | `tess`: `sandbox`. |

A user id is unique only inside its tenant, so the user `anna` of `eu` and the user `anna` of `us` are two users. The host `{label}.localhost` names a tenant or a company, so the app does not start when a label names more than one tenant or company.

## The flow of a page

1. The page maps its domain to a tenant, a company, or both:
   - `{company}.localhost`, for example `acme.localhost`, gives the company and its tenant.
   - `{tenant}.localhost`, for example `eu.localhost`, gives only the tenant.
   - `localhost` gives nothing. The page then asks the user to choose a tenant.
2. The page connects to the hub with no token, and calls `JoinTenant`. The connection joins the group of the tenant. A tenant with one company, for example `us`, also gives the company, so the connection also joins the group of that company.
3. The user logs in. The demo login has no password. A user with accounts in more than one company of the tenant chooses one account. On a company domain, the account is the company of the domain, and a user with no account in that company gets `Rejected`.
4. The page connects again with the token. The connection joins the groups of the tenant and of the company of the token, and the hub gives it the user name `{TenantId}:{UserId}`.

## A session pane

"Add session" adds a pane. Each pane has its own SignalR connection and keeps its token in memory, so two panes can be two users of one tenant or of two tenants. Type a host into the host field, and click Open to run the flow again with that host.

| Field | Meaning |
|---|---|
| Domain | What the host gives: a company and its tenant, a tenant, or nothing. |
| Tenant | The tenant of the connection. |
| Company | The company whose group the connection joined, or `-`. |
| User | The user and its SignalR user name, or `anonymous`. |
| Connection | The state of the connection, for example `Connected` or `Reconnecting`. |
| Groups | The SignalR groups of the connection. |

Below the fields, the pane shows the step of the flow: the tenant choice, the login, the account choice, or Log out. The log of the pane shows each notification that it got, with the time, the target level, and the payload. The pane flashes when it gets a notification.

## The publish panel and the sent log

The publish panel sends one notification to a target level: a tenant, a company of the tenant, or a user of the tenant. "Send a random notification" sends a random notification at the interval. Each notification goes through the publish endpoint, the router of the host, and the outbox, not directly through the hub.

The dashboard sends the state of each pane with the notification. The server gives the expected panes: each connected pane of the tenant, and of the company or of the user of the target. The sent log shows each pane of a notification as a chip:

- Green: the pane got the notification, and the dashboard expected it.
- Yellow: the dashboard expects the notification, and waits for it for 2 seconds.
- Red: a miss, or an extra hit. A miss is an expected pane that did not get the notification in 2 seconds. An extra hit is a pane that got a notification that the dashboard did not expect.

## One session in a real tab

The page `/session` shows one pane alone, for a demo in real browser tabs. The host comes from `?host=`, or from the address of the tab. For example, open `http://acme.localhost:5091/session` and `http://globex.localhost:5091/session` in two tabs.

## The code panel

"The code of each step" at the bottom of the dashboard shows the JavaScript of each step of a page, and the C# of the hub, of the `IUserIdProvider`, and of the router. Each block is a region of a source file that the app embeds, so it is the code that runs.

## Layouts in production

The same flow supports these layouts:

- A subdomain for each company, for example `acme.example.com`. The domain gives the company and its tenant, so the login only checks that the user has an account in that company.
- A subdomain for each tenant, for example `eu.example.com`. The domain gives the tenant, and the login gives the company. A tenant with one company gives the company before the login.
- One domain with a landing page. The landing page has drop-downs of the tenant and, when necessary, of the company before the login page. The choice replaces the domain of the other layouts.

The demo makes these choices only for the demo:

- The login has no password. A real app gets the token from its identity provider, with the tenant and the company as claims.
- An anonymous page joins the group of each tenant that it names. Send only public notifications to a tenant group that anonymous pages can join.

## The SignalR client

`wwwroot/lib/signalr.min.js` is the file `dist/browser/signalr.min.js` of the npm package `@microsoft/signalr` 10.0.11, the last version for ASP.NET Core 10, with no change. Its source is `https://cdn.jsdelivr.net/npm/@microsoft/signalr@10.0.11/dist/browser/signalr.min.js`, and its SHA-256 is `l+m5fmQqcuWkcJFxR6K/efhsrYKaXGeGrbEGFNJIu5U=` (Base64). The package has the MIT license, and Microsoft publishes it from the repository `dotnet/aspnetcore`. The repository has no source map of the file.

## Tests

```bash
dotnet test tests/SignalRDemo.Tests.Unit
```

The tests run the app on a test server with SignalR clients of two tenants. They need no browser.
