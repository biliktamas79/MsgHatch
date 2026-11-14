# Wolverine example: publisher, consumer, and user simulator

Three console apps show the full flow of MsgHatch behind a message broker:

- `WolverineExample.Publisher` sends five domain events to the RabbitMQ queue `msghatch-example-events` through Wolverine, and then stops.
- `WolverineExample.Consumer` listens to that queue through Wolverine. It gives each event to the MsgHatch inbox. The subscription router then sends SignalR notifications and emails to the users and the company administrators that the subscriptions name.
- `WolverineExample.UserSimulator` connects one SignalR client for each simulated user to the hub of the consumer, and logs each notification that a user gets.
- `WolverineExample.Contracts` holds the five event records, their names, and the RabbitMQ options. The apps reference it.

The examples are outside the phases of `docs/roadmap.md`.

## Run

The apps need a RabbitMQ broker. A local container is enough:

```bash
docker run -d --name dev-rabbit -p 5672:5672 -p 15672:15672 rabbitmq:4.3-management
```

The URI of the broker comes from the setting `RabbitMq:Uri` in `appsettings.json` of each app. The default is `amqp://localhost:5672`. To use another broker, set the environment variable `RabbitMq__Uri`, or a user secret.

Start the consumer first, and leave it open:

```bash
dotnet run --project examples/WolverineExample.Consumer
```

