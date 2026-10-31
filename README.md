# MsgHatch
A flexible and extensible .NET library that can generate and handle various message formats - such as emails, SMS or push notifications - based on templates.

MsgHatch turns domain events into messages, and delivers them by email, as real-time UI updates with SignalR, or through a message broker such as RabbitMQ, Amazon SQS, or Kafka with Wolverine. A router decides who gets each message on which channel. The optional package `MsgHatch.Subscriptions` is a router with managed subscriptions.

**Status:** planning. The development follows the [roadmap](docs/roadmap.md) of phases. Each phase ends in a tested and working state. The [design](docs/design.md) holds the requirements, the decisions, and the architecture. No package is released yet.

## Message flow
1. **Trigger.** A domain event arrives. Its type is the message type. An event from a source that is not a resilient broker goes into the inbox first.
2. **Route.** A router gives the routes of the event: one route for each target and channel, with the settings, the language, and the address. A consumer can write a simple router, or use the router of `MsgHatch.Subscriptions`. That router resolves subscriptions on three levels: for each channel, a user subscription overrides the company default, and the company default overrides the global default. A mandatory subscription, or a subscription for the company or for named users, is fixed: no lower level overrides it. For a company scope, a UI notification goes one time to the SignalR group of the company. Each route records where each decision came from.
3. **Select templates.** The email composer selects an email template for each message type, language, and scope.
4. **Load data.** The composer of each channel loads the data that the messages need, through a payload loader.
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

## Planned packages
| Package | Content |
|---|---|
| `MsgHatch.Domain` | The routing contract, the pipeline, the inbox, the outbox, and the dispatch loops |
| `MsgHatch.Email` | The email channel, the template engine interface, and the placeholder engine |
| `MsgHatch.Email.AmazonSES` | The Amazon SES email client |
| `MsgHatch.SignalR` | Real-time UI updates |
| `MsgHatch.Broker` | The broker channel and the broker client contract |
| `MsgHatch.Broker.Wolverine` | Message brokers through Wolverine |
| `MsgHatch.Data.FileSystem` | The JSON file stores for the inbox and the outbox |
| `MsgHatch.Data.Sqlite` | The SQLite stores for the inbox and the outbox |
| `MsgHatch.Data.EntityFrameworkCore` | The EF Core stores, with SQL Server as the first provider |
| `MsgHatch.Data.MongoDb` | The MongoDB stores |
| `MsgHatch.AspNetCore` | HTTP endpoints for delivery management and registry administration |
| `MsgHatch.Subscriptions` | The router with subscriptions on three levels, its management API, and the routing verification |
| `MsgHatch.Subscriptions.Data.FileSystem` | The JSON file subscription store |
| `MsgHatch.Subscriptions.Data.Sqlite` | The SQLite subscription store |
| `MsgHatch.Subscriptions.Data.EntityFrameworkCore` | The EF Core subscription store |
| `MsgHatch.Subscriptions.Data.MongoDb` | The MongoDB subscription store |
| `MsgHatch.Subscriptions.Caching.Hybrid` | The HybridCache backend of the subscription cache |
| `MsgHatch.Subscriptions.Caching.FusionCache` | The FusionCache backend of the subscription cache |
| `MsgHatch.Subscriptions.AspNetCore` | HTTP endpoints for subscription management |

## License
Apache License 2.0. See [LICENSE](LICENSE).
