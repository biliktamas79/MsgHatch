# MsgHatch
A flexible and extensible .NET library that can generate and handle various message formats - such as emails, SMS or push notifications - based on templates.

MsgHatch turns domain events into messages, and delivers them by email, as real-time UI updates with SignalR, or through a message broker such as RabbitMQ, Amazon SQS, or Kafka with Wolverine, or Amazon SNS directly. A router decides who gets each message on which channel. The optional package `MsgHatch.Subscriptions` is a router with managed subscriptions.

**Status:** in development. The development follows the [roadmap](docs/roadmap.md) of phases. Each phase ends in a tested and working state. Phases 0 to 40 are done. The [design](docs/design.md) holds the requirements, the decisions, and the architecture. The [store guide](docs/stores.md) explains the stores. No package is released yet.

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
- One event consumer and one publisher for each channel: in one process, or in separate processes that poll the outbox. On one machine, a named pipe signal also wakes the publishers of the other processes.
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
| `MsgHatch.Domain` | The routing contract, the pipeline, the inbox, the outbox, the dispatch loops, the dispatch composites for a storage for each tenant, the state rows of the channels, and the delivery queries and commands with their rules | 1, done; 17, 32, and 36, done |
| `MsgHatch.Templates` | The template library, which knows no channel: the template engine contract, the template stores, the placeholder engine, the template queries and commands, and the preview with sample payloads | 24 and 25, done; 29 and 30, done |
| `MsgHatch.Email` | The email channel, as a thin layer on `MsgHatch.Templates` | 3, done; 22 and 24, done |
| `MsgHatch.Email.AmazonSES` | The Amazon SES email client | 4 and 20, done |
| `MsgHatch.Templates.Liquid` | The Liquid template engine, for templates that a host adds at run time | 23 and 24, done |
| `MsgHatch.Templates.AspNetCore` | HTTP endpoints for template administration | 25, done |
| `MsgHatch.Api.Contracts` | The error metadata of the HTTP APIs: the wire form of an error type, and the bodies of the registry API and of the delivery API | 26, 31, and 36, done |
| `MsgHatch.Api.Client` | The HTTP clients of the registry API and of the delivery API, behind `IRegistryQueries`, `IRegistryCommands`, `IDeliveryQueries`, and `IDeliveryCommands` | 31, 33, 35, and 36, done |
| `MsgHatch.Templates.Api.Contracts` | The bodies of the template HTTP API, with their mappings in both directions | 26, done |
| `MsgHatch.Templates.Api.Client` | The HTTP client of the template management API, behind `ITemplateQueries` and `ITemplateCommands` | 27, done |
| `MsgHatch.AdminUI` | The shared parts of the admin UIs: the list page layout, the pager, the dialog, the bottom bar of the buttons, the delete question with details, and the text of each error. The pages of the channels, with their state, history, and check, and of the message types, with the columns of other packages. The edit pages of the stored settings of a channel and of a message type, and the pause and the resume of a channel. The delivery pages of the traffic, the outbox, and the inbox, with the page of one message | 28, 30, 31, 32, 33, 35, and 37, done |
| `MsgHatch.Templates.AdminUI` | The admin pages of the templates, with an editor for each engine, a read-only view, and a live preview | 28 to 30, done |
| `MsgHatch.Templates.Liquid.AdminUI` | The Liquid editor of the template admin UI, with highlighting and the check of the server | 28, done |
| `MsgHatch.Email.Resend` | The Resend email client | 4, done |
| `MsgHatch.SignalR` | Real-time UI updates | 5, done |
| `MsgHatch.Broker` | The broker channel and the broker client contract | 6, done |
| `MsgHatch.Broker.Wolverine` | Message brokers through Wolverine | 6, done |
| `MsgHatch.Broker.AmazonSns` | Amazon SNS topics with the AWS SDK, the first direct broker client | 38, done |
| `MsgHatch.Data.FileSystem` | The JSON file stores for the inbox and the outbox | 7, done |
| `MsgHatch.Data.Sqlite` | The SQLite stores for the inbox and the outbox, on one file or on one file for each tenant | 8, done; 17, done |
| `MsgHatch.Subscriptions` | The router with subscriptions on three levels, and its management API | 9, done |
| `MsgHatch.Subscriptions.Data.FileSystem` | The JSON file subscription store | 10, done |
| `MsgHatch.Subscriptions.Data.Sqlite` | The SQLite subscription store, on one file or on one file for each tenant | 10, done; 17, done |
| `MsgHatch.Subscriptions.Caching.Hybrid` | The HybridCache backend of the subscription cache | 11, done |
| `MsgHatch.Subscriptions.Caching.FusionCache` | The FusionCache backend of the subscription cache | 11, done |
| `MsgHatch.Subscriptions.Api.Contracts` | The bodies of the subscription HTTP API, with their mappings in both directions | 26, done |
| `MsgHatch.Subscriptions.Api.Client` | The HTTP client of the subscription management API, behind `ISubscriptionQueries`, `ISubscriptionCommands`, and `IRoutingVerifier` | 27, done |
| `MsgHatch.Subscriptions.AdminUI` | The admin pages of the subscriptions and of the routing verification, and the subscription counts of each message type | 28, 30, 31, and 32, done |
| `MsgHatch.Subscriptions.AspNetCore` | HTTP endpoints for subscription management | 12, done |
| `MsgHatch.AspNetCore` | HTTP endpoints for delivery management and registry administration | 15, done; 16, 31, and 36, done |
| `MsgHatch.Data.EFCore` | The EF Core stores, with SQL Server as the first provider | 18, done |
| `MsgHatch.Subscriptions.Data.EFCore` | The EF Core subscription store | 18, done |
| `MsgHatch.Data.MongoDb` | The MongoDB stores, and the change stream dispatch signal between the hosts of one database | 19 and 39, done |
| `MsgHatch.Subscriptions.Data.MongoDb` | The MongoDB subscription store | 19, done |
| `MsgHatch.Dispatch.NamedPipes` | The dispatch signal between the processes of one OS user on one machine, with named pipes | 21, done |
| `MsgHatch.Dispatch.RabbitMq` | The dispatch signal between the hosts of one system on different machines, through a fanout exchange of RabbitMQ | 40, done |