To see the SignalR notifications, start the user simulator in a second terminal, and leave it open. Its first start needs one signing key of `dotnet user-jwts`, so make one token one time before (see [Simulate the users](#simulate-the-users)):

```bash
dotnet run --project examples/WolverineExample.UserSimulator
```

Then send the five sample events from a third terminal:

```bash
dotnet run --project examples/WolverineExample.Publisher
```

The publisher declares the queue too. So you can also start the publisher first. The broker then keeps the events until the consumer starts. Wolverine declares two durable queues on the broker: `msghatch-example-events`, and `wolverine-dead-letter-queue` for the events that the consumer cannot accept after its retries.

The consumer writes each email to its log, with the event id 9200. It needs no SMTP server and no cloud account. Its SQLite file `wolverine-example.db` is in the working folder of the app, and git ignores it. Delete the file to seed the example data again, for example after a change of the scenario or of the directory tables.

The unit tests of the routing and of the user tokens need no broker:

```bash
dotnet test tests/WolverineExample.Consumer.Tests.Unit
```

```bash
dotnet test tests/WolverineExample.UserSimulator.Tests.Unit
```

## Example data

At its first start, the consumer writes the example data into its SQLite file (`Startup/ConsumerScenario.cs`):

| Tenant | Company | Admin email | Admin language | Users |
|---|---|---|---|---|
| `north` | `100` Acme | `admin@acme.example` | `hu` | `1`, `2` |
| `north` | `200` Globex | `admin@globex.example` | `en` | `3`, `4` |
| `south` | `100` Initech | `admin@initech.example` | `en` | `1`, `2` |

The tenant `south` uses the same ids as the tenant `north`. The events of one tenant never reach the users of the other tenant.

The admin language is the default language of the company. An email to a company has no language in its subscription, so the router takes the default language of the company (`docs/subscription-domain.md`, section 2.2). The email template store then selects the template of that language, for example `UserLogin.hu.subject.txt` for Acme and `UserLogin.en.subject.txt` for Globex. The folder `Templates` has an English and a Hungarian template for each message type. The placeholder `{{Text}}` stays English, because the consumer makes that text in code. SignalR sends its payload as data, with no template, so the language changes no SignalR notification.

Each tenant has the same 10 default subscriptions. Each message type has 2:

- A SignalR subscription for the scope users.
- An email subscription for the company (`SubscriptionRecipients.Company`). The company address of the email channel is the email address of the company administrator.

## Routing

| Event | Scope | SignalR | Email |
|---|---|---|---|
| `UserLogin` | the user | the user | the admin of the company of the user |
| `CompanyArchived` | the company | the group of the company, so all its connected users | the admin of the company |
| `ProjectCreated` | the owner of the project | the owner | the admin of the company of the owner |
| `ProjectFinished` | the owner of the project | the owner | the admin of the company of the owner |
| `TenantMaintenance` | each company of the tenant | the group of each company | the admin of each company |

A user scope reaches the company of each user in it, because an email subscription for the company sends for each company that an event concerns (`docs/subscription-domain.md`, section 2.1).

The sample events of the publisher give these deliveries:

| Sample event | SignalR | Email |
|---|---|---|
| `UserLogin` of user `1` | user `north:1` | `admin@acme.example`, Hungarian |
| `CompanyArchived` of company `200` | group of company `200` | `admin@globex.example`, English |
| `ProjectCreated` of project `7`, owner `2` | user `north:2` | `admin@acme.example`, Hungarian |
| `ProjectFinished` of project `7`, owner `2` | user `north:2` | `admin@acme.example`, Hungarian |
| `TenantMaintenance` of tenant `north` | groups of companies `100` and `200` | `admin@acme.example`, Hungarian, and `admin@globex.example`, English |

## Decisions of the example

- **The consumer is a web app.** The SignalR hub needs an HTTP server, so the consumer runs `WebApplication` on Kestrel. It maps only the hub, at `/hubs/notifications`.
- **A Wolverine handler receives the events.** `DomainEventReceiver` calls `INotificationService.AcceptAsync` with the tenant of the event and the id of the Wolverine envelope as the message id. A second delivery of the same envelope stores nothing new. The listener uses `ProcessInline()`, so the broker keeps an event until the inbox has it. The package `MsgHatch.Broker.Wolverine` is not used, because it sends outgoing broker messages, and this example has no broker subscription.
- **`TenantMaintenance` becomes one event for each company.** A notification scope holds one company or a list of users, and no scope holds a whole tenant. So the handler reads the companies of the tenant from the directory, and accepts one `CompanyMaintenance` event for each company, with the message type `TenantMaintenance`. The message id of each one comes from the envelope id and the company id, so a second delivery stores nothing new.
- **The SignalR hub knows the tenant.** The SignalR transport sends a user message to the name `{tenant}:{user}`, and a company message to the group of the company. `TenantUserIdProvider` gives each connection that name, and `NotificationHub` adds each connection to the group of the company of its user. The tenant and the user come from the token. The company comes from the directory, because the router reads the company from the directory too, so the SignalR groups and the emails always agree.
- **The hub closes a connection when its token expires.** The hub checks a token only when a client connects, so without this rule a connection could keep the groups of an expired token. With `CloseOnAuthenticationExpiration`, the client must reconnect with a new token, and the hub then reads the company of the user again.
- **The email client writes to the log.** The library has no development email client, so `LogEmailClient` is the same log client as in `ExampleApp`.
- **A client catches up the notifications that it missed.** SignalR delivers a message only to the connections that exist when the hub sends it, and the outbox marks the message `Done` also when no connection got it. So a client calls the hub method `GetMissedNotifications` after each connect and reconnect, with the id of the last notification that it got. Each live notification carries that id, which is the id of its outgoing message. `NotificationHistory` reads the SignalR messages to the user and to the company of the user from the outbox, through `IDeliveryManager`, and leaves out the cancelled ones. It gives those that the outbox stored from 5 minutes before the last notification, oldest first. The outgoing ids are UUID version 5 (design D11), which have no order, so the hub reads the time of the last notification from the outbox. A SignalR message can be sent after a newer one, but at most 5 minutes after its event (its default expiry, design D94), so the 5 minutes cover such a message. With no id, or with an id that is not a SignalR notification of the caller, the hub gives the notifications of the last 5 minutes. The client drops each id that it already has. A web UI keeps the last id in its local storage.

## Simulate the users

The hub needs a JWT with the user id and the claim `tenant`. Make one for user `1` of the tenant `north`:

```bash
dotnet user-jwts create --project examples/WolverineExample.Consumer --name 1 --claim tenant=north
```

The command writes the signing key into the user secrets of the consumer. `appsettings.Development.json` holds the issuer and the audience of the tokens, so the consumer accepts them in the `Development` environment of its launch profile.

The user simulator has the same `UserSecretsId` as the consumer. So it reads that signing key, and signs a token for each simulated user itself, with the claims `sub` and `tenant`. Only a development setup has this key. A real client gets its token from an identity provider.

The simulated users are in the section `UserSimulator:Users` of `appsettings.json` of the simulator: the users `1` to `4` of `north`, and `1` and `2` of `south`. The client does not choose its groups. The hub of the consumer adds each connection to the user name `{tenant}:{user}` and to the group of the company of the user. So each simulated user gets:

- Its user messages: `UserLogin`, `ProjectCreated`, and `ProjectFinished`.
- The messages of its company: `CompanyArchived`.
- The messages of its tenant: `TenantMaintenance`, which the consumer sends to the group of each company of the tenant.

The simulator logs each notification with the event id 9300, for example `User north:1 got UserLogin: User 1 signed in at ...`. The sample events of the publisher reach these users:

| Simulated user | Notifications |
|---|---|
| `north:1` | `UserLogin`, `TenantMaintenance` |
| `north:2` | `ProjectCreated`, `ProjectFinished`, `TenantMaintenance` |
| `north:3` | `CompanyArchived`, `TenantMaintenance` |
| `north:4` | `CompanyArchived`, `TenantMaintenance` |
| `south:1`, `south:2` | none |

A user that cannot connect, or that lost its connection, tries again every 2 seconds with no end. So the simulator can start before the consumer, and survives a restart of the consumer. The default retry policy of SignalR stops after 4 tries in about 42 seconds. After each connect, a user catches up the notifications that it missed (event id 9305), for example `User north:1 caught up UserLogin: ...`, and logs each notification only once. A connected user also reconnects when the hub closes its connection because its token expired. The simulator signs a new token for each connect. The setting `UserSimulator:TokenLifetime` (default 1 hour) sets how long a token is valid. To see the reconnects (event id 9304) after one minute, start the simulator with short tokens:

```bash
dotnet run --project examples/WolverineExample.UserSimulator -- --UserSimulator:TokenLifetime=00:01:00
```

Another client connects to `http://localhost:5090/hubs/notifications` with the token as its access token. It gets each notification on a method with the name of the message type, for example `UserLogin`, with a payload `{ "text": "..." }`. The JWT bearer setup reads the token from the `Authorization` header only. A browser client sends the token in the query string, so it needs an `OnMessageReceived` handler that the example does not have.
