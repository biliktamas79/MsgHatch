# MsgHatch
A flexible and extensible .NET library that can generate and handle various message formats - such as emails, SMS or push notifications - based on templates.

MsgHatch turns domain events into messages, and delivers them by email, as real-time UI updates with SignalR, or through a message broker such as RabbitMQ, Amazon SQS, or Kafka with Wolverine. A router decides who gets each message on which channel. The optional package `MsgHatch.Subscriptions` is a router with managed subscriptions.

**Status:** in development. The development follows the [roadmap](docs/roadmap.md) of phases. Each phase ends in a tested and working state. Phases 0 to 15 are done. The [design](docs/design.md) holds the requirements, the decisions, and the architecture. The [store guide](docs/stores.md) explains the stores. No package is released yet.

## Message flow
1. **Trigger.** A domain event arrives. Its type is the message type. An event from a source that is not a resilient broker goes into the inbox first.
2. **Route.** A router gives the routes of the event: one route for each target and channel, with the settings, the language, and the address. A consumer can write a simple router, or use the router of `MsgHatch.Subscriptions`. That router resolves subscriptions on three levels: for each channel, a user subscription overrides the company default, and the company default overrides the global default. A mandatory subscription, or a subscription for the company or for named users, is fixed: no lower level overrides it. For a company scope, a UI notification goes one time to the SignalR group of the company. Each route records where each decision came from.
3. **Select templates.** The email composer selects an email template for each message type, language, and scope.
4. **Load data.** A payload loader loads the data that the messages need. It is registered one time for each payload type of a message type, apart from the channels, and the composer of each channel of that payload type calls it.
5. **Generate.** The composer generates each message.
6. **Store.** Each complete message goes into the outbox. The event consumer, which routes and composes, does not send.
7. **Publish.** One publisher for each channel sends the messages of its channel, retries a failed send, and records each try. The event consumer and the publishers run in one process with no poll of the outbox, or each publisher runs in its own process and can stop and deploy without the others.

## Core requirements
- A simple routing contract: the event goes in, and the recipients and channels come out. The composer of each channel builds its messages.
- Flexible message templating, with exchangeable template engines.
- A template repository that does not depend on one storage technology.
- Zero or minimal memory allocation, and high performance.
- Type safety: typed domain events, template payloads, and channel envelopes.
- Resilient delivery with an inbox, an outbox, retries for each message type, a down state of a channel for all its publishers during a provider outage, and a full history of each publish try.
- An API for a UI of the traffic and the history: message lists with filters, publish tries, traffic counts, and channel states. It retries and cancels stuck messages, and pauses and resumes channels.
- One event consumer and one publisher for each channel: in one process, or in separate processes that poll the outbox or get a signal between processes.
- Storage of your choice: in memory, JSON files, SQLite, SQL Server through EF Core, or MongoDB, with versioned contracts. The core works with no database.
- Several tenants in one system, with ids that are unique inside each tenant, and optionally one storage for each tenant. A service of a tenant is a keyed DI service, so the code of one tenant and of many tenants looks the same.
- The optional package `MsgHatch.Subscriptions`: subscriptions on three levels (default, company, and user), with a management API, optional HTTP endpoints, and an optional subscription cache. The [subscription domain](docs/subscription-domain.md) describes the model.
- User ids, company ids, and tenant ids as strings, so that each application keeps its own id type.
- Channels and message types from the code, with optional stored settings for an administrator: names, descriptions, and enabled flags.
- Test messages that stay separate from real data, previews that store and send nothing, and a full routing verification with previews.
- A purge of finished messages after a retention that the host selects.
- Errors as values (`ErrorOr<T>`), async APIs with cancellation, and `TimeProvider` for all times.
- Traces and metrics for OpenTelemetry through the .NET APIs, with no extra package. A send in a publisher process links to the trace of its event.