Phase 14 added the routing verification to `MsgHatch.Subscriptions`. Phase 15 added the delivery management: the status `Cancelled`, the query, count, retry, cancel, and purge members of the stores, `IDeliveryManager`, and its HTTP endpoints in `MsgHatch.AspNetCore`. Phase 16 added the registry administration: the stored registry settings of the channels and the message types on top of the code, `IRegistryWriter`, the registry settings stores of the in-memory, file, and SQLite packages, the skip of a disabled message type or channel in the pipeline, and its HTTP endpoints in `MsgHatch.AspNetCore`. Phase 17 added a store for each tenant: `ITenantSource`, the dispatch composites with `UseStorageForEachTenant`, `UseSqliteTenantStores` with one SQLite file for each tenant, and `UseSqliteTenantSubscriptionStore`. Phase 18 added the EF Core stores with SQL Server as the first provider: `UseEntityFrameworkStores` for the inbox, the outbox, and the registry settings, and `UseEntityFrameworkSubscriptionStore`. The host creates their tables with its own EF Core migrations. Phase 19 added the MongoDB stores: `UseMongoDbStores` for the inbox, the outbox, and the registry settings, and `UseMongoDbSubscriptionStore`. They keep each message as one BSON document with its publish tries, and create their indexes themselves. Phase 20 ran the Amazon SES integration tests against a real SES account. They passed with no change of the client. Phase 21 fixed the fallback wait of the dispatch loop: while the dispatch signal does not work, the loop now obeys a next try time that a worker gives. It also added `MsgHatch.Dispatch.NamedPipes` with `UseNamedPipeDispatchSignal`, which wakes the loops of the other processes of one OS user on one machine. Phase 22 changed the file store and the embedded store: they read the template of a company from the folder of the company, for example `42/A.hu.subject.txt`. Phase 22 also built a Razor engine, and then removed it, because a host must add templates at run time with no build. Phase 23 added `MsgHatch.Email.Liquid`: with `AsLiquidEmail`, a Liquid template in the template store renders the subject, the html, and the text, with loops and conditions. So a host adds a template, a language, or a company template at run time, as a file, with no build. Phase 24 moved the templates into the separate library `MsgHatch.Templates`, which knows no channel: a template is one text, and a replaceable engine renders it. The email channel is a thin layer on it, and the Liquid engine is now `MsgHatch.Templates.Liquid`. Phase 25 added the template administration: `ITemplateWriter` on the file template store, a check of each written text by the engine of its payload type, `ITemplateManager`, and its HTTP endpoints in `MsgHatch.Templates.AspNetCore`. So an operator changes an email text over HTTP, and a text with an error gives 422 and never reaches a send. Phase 26 split each management API into a query interface and a command interface, for example `ITemplateQueries` and `ITemplateCommands` in place of `ITemplateManager`. A host selects the implementation of each interface one by one, for example an HTTP client for the reads. The phase also added the declared parts and the check of a draft of a template, the stateless rules that run first, the identity of an error type by its code, the error contract of the HTTP APIs with the member `errorType`, the contracts packages of the HTTP bodies, a host with no role, and the AOT setting of the packages of the API boundary. Phase 27 added the HTTP clients of the management APIs: `UseTemplateApiClient` and `UseSubscriptionApiClient` select them behind the same interfaces, so a host, for example an admin UI with no role, calls the API of another host and gets the same results and the same errors as in process. A source generator of `MsgHatch.SourceGeneration` writes their HTTP calls, with no reflection and no exception for an error status. Phase 28 added the admin UIs as Razor class libraries: the list and edit pages of the subscriptions and the templates, the routing verification, and a Liquid editor with highlighting and the check of the server. A host adds them in its setup, selects the HTTP clients or the in-process implementations, and adds its own authorization. Phase 29 added the template preview: a host gives each template registration a sample payload with `WithTemplateSample`, and the template queries render a draft with it. The template list has the buttons View and Edit, and the view page and the edit page show the preview next to the text, an html part in a sandboxed frame that runs no script. Phase 30 gave the admin pages one layout: a list scrolls in its own area, a new item opens in a dialog from a button above the list, each dialog and page has Cancel or Back on the bottom left and its main action on the bottom right, and the actions of an edit page stay on screen. A delete, also from a row of a list, shows the details of the item first. The filters of a list are in a framed bar, the example host has a dark mode, and the store of a template renders the text of its version for the user. Phase 31 added the registry queries: `IRegistryQueries` gives the channels and the message types with the revision of each stored entry, in process or over HTTP with `UseRegistryApiClient` of the new package `MsgHatch.Api.Client`. The registry endpoints write their errors with the member `errorType`. The core admin UI has read-only pages of the channels, with the message types of each channel, and of the message types. The subscription page of the message types is now "Applied subscriptions". Phase 32 keeps the state of each channel as insert-only rows with the duration of the previous state, and shows the state and the history of 30 days on the channel pages. The message types page shows the subscription counts of each owner level, the page "Applied subscriptions" goes away, and the admin menu has the order Channels, Message types, Subscriptions, Templates, and Verification. Phase 33 adds the channel check: `IRegistryCommands.CheckChannelAsync` runs the health check of the transport of a channel and records only a new state, in process or over HTTP with `UseRegistryApiClient`, and each row of the channel list gets the button Check. Phase 34 removes the avoidable allocations of the hot path: the closures, the copies of the routes, and the boxed keys of the event path, the boxed outcome tags and the lock of the batch of a send, the owner part strings of the subscription cache keys, and the scratch lists of the subscription resolution. Section 4.11 of the design records the measured budget. Phase 35 adds the registry commands: `IRegistryCommands` saves and resets the stored settings of a channel and of a message type, in process or over HTTP with `UseRegistryApiClient`, and the queries give the stored entry. The core admin UI gets the edit pages of a channel and of a message type, with the button Edit on the channel and message type pages. In the `Development` environment, the example app writes an example history of the channel states, so that the History dialog has data. Phase 36 adds the delivery API: `IDeliveryQueries` and `IDeliveryCommands` replace `IDeliveryManager`, in process or over HTTP with `UseDeliveryApiClient`, and give the content of a message and its route trace as JSON text. The public `DeliveryRules` run before a store call and before an HTTP request, the delivery bodies are in `MsgHatch.Api.Contracts`, the delivery endpoints write their errors with the member `errorType`, and the new route `DELETE /delivery/test-data` deletes the test data. The generated clients write lists and times as query values. Phase 37 adds the delivery pages to the core admin UI: the traffic with the delete of the test data and the purge, the outbox and the inbox with the filters and the pages of the server, "Retry filtered" and "Cancel filtered", and the page of one message with its tries, its trace, and its content on request. The channel pages show the pending messages, and pause and resume a channel. The example admin host shows Traffic, Outbox, and Inbox in its menu. Phase 38 adds `MsgHatch.Broker.AmazonSns`: `UseAmazonSnsBroker` publishes each broker message to the SNS topic of its registration, or to a default topic, with the payload JSON as the message and each header as a message attribute. A message with more than 10 headers, or with a header that SNS does not accept, fails before the call. A missing topic, a missing permission, and an error of the KMS key keep the messages pending with a Critical log, and the channel check reads the attributes of each topic. The broker transport now gives each client the content type and the JSON type information of the payload. Phase 39 adds `UseMongoDbDispatchSignal` to `MsgHatch.Data.MongoDb`: one change stream on the database of the MongoDB stores wakes the dispatch loops of each host when a message arrives, also on another machine. A notify, for example of a retry or a resume, inserts a small document into the collection `msghatch_signals`, which the watch also sees. The change stream needs a replica set, and a loop polls while the watch does not work. Phase 40 adds `MsgHatch.Dispatch.RabbitMq`: `UseRabbitMqDispatchSignal` wakes the dispatch loops of each host of a system through a fanout exchange of RabbitMQ, with each store. A notify publishes the topic with no persistence, and each process reads its own exclusive queue. A loop polls while the signal has no connection, so the signal requires `EnablePolling` true.

## Example app
`examples/ExampleApp` runs the subscription example of the design end to end, with no broker server, no SMTP server, no cloud account, and no network. It writes each email to the log, sends the broker messages to a local Wolverine queue, keeps all data in one SQLite file, reads its email templates from the folder `Templates` of its build output, and maps the subscription endpoints and the delivery endpoints under `/api/delivery`. A caller signs in with a local JWT from `dotnet user-jwts` (D158). The delivery endpoints (D165), the registry endpoints (D170), and the template endpoints (D225) each need a role of their own.

The setup, the tokens, and the start of the app are in [examples/ExampleApp/README.md](examples/ExampleApp/README.md).

## Admin UI example
`examples/AdminUiExample` is an Interactive Server host of the admin UIs. It calls the subscription and template APIs of `ExampleApp` through the HTTP clients, so `ExampleApp` runs first. It sends one token from its user secrets for each user, and listens only on `http://localhost:5090`.

The steps to run it are in the section [Start the admin UI](examples/ExampleApp/README.md#start-the-admin-ui) of the README of the example app.

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
