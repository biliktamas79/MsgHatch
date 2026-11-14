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

The consumer writes each email to its log, with the event id 9200. It needs no SMTP server and no cloud account. Its SQLite file `wolverine-example.db` is in the working folder of the app, and git ignores it. Delete the file to seed the example data again.

The unit tests of the routing and of the user tokens need no broker:

```bash
dotnet test tests/WolverineExample.Consumer.Tests.Unit
```

```bash
dotnet test tests/WolverineExample.UserSimulator.Tests.Unit
```

## Example data

At its first start, the consumer writes the example data into its SQLite file (`Startup/ConsumerScenario.cs`):

| Tenant | Company | Admin email | Users |
|---|---|---|---|
| `north` | `100` Acme | `admin@acme.example` | `1`, `2` |
| `north` | `200` Globex | `admin@globex.example` | `3`, `4` |
| `south` | `100` Initech | `admin@initech.example` | `1`, `2` |

The tenant `south` uses the same ids as the tenant `north`. The events of one tenant never reach the users of the other tenant.

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
| `UserLogin` of user `1` | user `north:1` | `admin@acme.example` |
| `CompanyArchived` of company `200` | group of company `200` | `admin@globex.example` |
| `ProjectCreated` of project `7`, owner `2` | user `north:2` | `admin@acme.example` |
| `ProjectFinished` of project `7`, owner `2` | user `north:2` | `admin@acme.example` |
| `TenantMaintenance` of tenant `north` | groups of companies `100` and `200` | `admin@acme.example`, `admin@globex.example` |

## Decisions of the example

- **The consumer is a web app.** The SignalR hub needs an HTTP server, so the consumer runs `WebApplication` on Kestrel. It maps only the hub, at `/hubs/notifications`.
- **A Wolverine handler receives the events.** `DomainEventReceiver` calls `INotificationService.AcceptAsync` with the tenant of the event and the id of the Wolverine envelope as the message id. A second delivery of the same envelope stores nothing new. The listener uses `ProcessInline()`, so the broker keeps an event until the inbox has it. The package `MsgHatch.Broker.Wolverine` is not used, because it sends outgoing broker messages, and this example has no broker subscription.
- **`TenantMaintenance` becomes one event for each company.** A notification scope holds one company or a list of users, and no scope holds a whole tenant. So the handler reads the companies of the tenant from the directory, and accepts one `CompanyMaintenance` event for each company, with the message type `TenantMaintenance`. The message id of each one comes from the envelope id and the company id, so a second delivery stores nothing new.
- **The SignalR hub knows the tenant.** The SignalR transport sends a user message to the name `{tenant}:{user}`, and a company message to the group of the company. `TenantUserIdProvider` gives each connection that name, and `NotificationHub` adds each connection to the group of the company of its user.
- **The email client writes to the log.** The library has no development email client, so `LogEmailClient` is the same log client as in `ExampleApp`.

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

A user that cannot connect, for example because the consumer is not started yet, tries again every 2 seconds. A connected user reconnects by itself after a short outage.

Another client connects to `http://localhost:5090/hubs/notifications` with the token as its access token. It gets each notification on a method with the name of the message type, for example `UserLogin`, with a payload `{ "text": "..." }`. The JWT bearer setup reads the token from the `Authorization` header only. A browser client sends the token in the query string, so it needs an `OnMessageReceived` handler that the example does not have.