## Packages
| Package | Content | Phase |
|---|---|---|
| `MsgHatch.Domain` | The routing contract, the pipeline, the inbox, the outbox, and the dispatch loops | 1, done |
| `MsgHatch.Email` | The email channel, the template engine interface, and the placeholder engine | 3, done |
| `MsgHatch.Email.AmazonSES` | The Amazon SES email client | 4, done |
| `MsgHatch.Email.Resend` | The Resend email client | 4, done |
| `MsgHatch.SignalR` | Real-time UI updates | 5, done |
| `MsgHatch.Broker` | The broker channel and the broker client contract | 6, done |
| `MsgHatch.Broker.Wolverine` | Message brokers through Wolverine | 6, done |
| `MsgHatch.Data.FileSystem` | The JSON file stores for the inbox and the outbox | 7, done |
| `MsgHatch.Data.Sqlite` | The SQLite stores for the inbox and the outbox | 8, done |
| `MsgHatch.Subscriptions` | The router with subscriptions on three levels, and its management API | 9, done |
| `MsgHatch.Subscriptions.Data.FileSystem` | The JSON file subscription store | 10, done |
| `MsgHatch.Subscriptions.Data.Sqlite` | The SQLite subscription store | 10, done |
| `MsgHatch.Subscriptions.Caching.Hybrid` | The HybridCache backend of the subscription cache | 11, done |
| `MsgHatch.Subscriptions.Caching.FusionCache` | The FusionCache backend of the subscription cache | 11, done |
| `MsgHatch.Subscriptions.AspNetCore` | HTTP endpoints for subscription management | 12, done |
| `MsgHatch.AspNetCore` | HTTP endpoints for delivery management and registry administration | 15, done; 16, planned |
| `MsgHatch.Data.EntityFrameworkCore` | The EF Core stores, with SQL Server as the first provider | 18, planned |
| `MsgHatch.Subscriptions.Data.EntityFrameworkCore` | The EF Core subscription store | 18, planned |
| `MsgHatch.Data.MongoDb` | The MongoDB stores | 19, planned |
| `MsgHatch.Subscriptions.Data.MongoDb` | The MongoDB subscription store | 19, planned |

Phase 14 added the routing verification to `MsgHatch.Subscriptions`. Phase 15 added the delivery management: the status `Cancelled`, the query, count, retry, cancel, and purge members of the stores, `IDeliveryManager`, and its HTTP endpoints in `MsgHatch.AspNetCore`.

## Example app
`examples/ExampleApp` runs the subscription example of the design end to end, with no broker server, no SMTP server, no cloud account, and no network. It writes each email to the log, sends the broker messages to a local Wolverine queue, keeps all data in one SQLite file, and maps the subscription endpoints and the delivery endpoints under `/api/delivery`. A caller signs in with a local JWT from `dotnet user-jwts`:

```bash
dotnet user-jwts create --project examples/ExampleApp --name 1
```

The delivery endpoints need a token with roles: `DeliveryAdmin` for the overview and the actions, and `DeliveryContent` for the content of the messages (D165):

```bash
dotnet user-jwts create --project examples/ExampleApp --name 1 --role DeliveryAdmin --role DeliveryContent
```

The registry endpoints under `/api/registry` change the stored registry settings, and need a token with the role `RegistryAdmin` (D170):

```bash
dotnet user-jwts create --project examples/ExampleApp --name 1 --role RegistryAdmin
```

```bash
dotnet run --project examples/ExampleApp
```

Then call `GET http://localhost:5080/api/subscriptions/me` with the token as a bearer token. The response holds the subscriptions of the user `"1"`.

## Commands
Build, and run the unit tests of one project:

```bash
dotnet build MsgHatch.slnx
```

```bash
dotnet test tests/MsgHatch.Domain.Tests.Unit
```

The coverage report of all unit tests, with PowerShell 7. It fails when the line coverage is less than 90 %:

```bash
pwsh ./coverage.ps1
```

The benchmarks, in the Release configuration, one project at a time:

```bash
dotnet run -c Release --project tests/MsgHatch.Domain.Tests.Performance
```

```bash
dotnet run -c Release --project tests/MsgHatch.Email.Tests.Performance
```

```bash
dotnet run -c Release --project tests/MsgHatch.Subscriptions.Tests.Performance
```

`AGENTS.md` lists the integration tests and the local settings that they need.

## License
Apache License 2.0. See [LICENSE](LICENSE).
