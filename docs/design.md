# MsgHatch design

Status: approved in parts. Section 3 shows which decisions are confirmed. The plan uses the recommended option of each other decision until it is changed.

The plan has two documents. This design holds the requirements, terms, decisions, architecture, conventions, risks, and the scope. The roadmap `docs/roadmap.md` holds the order of the phases, and each phase with its items, tests, and commits. In this document, "phase N" refers to a phase of the roadmap.

The plan is complete for an empty repository. The repository starts with `README.md`, `LICENSE`, `.gitignore`, `AGENTS.md`, this file, `docs/roadmap.md`, and `docs/subscription-domain.md`. `docs/subscription-domain.md` describes the subscription domain with its use cases and its numbered examples, which the tests use as their rows.

## 1. Requirements

MsgHatch is a .NET library that turns domain events into messages from templates, and delivers them on channels. A router gives the recipients and the channels of each event. The optional package `MsgHatch.Subscriptions` is a router with managed subscriptions. The channels:
- Email.
- Real-time UI updates with SignalR.
- Message brokers, for example RabbitMQ, Amazon SQS, or Kafka, through a broker library such as Wolverine, with generic message types.

Requirements:
1. The library is well organized, type safe, and generic. It is easy to extend with new transports, channels, templates, and template engines.
2. Email templates come from embedded resources, external files, or other storage, behind an interface. Each template has a language, and it contains placeholders.
3. Email templates support different template engines through an interface (D44).
4. The library has zero or minimal memory allocation and high performance.
5. Each I/O path is async and takes a `CancellationToken`.
6. Expected failures are values (`ErrorOr<T>` and `Error?`). The library defines no custom exception types.
7. Outgoing messages go through an outbox. A failed publish can retry, and the outbox keeps the full history of the publish tries.
8. Incoming domain events from a source that is not a resilient broker go through an inbox.
9. The stores serialize typed envelopes with the serializer of their storage: JSON files in a folder, BSON in a NoSQL database, or a JSON column in SQLite or SQL Server. A stored record stays readable and editable.
10. Each persisted contract is backward compatible and has a version number.
11. All times come from `TimeProvider`, so that tests can use `FakeTimeProvider`.
12. Each service interface can have a logging wrapper. The subscription store can have a caching wrapper.
13. The library owns the dispatch loops. Each loop waits for the next message with a work queue, a bounded `Channel<T>`, and hides the poll of the store.
14. Unit tests use xUnit and NSubstitute.
15. The package `MsgHatch.Subscriptions` is a router with subscriptions on three owner levels for each message type: default, company, and user (D97). A subscription for the users of an event is overridable: a lower level overrides it for each channel (D72). A mandatory subscription, or a subscription for the company or for named users, is fixed: no lower level overrides it (D88, D89). Each level can have several channels, each with its own settings. An API lists the message types, the channels with their features, and the subscriptions, and creates, reads, updates, and deletes subscriptions (D53). `docs/subscription-domain.md` describes this domain in detail.
16. SQLite is a relational store for the inbox, the outbox, and the subscriptions, when no SQL Server or MongoDB is available (D55).
17. User ids, company ids, and tenant ids are strings, so that each consumer can map its own id type, for example `int`, `long`, or `Guid` (D73, D114).
18. An incoming message can be marked as a test message. Test data stays separate, can be deleted after a test, and can be left out of traffic analytics. Each channel can handle a test message differently (D82, D83).
19. A full routing test runs all message types with their test events, for all subscriptions, test users, and languages, and reports each result (D85).
20. Core MsgHatch does not know subscriptions. It routes each event through one simple contract: the received message goes in, and the message routes come out. A route is one target on one channel, with its settings, language, and address. The router decides how it finds the routes, what it stores, and how configurable it is (D95).
21. Each channel registration composes its messages through one simple contract: the received message and the routes of one channel go in, and one channel envelope for each route comes out. How a composer loads its data and renders its templates is the job of the composer (D96).
22. The event consumer routes and composes, and does not send. One publisher for each channel sends (D101). The event consumer and the publishers can run in one process as background services, with no poll of the store. Or each publisher runs in its own process, and finds new messages by a poll of the outbox, or by a signal between processes, for example a named pipe (D102).
23. An API serves a UI for the traffic and the history of the inbox and the outbox. It lists the messages with filters, shows each publish try and the route trace, counts the traffic, and shows the state of each channel. It retries and cancels stuck messages, pauses and resumes channels, and deletes old finished messages (D128). A channel is marked down for all its publishers, after errors or by a person (D106, D107).
24. MsgHatch supports several tenants in one system, and the data of one tenant never reaches another tenant (D114 to D116). Each event and message can belong to a tenant. The tenant services, for example the routers, the directory, and the tenant stores, exist one time for each tenant as keyed services. So their methods have no tenant parameter, and the code of a setup with one tenant looks the same (D126). A storage package can keep one storage for each tenant (D125).
25. MsgHatch gives traces and metrics through the .NET APIs `ActivitySource` and `Meter`, so OpenTelemetry collects them with no extra package. A send in another process links to the trace of its event (D131 to D133).
25. The channels and the message types come from the code. A registry reader gives them, from memory with no database. An optional registry writer stores settings on top of them for an administrator: display names, descriptions, and enabled flags (D117, D118).
26. The work comes in phases, in the order of the roadmap. Each phase ends in a tested and working state, and each channel, each storage technology, the caching, and each administration API come in their own phases (D122).

### 1.1 Target flow

1. **Trigger.** A domain event arrives. Its type is the message type to send. If the source is not a resilient broker, MsgHatch first persists the event in the inbox, and then processes it from there.
2. **Route.** The router of the message type gives the routes of the event (D95). A route is one target on one channel, with the channel settings, the language, the address, the template scope, and a trace that tells why the router gave it. A consumer can write a simple router, or use the router of `MsgHatch.Subscriptions` (D97). That router reads the scope of the event: one or more user ids, or one company id. For each user and each channel, it takes the subscription of the user for the message type. If the user has none for that channel, it takes the subscription of the company of the user, and if the company has none, the default subscription. An opt-out means "not subscribed". A mandatory subscription of the default or the company level always applies to its channel. The router then adds the fixed subscriptions for the company and for named users, which no lower level can override. For a company scope, a channel with group delivery, for example the UI notification channel, sends one message to the whole company. The language is the language of the subscription, or else the UI language of the user.
3. **Select templates.** For email, the composer selects the template for each message type, language, and scope. Each template declares the attributes that it needs as placeholders.
4. **Load data.** The domain event usually contains only a few ids. The composer loads all data that the selected templates need before it generates the messages.
5. **Generate.** The composer generates each message. Steps 3 to 5 are the job of the composer of each channel registration, not of the pipeline (D96).
6. **Store.** MsgHatch persists each complete generated message in the outbox.
7. **Publish.** A dispatcher reads the pending messages from the outbox and sends each one on its channel. It retries a failed publish.

Subscription example, with the router of `MsgHatch.Subscriptions`. The tests and the example app use it. `docs/subscription-domain.md` has more examples. The users Client 1 (UI language English) and Client 2 (UI language Hungarian) belong to the company Acme (default language Hungarian, company email address `admin@acme.example`):

| Level | A | B | C | D | E |
|---|---|---|---|---|---|
| Default | Email | Email | SignalR, mandatory | none | none |
| Company Acme | none | opt-out, and a fixed email to the company | none | a fixed email to Client 2 | Email (en) |
| User Client 1 | none | Email (en) | none | none | opt-out |

"none" means that the level has no subscription for that message type. A subscription with no language uses the UI language of the user, or the default language of the company for a company target. The effective result:
- Client 1 gets A and B by email in English, and C as real-time UI updates in English. Client 1 gets no D and no E.
- Client 2 gets A by email and C as UI updates in Hungarian, and E by email in English. Client 2 gets no B. Client 2 gets D by email in Hungarian for each D event of Acme, also when Client 2 is not in the scope of the event.
- `admin@acme.example` gets one email in Hungarian for each B event of Acme. The company opt-out of B stops only the emails to the users.
- No company and no user can stop the UI updates of C, because the default subscription of C is mandatory.
- An event of the type C with the scope company Acme gives one UI notification, in Hungarian, to the SignalR group of Acme.

## 2. Terms

| Term | Meaning |
|---|---|
| Consumer | The application that uses MsgHatch. A consumer of a broker library, for example a Wolverine handler, is a different thing, and the plan always writes its full name. |
| Domain event | A C# record with properties that the consumer defines. Each record type is one message type (D15). |
| Message type | The kind of notification, for example A to E in the subscription example. The registration gives each one a stable name. |
| Message context | `MessageContext`: the message id, the tenant, and the flags of one event. The caller gives it to `AcceptAsync`, `ProcessAsync`, or `PreviewAsync` (D115). |
| Router | `IMessageRouter<TEvent>`. It takes a received message and gives its message routes (D95). The consumer writes one, or uses the subscription router. |
| Message route | One delivery target on one channel, with the channel settings, the language, the channel address, the template scope, and the route trace. The outbox gets one outgoing message for each route. |
| Route trace | The data that tells why a router gave a route. Each router package derives its own trace type from `RouteTrace` (D98). |
| Envelope composer | `IEnvelopeComposer<TEvent, TEnvelope>` of one channel registration. It takes the received message and the routes of one channel, and gives one channel envelope for each route (D96). |
| Channel registration | One `As<Channel>` registration of a message type on one channel, for example `AsEmail`: its name, its composer, its content type, and its retry options. Its composer gets the payload loader of its payload registration (D61, D87, D96, D136). |
| Payload registration | One `WithPayload` registration of a message type: one payload type, its payload loader, and the channel registrations that use that payload type (D136). `WithEventPayload` registers the domain event as the payload type, with a library loader (D139). |
| Subscription router | `SubscriptionRouter<TEvent>` of the package `MsgHatch.Subscriptions`. It gives the routes from the stored subscriptions of three owner levels (D97). |
| User | A person who gets messages. A `UserId` identifies the user (D73). |
| Company | A group of users. A user belongs to one company or to no company. A `CompanyId` identifies the company. |
| Tenant | A group of companies and users that MsgHatch keeps apart from the other tenants. A `TenantId` identifies the tenant. User ids and company ids are unique only inside one tenant (D114). A setup with one tenant uses no tenant. |
| Tenant service | A service that exists one time for each tenant: a keyed service with the tenant key as its key (D126). Its methods have no tenant parameter. For example, the router, the recipient directory, and the tenant stores. |
| Tenant key | `TenantKey`, the key of a tenant service. `TenantKey.None` is the key of the data with no tenant, and `TenantKey.For(tenant)` gives the key of a tenant (D126). |
| Tenant source | `ITenantSource`, which the consumer implements. It lists the tenants, for a setup with one storage for each tenant (D125). |
| Notification scope | The scope of a domain event for the subscription router: one or more users, or one company. The function of `WithScope` gives it (D16, D74). |
| Subscription | One stored record for one owner, one message type, and one set of recipients: either one channel with its channel settings, or an opt-out. |
| Subscription level | Default, company, or user. The owner of a default subscription is the whole tenant, or the whole system with no tenant. The owner of a company subscription is one company, and of a user subscription one user. The level tells who owns the subscription. The recipients tell who gets the messages. |
| Recipients | The receivers of a subscription, separate from its level: the scope users, the company, or named users (D89). |
| Scope users | The users of an event that the owner of a subscription covers: all users of the event for a default subscription, the users of the company for a company subscription, and the owner user for a user subscription. |
| Opt-out | A subscription that means "not subscribed" at its level, for one channel or for all channels. It is always for the scope users. |
| Overridable subscription | A subscription for the scope users that is not mandatory. For each channel, a lower level overrides it (D72). |
| Mandatory subscription | A default or company subscription for the scope users that always applies to its channel. No lower level overrides it or opts out of it (D88). |
| Fixed subscription | A subscription that no lower level can override: a mandatory subscription, or a subscription for the company or for named users (D89). |
| Effective subscription | The message route that the subscription router gives for one delivery target and one channel. Its route trace is a resolution trace. The stored subscriptions that give the effective subscriptions are the applied subscriptions. |
| Resolution trace | The route trace of the subscription router. It records where each decision of an effective subscription comes from: the subscription that applied with its level, the sources of the target, the language, and the address, and each skipped subscription with its reason (D92). |
| Delivery target | A user, or a company. A company is the target of a channel with group delivery on a company scope, and of a subscription for the company. |
| Recipient directory | `IRecipientDirectory` of `MsgHatch.Subscriptions`, which the consumer implements. It gives the company, the UI language, and the channel addresses of a user, the default language and the channel addresses of a company, and the users of a company (D76). |
| Channel address | The address of a user or a company on a channel, for example an email address. The router puts it into the route. The subscription router takes it from the recipient directory. |
| Template scope | The key of a template override. The router sets it. The subscription router uses the company of the delivery target (D80). |
| Channel | A delivery path: email, UI notifications with SignalR, or messages through a message broker. A .NET `Channel<T>` is always called a work queue in the plan. |
| Channel descriptor | The description of a channel: its id, its name, its features, and the type of its channel settings (D77). |
| Channel features | The flags of a channel: languages, rich text, raw data, and group delivery. |
| Channel settings | The typed settings of one channel subscription, for example the language and the format of an email. |
| Template payload | A class or record with properties. It contains the data of one message. The consumer defines one payload type for each message type and channel. |
| Template engine | A component that turns the template of one part and a template payload into the output text of that part. `ITemplateEngine<TPayload>` is its interface (D44, D213). |
| Placeholder engine | The first template engine. It reads text templates from a template store, and replaces each `{{PropertyName}}` with a payload property (D45). |
| Template | The text of one email for one message type, language, and scope, in the template store of a tenant or in the shared template store (D123). It has the parts `subject`, `html`, and the optional `text`. |
| Placeholder | `{{PropertyName}}` in a template part. It refers to a property of the template payload. |
| Channel envelope | The content of an outgoing message for its channel: `EmailMessage` for email, `SignalREnvelope<TPayload>` for SignalR, and `BrokerEnvelope<TPayload>` for the broker (D17). Each channel has its own envelope type (D135). It has no delivery state. |
| Delivery state | The retry summary of one inbox or outbox item: `TryCount`, `MaxTryCount`, `LastTryAtUtc`, `LastError`, and the other fields in section 4.4. |
| Lease | The time until which one dispatcher owns a claimed item. A claim sets `LeaseUntilUtc` and `NextTryAtUtc` to the end of the lease, so no other claim takes the item before that time (D103). |
| Received message | `ReceivedMessage<TEvent>`, the inbox envelope. It contains a delivery state, the tenant, the message type, the content version, the flags, the trace context, and the domain event. |
| Outgoing message | `OutgoingMessage<TEnvelope>`, the outbox envelope. It contains a delivery state, the source message id, the tenant, the message type, the channel, the delivery target, the content type, the content version, the flags, the trace context, the route trace, and the channel envelope. |
| Publish try | One try to publish an outgoing message, with its outcome: a success or an error. An outgoing message has one or more publish tries. |
| Trace context | The W3C `traceparent` of an OpenTelemetry span. Each received and outgoing message keeps the trace context of the span that made it, so that a later span links to it (D132). The route trace is a different thing. |
| Content type | The registered name of a persisted event type, envelope type, settings type, or trace type, for example `OrderShipped`. With its version, it gives a stable discriminator, for example `OrderShipped@2`. |
| Content version | The contract version of a persisted domain event or envelope type. The consumer increases it for each breaking change (section 4.13). |
| Schema version | The contract version of the library's own persisted types. The library owns it. |
| Stored record | The persisted form of one received or outgoing message. The store selects the format: a JSON file, a BSON document, or a SQL row with JSON columns. |
| Inbox | The durable store of received messages. |
| Outbox | The durable store of outgoing messages and their publish tries. |
| Tenant store | `IInboxStore` or `IOutboxStore`: a tenant service that adds, reads, counts, retries, and cancels the messages of one tenant (D127). |
| Dispatch store | `IInboxDispatchStore` or `IOutboxDispatchStore`: one service for all tenants. It claims messages, records the tries, keeps the down states of the channels, and deletes test data and old messages (D127). |
| Dispatcher | A component that claims due items from the inbox or the outbox and processes them with retry. |
| Work queue | The bounded `Channel<T>` between the claim loop and the send workers of one dispatch loop (section 4.9). |
| Event consumer | The role of a host that routes and composes the events, and writes the outgoing messages into the outbox. It does not send. `MsgHatchRoles.Consumer` selects it (D101, D105). The term "consumer" alone means the application that uses MsgHatch. |
| Publisher | The role of a host that sends the messages of the channels whose transports it registered. `MsgHatchRoles.Publisher` selects it (D101, D105). A publisher instance is the dispatch loop of one channel in one process. |
| Down channel | A channel that the outbox dispatch store marks as down, for all its publisher instances: after transient errors in a row (reason `Errors`), or by a person (reason `Operator`). No publisher instance claims its messages until it is up again (D106). |
| Stuck message | A pending message that is due since a long time, because no publisher runs or its channel is down. Or a failed message that a person must retry or cancel. |
| Delivery management | `IDeliveryQueries` and `IDeliveryCommands`, the API for a UI that shows the traffic and the history of the messages, and repairs stuck messages and down channels (D107, D289). `DeliveryManager` is the in-process implementation of both. |
| Registry reader | `IMessageRegistry`. It gives the registered channels and message types with their settings. The default reader is in memory, from the DI registrations (D117). |
| Registry writer | `IRegistryWriter`. It reads and writes the registry settings, only for the administration. A setup in code needs no writer (D117). |
| Registry settings | The stored settings of a channel or a message type on top of the code: a display name, a description, and enabled flags (D118). |
| Test message | A message with `MessageFlags.Test`. Its data is separate from the real data, and a test run deletes it at the end (D82). |
| Preview | A run of steps 3 to 5 of the pipeline that stores and sends nothing, and returns one result for each route: `INotificationService.PreviewAsync` (D84, D100). |
| Test event | A domain event that a registration makes for a test user, for one test case of a message type (section 4.18). |
| Test user | A real user of the recipient directory that a routing verification uses for its test events (D85). |
| Test profile | A copy of the profile of a test user with another UI language, for one variation of a routing verification (section 4.18). |
| Routing verification | A full test of the subscription router: all message types, test events, subscriptions, test users, and languages, with previews only (D85). |

## 3. Decisions

"Confirmed" means that the owner decided it. "Recommended" means that the plan uses this option until the owner changes it.

| # | Decision | Status | Option in the plan | Alternative |
|---|---|---|---|---|
| D1 | Target framework | Confirmed | `net10.0`. .NET 10 is an LTS release with support until November 2028. | .NET 9, with support until 2026-11-10. |
| D2 | Channel identity | Recommended | `readonly record struct ChannelId(string Name)`. Each channel package defines its own id, so a new channel needs no change in `MsgHatch.Domain`. | An enum of channel types in the core project. |
| D3 | Data types | Confirmed | A domain event record, the inbox envelope `ReceivedMessage<TEvent>`, one template payload type for each message type and channel, and one channel envelope type for each channel. | Untyped placeholder data. |
| D4 | Placeholder syntax | Recommended | Logic-less `{{PropertyName}}` for top-level properties, with an own parser. The placeholder engine HTML-encodes each value in an `html` part. | Loops and conditions in the placeholder engine. |
| D5 | Placeholder check | Recommended | A placeholder with no matching property makes the template invalid when it loads, before any message uses it. A null value of a nullable property renders as empty text. | Check each placeholder at render time. |
| D6 | Templates by channel | Recommended | Only email has templates. For SignalR and the broker, the template payload is the message body. | JSON text templates for SignalR. |
| D7 | Outbox scope | Recommended | All messages go through the outbox. A channel can set a default expiry. SignalR has a default of 5 minutes, because an old UI update has no value. Email and the broker have no default expiry (D94). | The outbox is optional for each channel. |
| D8 | Dispatcher hosting | Confirmed | The library owns the dispatch loop. The consumer starts `RunAsync` one time, for example from its own `BackgroundService`. `DispatchPendingAsync` and `ProcessPendingAsync` are for scheduled hosts, for example a Lambda function or Hangfire. The library has no hosting dependency. | The consumer owns the loop, or the library ships an `IHostedService`. |
| D9 | Broker outbox | Recommended | One MsgHatch outbox for all channels. Each publisher claims only the messages of its channel (D104). | The outbox of the broker library for the broker channel, for example of Wolverine. |
| D10 | Storage | Recommended | The consumer can implement the store interfaces. The library ships these stores: the in-memory stores in `MsgHatch.Domain` (D119), two template stores (embedded resources and files), the JSON file stores in `MsgHatch.Data.FileSystem` (D26), the SQLite stores in `MsgHatch.Data.Sqlite` (D55), the EF Core stores in `MsgHatch.Data.EFCore` on each relational EF Core provider, with the SQL Server package `MsgHatch.Data.EFCore.SqlServer` and the PostgreSQL package `MsgHatch.Data.EFCore.PostgreSql` (D120, D318), and the MongoDB stores in `MsgHatch.Data.MongoDb` (D121). The subscription stores are in `MsgHatch.Subscriptions.Data.FileSystem`, `.Data.Sqlite`, `.Data.EFCore` with `.Data.EFCore.SqlServer` and `.Data.EFCore.PostgreSql`, and `.Data.MongoDb` (D97). | The consumer writes each store. |
| D11 | Duplicate prevention | Recommended | Deterministic outgoing message ids (UUID version 5 from SHA-1) and stores that ignore an insert of an existing id. No shared transaction is necessary. | A unit-of-work interface across the inbox and outbox stores. |
| D12 | Inbox use | Recommended | Optional. `AcceptAsync` stores a received message in the inbox. `ProcessAsync` runs the pipeline directly, for a resilient broker. | The inbox is always used. |
| D13 | Async contract type | Recommended | `ValueTask` in all library interfaces. | `Task`. |
| D14 | Tests | Confirmed | xUnit with NSubstitute substitutes. | xUnit with hand-written fakes. |
| D15 | Message type | Recommended | Each message type has its own domain event record type. The registration gives it a stable name. | One record type with a `MessageType` property. |
| D16 | Scope of an event | Recommended | For the subscription router, the consumer registers with `WithScope` a typed function that gives the notification scope, for example `e => NotificationScope.ForUsers(e.CustomerIds)` or `e => NotificationScope.ForCompany(CompanyId.From(e.CompanyId))`. The domain event stays a plain record with no MsgHatch type. | The event implements a MsgHatch interface, or the caller gives the scope to `AcceptAsync`. |
| D17 | Content of an outgoing message | Confirmed | The envelope keeps only what `OutgoingMessage` does not already have. Email keeps the rendered `EmailMessage`, a library type, with no copy of the payload, because the rendered email is the complete record of what the recipient got. SignalR keeps `SignalREnvelope<TPayload>` with the payload. The broker keeps `BrokerEnvelope<TPayload>` with the payload and the headers. The target and the method name come from `OutgoingMessage` (D113). | The rendered email and the payload together. It doubles the size of each email row, and puts a consumer type with its versioning risk into each email row. |
| D18 | Placeholder binding | Recommended | A Roslyn source generator writes a placeholder writer for each payload type. It writes each property with no boxing and no reflection. | Reflection one time for each payload type, with compiled delegates. It does not support Native AOT. |
| D19 | Envelope levels | Confirmed | The channel envelope and the outgoing message are separate types. `ReceivedMessage<TEvent>` and `OutgoingMessage<TEnvelope>` contain the same `DeliveryState`. | One envelope type for each channel that also contains the delivery state. |
| D20 | Message id | Recommended | The caller gives the message id to `AcceptAsync` and `ProcessAsync`, for example the broker message id. A plain record has no id. | The registration reads an id property of the event. |
| D21 | Channel address | Recommended | `IRecipientDirectory` gives the channel addresses of each user (D76). | An address in each subscription. |
| D22 | Record format | Confirmed | The stores receive typed envelopes and serialize them with the serializer of their storage: JSON files, BSON in a NoSQL database, or a JSON column in SQL Server. A record stays readable and editable in the tools of its storage. | The library serializes the content to `byte[]`. |
| D23 | Stored names and enums | Recommended | Property names are camelCase. Enum values are strings, so that a new enum member does not change a stored value. `MsgHatchJson` applies this for `System.Text.Json`, and `docs/stores.md` shows the MongoDB conventions. | Enum values as numbers. |
| D24 | Contract versioning | Confirmed | Backward-compatible contracts, with a version number for each persisted type (section 4.13). | none |
| D25 | Breaking changes | Recommended | A breaking change gets a new content version and an upgrade function from each old version directly to the current type. A record with a newer version than the code knows is a transient error, so that a newer instance processes it during a rolling deployment. | An upgrade chain from each version to the next one. |
| D26 | File stores | Recommended | The package `MsgHatch.Data.FileSystem` ships `FileSystemInboxStore` and `FileSystemOutboxStore` for the JSON file option. They suit one machine, or a shared folder with atomic rename. | The consumer writes the file stores. |
| D27 | Publish tries | Confirmed | Each outgoing message has a one-to-many relation to `PublishTry` records. Each try records a success or an error, so the full history stays. A SQL store keeps the tries as a JSON array column of the message row, so the record of one try is one `UPDATE` (section 4.5). | Only the last try, in the delivery state. Or a child table of tries, with two writes in a transaction for each try. |
| D28 | Publish receipt | Recommended | A successful send returns a `PublishReceipt` with the provider message id, for example the SES message id. The publish try stores it. | A successful send returns no data. |
| D29 | Inbox history | Recommended | Not now. The inbox keeps only the summary fields of its delivery state. | A `ProcessTry` history for the inbox now. |
| D30 | Discriminators | Recommended | The content type registry gives stable discriminators of the form `{ContentType}@{Version}`. A C# rename does not change a stored record. | CLR type names. |
| D31 | Time source | Confirmed | All times come from an injected `TimeProvider`. Tests use `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing` (section 4.14). | `DateTime.UtcNow`. |
| D32 | Clock guard | Recommended | `Microsoft.CodeAnalysis.BannedApiAnalyzers` bans the direct clock APIs and the `IConfiguration` indexer (D63) in all projects, so a direct clock read or an indexer read does not compile. The version is 3.3.4, because the analyzer check of phase 1 found that the versions 4.14.0 to 5.6.0 do not report a banned indexer. `src/GeneratedCodeSuppressions.cs` turns off `RS0030` only in the namespace of the configuration binding generator (D65), because its generated code reads the indexer. | Code review only. |
| D33 | Retry jitter | Recommended | The jitter derives from the message id and the try number, not from `Random`. A test can then predict the exact next try time. | `Random.Shared`. |
| D34 | Logging wrappers | Confirmed | Each service interface can have a logging wrapper that logs to `ILogger<T>` (section 4.15). | Logging inside each implementation. |
| D35 | Wrapper wiring | Recommended | The registration methods add the wrappers. `MsgHatchOptions.EnableLogging`, true by default, adds or removes all logging wrappers. No decorator package is necessary. | Scrutor `Decorate`. |
| D36 | Wrapper scope | Recommended | Logging wrappers for each interface with I/O or with a consumer implementation. Pure in-memory components get no wrapper. | A wrapper for each component. |
| D37 | Log content | Recommended | Ids, types, counts, error codes, and elapsed times. Never a channel address, a payload value, rendered text, or a domain event, because these can contain personal data. | Full payloads at the Trace level. |
| D38 | Subscription cache | Recommended | An opt-in `SubscriptionStoreCachingWrapper`. It caches the subscriptions of each message type and owner: the defaults, one company, or one user. It caches empty results, reads all missing owners with one store call, and removes the entries of an owner after each change through the store. | One entry for each complete scope. |
| D39 | Cache implementation | Confirmed | The caching wrapper uses the interface `ISubscriptionCacheBackend`. The separate packages `MsgHatch.Subscriptions.Caching.Hybrid` (HybridCache) and `MsgHatch.Subscriptions.Caching.FusionCache` (FusionCache) implement it. `MsgHatch.Subscriptions` has no cache package dependency. | `IMemoryCache` in `MsgHatch.Subscriptions`. |
| D40 | Wrapper order | Recommended | From outside to inside: the logging wrapper, the caching wrapper, the implementation. One log line describes each call, and the caching wrapper logs its hits and misses at the Debug level. | The caching wrapper outside. Then the log shows only the store calls. |
| D41 | Dispatch loop | Confirmed | For each channel, a work queue (a bounded `Channel<T>`) between one claim loop and the send workers, a dispatch signal (D102), and an adaptive poll of the store (section 4.9). | `Parallel.ForEachAsync` over each claimed batch. |
| D42 | Loop defaults | Recommended | 8 send workers, a queue capacity of 16, a lease of 60 seconds, a lease safety margin of 10 seconds, and a poll delay from 1 second to 30 seconds. | Values from the number of processors. |
| D43 | Store results | Recommended | Lists, not `IAsyncEnumerable`, in the store, subscription, and loader contracts. A claim is one atomic batch, and the pipeline needs all subscriptions and payloads of an event. | `IAsyncEnumerable` results. |
| D44 | Template engines | Confirmed | Email templates use an engine interface, `IEmailTemplateEngine<TPayload>`. Each email registration selects its engine (section 4.10). | One fixed template format. |
| D45 | First engine | Confirmed | The placeholder engine first. An engine with loops and conditions follows later as a separate package. Phase 22 built it with Razor (D193 to D202), and the owner then removed it, because a Razor template cannot be added at run time (D204). The Liquid engine of D203 follows instead (section 4.10). The owner: "Package + NuGet ref (Recommended)", and later "Revert Razor, keep folders (Recommended)". | Razor components as the first engine. |
| D46 | Location of the template code | Recommended | The template stores, the placeholder engine, and the placeholder writers are in `MsgHatch.Channels.Email`, because only email uses templates (D6). | Template code in `MsgHatch.Domain`. |
| D47 | Solution file | Recommended | `MsgHatch.slnx` at the repository root, in the XML solution format of the .NET 10 SDK. | A `.sln` file in `src/`. |
| D48 | Package versions | Recommended | Central package management in `Directory.Packages.props`. | A version in each project file. |
| D49 | Options class names | Recommended | The suffix `Options`, the .NET convention for `IOptions<T>`. | The suffix `Config`. |
| D50 | Folder layout | Recommended | Feature folders. The folder path is the namespace. A folder never has the name of a type in it, because the namespace then hides the type. So the error types are in the folder `Failures`, not `Errors`. | Folders by kind of type, for example one folder for all interfaces. |
| D51 | Warnings | Recommended | `TreatWarningsAsErrors` in the `src` projects. | Warnings that do not stop the build. |
| D52 | xUnit version | Recommended | xUnit v3 (`xunit.v3`). The test projects set `IsTestingPlatformApplication` to `false`, so that `dotnet test` runs them through VSTest and `xunit.runner.visualstudio`. With the .NET 10 SDK, `dotnet test` in VSTest mode does not run a Microsoft Testing Platform application. | xUnit v2. The new `dotnet test` mode for Microsoft Testing Platform. |
| D53 | Subscription management | Confirmed | The C# API `ISubscriptionManager` of `MsgHatch.Subscriptions`. It lists the channels with their features, the message types with their effective subscriptions, and the subscriptions, each with optional filters for the message type, the company, and the user. It creates, reads, updates, and deletes subscriptions of all three levels (section 4.17). | none |
| D54 | HTTP endpoints | Recommended | The package `MsgHatch.Subscriptions.AspNetCore` maps the subscription management API to minimal API endpoints. The consumer adds the authorization. | The consumer writes its own endpoints. |
| D55 | SQLite stores | Confirmed | The package `MsgHatch.Data.Sqlite` ships `SqliteInboxStore` and `SqliteOutboxStore`, a relational alternative when no SQL Server or MongoDB is available (section 4.5). | none |
| D56 | SQLite access | Recommended | `Microsoft.Data.Sqlite` with own SQL statements, `INSERT OR IGNORE`, and `UPDATE ... RETURNING`. | EF Core. It adds a large dependency and hides the claim statement. |
| D57 | SQLite time format | Recommended | UTC times as ISO 8601 text with 7 fractional digits and the suffix `Z`. This text sorts in time order, and a person can read and edit it. | Ticks as integers. |
| D58 | Implementation package names | Confirmed | A storage implementation is `MsgHatch.Data.<Technology>`, for example `MsgHatch.Data.Sqlite`, `MsgHatch.Data.EFCore`, or `MsgHatch.Data.MongoDb`. A package of the subscription router starts with `MsgHatch.Subscriptions`, for example `MsgHatch.Subscriptions.Data.Sqlite`, and a cache backend is `MsgHatch.Subscriptions.Caching.<Technology>`, for example `MsgHatch.Subscriptions.Caching.FusionCache` (D97). | Names with no family prefix, for example `MsgHatch.Sqlite`. |
| D59 | Location of the store interfaces | Confirmed | The store interfaces stay in `MsgHatch.Domain`, as repository interfaces of the domain. Each `MsgHatch.Data.<Technology>` package references only `MsgHatch.Domain`. `ISubscriptionStore` is in `MsgHatch.Subscriptions`. Each `MsgHatch.Subscriptions.Data.<Technology>` package references `MsgHatch.Subscriptions` and its core package `MsgHatch.Data.<Technology>`, so it shares the options and the helpers of that package. One setup in code sets these options one time (section 4.7). No `MsgHatch.Data` base project exists. | Subscription data packages with no reference to a core data package. They need their own options classes for the same settings. |
| D60 | Package setup | Confirmed | One setup call registers all parts of an implementation package for the domain interfaces, with their wrappers, options, and third-party services (section 4.7). A second layout of a package, for example one storage for each tenant, has its own `Use…` method. | Many calls for one layout, for example one for the backend and one for the third-party library. |
| D61 | Setup method names | Recommended | `Use<Technology>` selects an implementation, for example `UseSqliteStores`. `Add<Thing>` registers a message type, and `As<Channel>` registers a form of a message type on one channel, for example `AsEmail`. | `Add` for all methods. |
| D62 | Two implementations for one interface | Recommended | A startup validation error that names both setup methods. | The last setup method wins. This hides a configuration error. |
| D63 | Configuration access | Confirmed | Only typed options: each setting is a property of a dedicated options class, with data validation attributes on its properties. No code uses an `IConfiguration` indexer. The analyzer of D32 bans the indexer. | `IConfiguration` indexers. |
| D64 | Options validation | Recommended | A source-generated validator for each options class (`[OptionsValidator]`), with `ValidateOnStart`. It uses no reflection. A rule across properties is in `IValidatableObject.Validate` of the options class. | `ValidateDataAnnotations()`. It validates with reflection, and needs `Microsoft.Extensions.Options.DataAnnotations`. |
| D65 | Options binding | Recommended | Each options class has a constant `SectionName`. Each setup method binds that section with `BindConfiguration`, and then applies its `Action<TOptions>`. The configuration binding source generator creates the binding code. From phase 54 (D328, D329), an options class has the constant `SectionKey`, the key of its section under the root of `AddMsgHatch`, in place of `SectionName`, and a setup method binds `builder.SectionPathOf(SectionKey)`. `MsgHatchOptions.DefaultSectionKey` is the default root `MsgHatch`. | Options only from `Action<TOptions>` callbacks. |
| D66 | Test projects | Confirmed | One test project for each production project and each level, named `{Project}.Tests.Unit`, `{Project}.Tests.Integration`, or `{Project}.Tests.Performance`, in `tests/` (section 5.5). The level is in the path, so one glob selects each level. | One test project for all code. |
| D67 | Contract base classes | Confirmed | The use-case tests of each domain interface are in one abstract generic base class `{Interface}TestsBase<T>`. Each implementation gets the full coverage with one subclass that only builds the implementation (section 5.5). | Separate tests for each implementation. |
| D68 | Test access | Recommended | Tests reach the code only through its public API and the DI setup methods. No `InternalsVisibleTo`. A type that needs its own test is public for a reason of its own, or it is tested through the public type that uses it. | `InternalsVisibleTo` for the test projects. |
| D69 | Test data | Recommended | Factory methods with valid defaults and named parameters, in each test project. No test data library. From phase 54, a test that needs realistic data, for example an integration test or a seeder with many values, uses Bogus with a fixed seed, only in the test project that needs it. The owner: "If there would be a need to realistic test data, then I'd use Bogus, which is a great NuGet library." | Bogus, with a fixed seed, for all test data. |
| D70 | Coverage | Recommended | `coverlet.collector` in each unit and integration test project. `coverage.ps1` writes an HTML report with ReportGenerator, a local tool in `dotnet-tools.json`, and fails when the line coverage of the production code is less than 90 %. An uncovered line is a behavior with no test, not a number to raise. The coverage leaves out the generated code, with `GeneratedCodeAttribute`, `CompilerGeneratedAttribute`, and the files `*.g.cs`. From phase 54, it also leaves out the EF Core migrations that the tools write: the files `{timestamp}_{name}.cs` with their `Designer` files, and the model snapshots `*DbContextModelSnapshot.cs`. The hand-written migration base classes stay counted. The integration tests on real servers run the migrations. The owner: "generated codes and EFCore migrations shouldn't require test coverage and shouldn't decrease the coverage %", and then chose "Exclude migrations". | No coverage data. Coverage as a diagnostic with no lower limit. |
| D71 | Level of the SQLite tests | Confirmed | The tests of the SQLite stores, also in the example app, are unit tests. SQLite runs in process, and its only dependency is a database file in a temporary folder, which a unit test may use. This is a deliberate exception to the rule that a database test is an integration test. | Integration tests for SQLite. |
| D72 | Subscription levels | Confirmed | Each message type has three owner levels: default, company, and user. The overridable subscriptions merge for each channel: for one user and one channel, the most specific level with a subscription or an opt-out for that channel applies. An opt-out for all channels covers each channel of its level. Each level can have several channels, each with its own settings. | The most specific level with a subscription replaces the levels above it, with all its channels. |
| D73 | User and company ids | Confirmed | `UserId`, `CompanyId`, and `TenantId` (D114) hold strings. `From<T>` maps any `ISpanFormattable` id, for example `int`, `long`, or `Guid`, with the invariant culture, and `TryAs<T>` maps it back (section 4.4). | A generic id type parameter in each API. |
| D74 | Scope of a delivery | Confirmed | An event has a user scope or a company scope. For a user scope, each user gets the message. For a company scope, a channel with group delivery sends one message to the company, for example to the SignalR group of the company, and SignalR delivers it to the active sessions of all users of the company. | User scopes only. |
| D75 | Company scope on other channels | Recommended | A channel without group delivery, for example email, sends to each user of the company, with the resolution of that user. The per-user resolution skips the channels with group delivery, so that no user gets a message two times. | One message to an address of the company. |
| D76 | Recipient directory | Recommended | The consumer implements `IRecipientDirectory`. It gives the company, the UI language, and the channel addresses of each user, the default language and the channel addresses of a company, for example the email address of the company administrator, and the users of a company. | Addresses and languages in each subscription. |
| D77 | Channel descriptors | Confirmed | Each channel package registers a `ChannelDescriptor<TSettings>` with its `ChannelFeatures` (languages, rich text, raw data, group delivery), a typed `ChannelSettings` record as `TSettings`, and an `IChannelSettingsValidator`. The compiler checks that the settings type derives from `ChannelSettings`, and that the JSON type information of `AddChannelType` has the same settings type. The registry gives each channel as the base record `ChannelDescriptor`. | The same settings fields for all channels. A `Type SettingsType` parameter, which accepts each type and fails each route at run time for a wrong type. |
| D78 | Subscription store | Confirmed | `MsgHatch.Subscriptions` defines `ISubscriptionStore` for the subscriptions of all levels, and resolves the levels itself. `MsgHatch.Subscriptions.Data.FileSystem`, `.Data.Sqlite`, `.Data.EFCore`, and `.Data.MongoDb` implement it. | The consumer implements the store and the resolution. |
| D79 | Concurrent changes | Recommended | Each subscription has a `Revision`. An update with an old revision gives `Errors.Conflict`. | The last write wins. |
| D80 | Template scope | Recommended | The template scope is the company of the delivery target, so that a company can have its own email templates. | A free template scope string in each subscription. |
| D81 | Message type list | Confirmed | `GetMessageTypesAsync` returns each message type that the subscription router routes: each message type with a scope function. Each one has the applied subscriptions of the given company or user: for each channel, the subscription or the opt-out that applies, with its level. The channels of one message type can come from different levels. | Only the message types that are available to a company. |
| D82 | Test messages | Confirmed | `MessageFlags.Test` marks an incoming message. The flag goes to each outgoing message. The stores persist it, each log record carries it, and `IDeliveryManager.DeleteTestDataAsync` deletes the test data through the dispatch stores (section 4.18). | Test messages only in a separate environment. |
| D83 | Test delivery | Recommended | `TestDeliveryOptions` for each channel: `Suppress` (the default: record a try with the outcome `Suppressed`, and send nothing), `Redirect` (send to a test address or a test group), or `Deliver`. | Send a test message like a normal message. |
| D84 | Preview | Confirmed | `INotificationService.PreviewAsync` runs steps 3 to 5 of the pipeline, stores and sends nothing, and returns one result for each route. With no routes from the caller, the router of the message type gives them (section 4.8). | A `MessageFlags.DryRun` flag on the normal processing. It is a second way to do the same thing, and a member of a stored enum that is never stored. |
| D85 | Routing verification | Confirmed | `IRoutingVerifier` of `MsgHatch.Subscriptions` with registered test events, test users, and languages, with previews only, and with one report row for each combination (section 4.18). | Manual tests only. |
| D86 | Automatic down state | Confirmed | After `ErrorThreshold` transient errors in a row from the transport of one channel, the dispatch loop marks the channel down for all its publisher instances (D106). No publisher instance claims a message of that channel, and one publisher instance at a time calls `ITransport.CheckHealthAsync` every `CheckInterval`. A successful check marks the channel up. A message of a down channel keeps its tries, so a long provider outage does not move all messages to `Failed`. `ChannelDownOptions` holds the threshold and the interval (section 4.9). | More tries and a longer `MaxDelay`. Each message still uses up its tries in a long outage. |
| D87 | Retry for each message type | Confirmed | Each channel registration of a message type has its own named `RetryOptions`, with its content type as the name. The values start from the retry options of the channel. The section `MsgHatch:Retry:{ContentType}` and `ChannelBuilder.WithRetry` then change only the values that they set (section 4.4). For example, a password reset email can expire after 15 minutes, while a weekly report waits one day. | Retry options only for each channel. |
| D88 | Mandatory subscriptions | Confirmed | A default or company subscription for the scope users can be mandatory. It always applies to its channel. No lower level overrides it or opts out of it, and the management API rejects such a change with `Errors.Conflict`, so a settings UI can show the channel as locked. | Channels that the registration marks as always on in code. |
| D89 | Recipients | Confirmed | Each subscription has recipients, separate from its owner level: the scope users, the company, or named users. A user subscription is always for its own user. A subscription for the company goes to the company: its SignalR group, or its address from `CompanyProfile.Addresses`. A subscription for the company or for named users is fixed. A company subscription can name only users of that company, and the resolution skips a named user of another company. | Recipients that follow from the level only. A company administrator then needs a user subscription of each administrator. |
| D90 | Trigger of a fixed subscription | Confirmed | A fixed subscription of a company sends one message for each event that concerns that company: an event with the company scope of that company, or with at least one user of that company. A fixed default subscription for the company sends to each company that the event concerns. A fixed default subscription for named users sends for each event of its message type. | Only events with a company scope. |
| D91 | Duplicate deliveries | Recommended | For one delivery target and one channel, the resolution gives one effective subscription. A fixed subscription applies before an overridable one. Between two fixed subscriptions, the higher level applies. On the same level, a subscription for named users or for the company applies before a mandatory one for the scope users. | The outbox store keeps the first message with the same deterministic id. The settings that apply then depend on the order. |
| D92 | Resolution trace | Confirmed | The subscription router gives each effective subscription and each opt-out decision a `ResolutionTrace`. It names the subscription that applied with its level, the source of the target, the language, and the address, and each skipped subscription with the rule that removed it (section 4.4). It is the route trace of the route (D98), so the payload loaders, the composers, and the template engines receive it, and the outbox stores it. | A log of the resolution only. A stored message then cannot explain itself later. |
| D93 | Settings of one channel | Confirmed | The channel settings come whole from the one subscription that applies to the target and channel. A lower level that overrides a channel replaces all its settings, for example the language and the format of an email together. The trace names that one subscription as the source of the settings (section 4.4). | Each field inherits from the higher levels on its own. It needs nullable settings fields, a merge function for each channel, and a trace for each field. |
| D94 | Expiry of an outgoing message | Confirmed | `Expiry` is optional, and null means no expiry. SignalR has a default of 5 minutes, because an old UI update has no value. Email and the broker have no default expiry, because a broker message can be an integration event that another service needs, also after a long outage. A message type that gets stale sets its own expiry with `WithRetry` (D87). A message that a pause or an outage holds back past its expiry gets an `Expired` try, and is not sent. The expiry counts from the accept time of the event, so the time in the inbox counts too (D142). | A required expiry for each channel. It drops broker events after a long outage with no warning, and it cannot express "never", because the range stops at 30 days. |
| D95 | Routing contract | Confirmed | Core MsgHatch routes each event through `IMessageRouter<TEvent>`: the received message goes in, and the message routes come out. A route is one target on one channel, with its channel settings, language, address, template scope, and trace. How the router decides, what it stores, and how configurable it is, is the job of the router. A simple router is a few lines of consumer code (section 4.6). | The subscription model in core. Each consumer then gets the three owner levels, their stores, and their management API, also when it needs none of them. |
| D96 | Composer contract | Confirmed | Each channel registration composes its envelopes through `IEnvelopeComposer<TEvent, TEnvelope>`: the received message and the routes of one channel go in, and one envelope for each route comes out. The payload loader and the template engine are parts of the library composers, not steps of the pipeline (section 4.8). | A payload loader step in the pipeline, and a composer that receives the payloads. The pipeline then knows how each channel gets its data. |
| D97 | Subscription packages | Confirmed | The three-level subscription model is the router package `MsgHatch.Subscriptions`, with its own family: `MsgHatch.Subscriptions.Data.FileSystem`, `.Data.Sqlite`, `.Data.EFCore`, `.Data.MongoDb`, `.Caching.Hybrid`, `.Caching.FusionCache`, and `.AspNetCore`. The core packages contain no subscription code (section 4.17). | The subscription stores in the core data packages. Each consumer of the SQLite outbox then also gets the subscription package. |
| D98 | Route trace | Confirmed | `RouteTrace` is an abstract record. Each router package derives its own trace type, for example `ResolutionTrace` of the subscription router, and registers it with `MsgHatchBuilder.AddTraceType`, with the content type `Trace:{Name}` and a version. The outbox stores the trace with its discriminator (section 4.5). A router with nothing to explain gives no trace. | A fixed trace type in core. It ties the outbox to one router. |
| D99 | Router registration | Recommended | `MessageBuilder<TEvent>.WithRouter<TRouter>()` selects the router of one message type. `MsgHatchBuilder.UseRouter(Type)` selects an open generic router for each other message type, for example `SubscriptionRouter<>`. The router of one message type wins over the open generic router (section 4.7). | One router for all message types, or a router for each message type only. |
| D100 | Route preview | Recommended | `PreviewAsync` also takes the routes from the caller (D84). The routing verifier of `MsgHatch.Subscriptions` resolves the routes with test profiles, and then calls it with these routes (section 4.18). | The verifier runs the whole pipeline with a replaced recipient directory. It needs a hook in the DI scope of each run. |
| D101 | Event consumer and publishers | Confirmed | The event consumer routes and composes the messages, and writes them into the outbox. It does not send. The outbox dispatcher runs one dispatch loop for each channel whose transport the host registered, and only these loops send. One process can host the event consumer and all publishers as background services. Or each channel publisher runs in its own process, and stops, restarts, and deploys without the others. A stopped publisher lets the messages of its channel wait in the outbox, until they are sent or expire (section 4.9). | One dispatcher for all channels in each process. A stop or a defect of one channel then stops the others too. |
| D102 | Dispatch signal | Confirmed | `IDispatchSignal` wakes a dispatch loop when new messages wait for its topic: the inbox, or one channel. The default `InProcessDispatchSignal` works in one process, with no poll of the store. Between processes, a publisher polls the store, or a later package implements the same contract with a named pipe, a message broker, or another messaging solution between processes (section 4.9). A signal is a hint: a lost signal only delays a send until the next poll or due time. | An in-process doorbell with no contract, and polling only between processes. |
| D103 | Lease and next due time | Confirmed | A claim sets `NextTryAtUtc` and `LeaseUntilUtc` of each claimed message to the end of the lease, so a due message is a pending message with `NextTryAtUtc` at or before now. Each claim also returns the earliest `NextTryAtUtc` of the pending messages of its channel, which includes the lease ends. After a try with a next try time, the worker gives that time to its claim loop. A loop waits until the earliest of these times, a signal, or the poll delay. So a retry needs no poll, and `DispatchOptions.EnablePolling` can be false where a signal reaches each loop (section 4.9). | Polling only. A retry then waits for the next poll. |
| D104 | Outbox parts | Confirmed | One outbox store with a part for each channel. `IOutboxDispatchStore.ClaimDueAsync` takes one channel. SQL stores have the partial index `(Channel, NextTryAtUtc) WHERE Status = 'Pending'`, so the index holds only the backlog and the claimed messages, and the file store has a folder for each channel. The event consumer writes all messages of one event in one `AddAsync` call (section 4.5). | One outbox store for each channel. The event consumer then writes each event into several stores, and a query for traffic reads several stores. |
| D105 | Host roles | Recommended | `MsgHatchOptions.Roles` is `Consumer`, `Publisher`, or both, and both is the default. It tells the startup validation what the host must have: a router for each message type for an event consumer, and at least one transport for a publisher. The DI registrations do not change with the role, so all processes call the same registration code (section 4.7). | Separate setup methods for an event consumer and for a publisher. The registration of the message types is then split in two parts. |
| D106 | Shared down state of a channel | Confirmed | The outbox dispatch store keeps one down state for each channel, for all its publisher instances, with the reason `Errors` or `Operator`. Each dispatch loop counts its own transient errors, and the first loop that reaches `ErrorThreshold` marks the channel down. Each claim of a down channel claims nothing. One publisher instance at a time runs the health check, because `TryStartChannelCheckAsync` moves the next check time atomically. A successful check ends only the reason `Errors`. A pause of a person ends only by a resume of a person (section 4.9). | A pause in each process. Each publisher instance then finds an outage by itself, and a person cannot stop all publisher instances of a channel. |
| D107 | Delivery management | Confirmed | `IDeliveryManager` lists the received and outgoing messages with filters and pages, shows the detail of an outgoing message with its route trace and its publish tries, counts the traffic, and shows the state of each channel. It retries and cancels messages, and pauses and resumes channels, so that a UI can repair stuck messages and down channels (section 4.19). | The consumer reads and changes its stores itself. |
| D108 | Store members for the management | Confirmed | The query, count, retry, and cancel members are on the tenant stores `IInboxStore` and `IOutboxStore` (D127), in the same way as `ISubscriptionStore` holds the delivery reads and the management. One registration serves both, and the contract base classes test all members. Each consumer store must implement them. | Separate optional admin stores. They need more interfaces, registrations, and wrappers. |
| D109 | Message content | Confirmed | The lists and the details hold no content. `GetReceivedContentAsync` and `GetOutgoingContentAsync` give the event and the envelope, and `MapMsgHatchDeliveryContent` maps them as a separate route group, so that the host can give the content a stricter authorization policy (section 4.19). | The content in the detail, with one route group and one policy. |
| D110 | Cancelled messages | Confirmed | `DeliveryStatus.Cancelled` and `PublishOutcome.Cancelled`, at the end of the enums. The cancel of an outgoing message records a `Cancelled` try with the time. The traffic counts keep the cancelled messages apart from the failed ones, so a failure alert does not fire for them (section 4.19). | The status `Failed` with a `Cancelled` try. The failed counts then include the cancelled messages. |
| D111 | Retry of a failed message | Recommended | A retry gives a failed message a new set of tries: `MaxTryCount` becomes `TryCount` plus the `MaxTryCount` of its retry options. A retry of a pending message only makes it due at once. A retry skips a delivered, a cancelled, a leased, and an expired message (section 4.19). | One more try for each retry. After an outage, the first transient error then fails the message again. |
| D112 | Message pages | Recommended | Pages with the newest message first, and a cursor of `CreatedAtUtc` and `Id` for the next page. The summaries hold no content, so a SQL store reads only columns (section 4.19). | Pages with an offset. They skip or repeat messages while the messages change, and are slow on a large table. |
| D113 | Type and target of an outgoing message | Recommended | `OutgoingMessage` has the `MessageType` of the event and the `DeliveryTarget` of the route, so that a store can filter and count by them, for example all messages of one user (section 4.4). | The target from the envelope. An email envelope has only the address, so a search for a user is not possible. |
| D114 | Tenants | Confirmed | `TenantId` holds a string, as `UserId` and `CompanyId` do (D73). A tenant is a group of companies and users that MsgHatch keeps apart from the other tenants. User ids and company ids are unique only inside one tenant. The stored messages keep their tenant, and the tenant services exist one time for each tenant (D126). So each store, directory, cache key, deterministic message id, template store, and SignalR name belongs to one tenant. A setup with one tenant uses no tenant, which is null. | Ids that are unique in the whole system, with the tenant as a label only. |
| D115 | Message context | Confirmed | `AcceptAsync`, `ProcessAsync`, and `PreviewAsync` take a `MessageContext` with the message id, the tenant, and the flags. The caller gives it, for example from the request or from the header of a broker message. The pipeline resolves the tenant services with `TenantKey.For(context.Tenant)` (D126). The received message and each outgoing message store the tenant (section 4.6). | A function on the event, or an ambient tenant service. |
| D116 | Subscriptions in a tenant | Confirmed | The subscription store, the recipient directory, the subscription router, and the subscription manager are tenant services (D126). The store of a tenant holds only the subscriptions of that tenant, and the three levels work inside it: the default level is the default of that tenant. An event of a tenant uses only the services of that tenant, and an event with no tenant only the services of `TenantKey.None` (section 4.17). | A fourth level above the tenants, or the tenant as a filter only. |
| D117 | Registry reader and writer | Confirmed | `IMessageRegistry` reads the channels and the message types, with synchronous lookups over a frozen snapshot and no logging wrapper. `IRegistryWriter` reads and writes the stored settings, only for the administration. The default reader is in memory, from the DI registrations, so a setup in code needs no writer and no database (section 4.20). | Separate readers and writers for the channels and for the message types, with async reads and logging wrappers on the path of each event. |
| D118 | Registry settings | Confirmed | The code defines each message type, channel, composer, and transport. The writer changes only stored settings on top of the code: a display name, a description, and an enabled flag for each message type, for each channel, and for each channel of a message type. The pipeline skips a route on a disabled channel, and stores nothing for a disabled message type (section 4.20). | Display data only, or message types that a person defines at run time with no code. |
| D119 | In-memory stores | Confirmed | `MsgHatch.Domain` has `InMemoryInboxStore` and `InMemoryOutboxStore`, selected with `UseInMemoryStores`. They pass the contract base classes, so phase 1 reaches the outbox with no database. They are not durable (section 4.5). | The JSON file stores in phase 1. |
| D120 | EF Core stores | Confirmed | `MsgHatch.Data.Sqlite` stays a small package with its own SQL (D56). The package `MsgHatch.Data.EFCore` adds stores on an `IDbContextFactory`, with SQL Server as its first provider and a claim statement for each provider. On SQL Server, the claim is an `UPDATE` of the rows of a `TOP` subquery with `ORDER BY NextTryAtUtc, Sequence` (D144), because `UPDATE TOP` takes rows in no order, and an `UPDATE` of a CTE locks each due row (section 4.5). Phase 48 changes this decision (D318): the base package has no SQL text, and the claim statement of SQL Server is the dialect of `MsgHatch.Data.EFCore.SqlServer`. | EF Core stores in place of the SQLite package. |
| D121 | MongoDB stores | Recommended | The package `MsgHatch.Data.MongoDb` stores each message as one document, with its tries in an embedded array, through the official MongoDB driver. A claim is a loop of `findOneAndUpdate` calls, one for each message (section 4.5). | One `updateMany` with a claim id, and then a find. A stop between the two calls leaves claimed messages that only the lease frees. |
| D122 | Roadmap phases | Confirmed | Each phase ends in a tested and working state. Phase 1 reaches the outbox with the in-memory stores and no dispatcher. The dispatch loop, each channel, each storage technology, the caching, and each administration API come in their own phases (roadmap). | Phases by layer. A working system then exists only after several phases. |
| D123 | Templates of a tenant | Recommended | The template store of a tenant is a tenant service (D126), and the shared template store is one service for all tenants. The template selector tries the template store of the tenant, with the scope and then with no scope. Then it tries the shared template store, with the scope and then with no scope. It does this for the language, and then for the default language. With no tenant, it tries only the shared store, also with the scope, so the company templates of D80 apply (section 4.10). | Templates for all tenants only. |
| D124 | Integration tests | Recommended | The EF Core and MongoDB phases test their stores in `.Tests.Integration` projects, against a real SQL Server and a real MongoDB in a container, with Testcontainers. D180 and D183 change this for the EF Core and MongoDB tests: they use the server of a connection string in `appsettings.Local.json` first, and Testcontainers only with no connection string. Phase 4 adds the integration tests of the Amazon SES client in `MsgHatch.Channels.Email.AmazonSes.Tests.Integration` and of the Resend client in `MsgHatch.Channels.Email.Resend.Tests.Integration`. The Resend tests run against a real Resend account in phase 4, and the SES tests run against a real SES account in phase 20. Their secrets come from `appsettings.Local.json`, which git ignores, and each of their tests is skipped with no account. The stage after the merge runs them (section 5.5). | Unit tests with an in-memory database provider. They do not test the claim statement. |
| D125 | A store for each tenant | Recommended | A storage package can keep one storage for each tenant, for example one SQLite file for each tenant, through its own setup method, for example `UseSqliteTenantStores<TTenantSource>`. The keyed tenant store of a tenant opens the storage of that tenant, and the key `TenantKey.None` opens the system storage. Only the dispatch stores, which work for all tenants, need a library composite: it claims from the storage of each tenant of `ITenantSource`, and keeps the down states in the system storage (section 4.5). | One shared storage with a tenant column only. |
| D126 | Keyed tenant services | Confirmed | Each tenant service is a keyed DI service with a `TenantKey` key, and no method of it has a tenant parameter. A setup method registers it with `KeyedService.AnyKey`, and adds an unkeyed registration that resolves the key `TenantKey.None`. So a setup with one tenant injects each service with no key, and a setup with tenants resolves the service of each tenant with `TenantKey.For(tenant)`. An implementation that needs its tenant takes a `TenantKey` parameter in its constructor, and the setup method creates it with that key. Each tenant service that it needs has the same key. The pipeline resolves the services of an event with the tenant of its message context, and the inbox processor with the tenant of the received message (section 4.6). | A `TenantId? tenant` parameter on each method of each service. Each signature then shows the tenant, and a null has two meanings. |
| D127 | Tenant stores and dispatch stores | Confirmed | Each storage of messages has two parts. The tenant store (`IInboxStore`, `IOutboxStore`) is a tenant service: it adds, reads, counts, retries, and cancels the messages of one tenant. The dispatch store (`IInboxDispatchStore`, `IOutboxDispatchStore`) is one unkeyed service for all tenants: it claims the due messages, records the tries, keeps the down states, and deletes test data and old messages. One class can implement both parts (section 4.5). | One store interface with a tenant filter for the queries and none for the claim. |
| D128 | Purge | Confirmed | `PurgeAsync` of each dispatch store deletes up to a maximum count of finished messages that finished before a time. `IDeliveryManager.PurgeAsync` and the endpoint `POST {prefix}/purge` call it. The host selects the retention, and the window of the duplicate detection is the same as the retention (section 4.19). | No delete. The stores then grow with no limit, and each claim index grows too. |
| D129 | One transport for each channel | Confirmed | `ITransport` is not generic. It has the channel, `SendAsync(OutgoingMessage, string? redirectTo, CancellationToken)`, and `CheckHealthAsync`. A channel package registers one transport for its channel, so the health check of a down channel has one transport to call. The transport gets the channel envelope from `OutgoingMessage.Content` (section 4.7). | One generic transport and one outgoing handler for each envelope type. A channel package then cannot register a transport for payload types that it does not know. |
| D130 | Singleton lifetimes | Confirmed | The library stores, transports, template stores, and registries are thread-safe singletons. The outbox dispatcher makes no DI scope for each outgoing message. Only a received message gets a DI scope, because the routers and the payload loaders of the consumer run in it: the inbox processor makes one for each claimed message, and a caller of `ProcessAsync` or `AcceptAsync` uses its own scope, for example the scope of an HTTP request. A store of the consumer must be thread-safe too, for example an EF Core store with an `IDbContextFactory` (section 4.7). | Scoped stores, with a DI scope for each claim and each message. Each send then pays for a scope, and a store with state across scopes needs a second lifetime. |
| D131 | Telemetry API | Confirmed | MsgHatch gives traces and metrics through `ActivitySource` and `Meter` of .NET, both named `MsgHatch`. `MsgHatchTelemetry.SourceName` and `MeterName` give the names, and a host turns them on with `AddSource` and `AddMeter` of OpenTelemetry. The spans are at the pipeline and at each send, not at each store call (section 4.21). | A `MsgHatch.OpenTelemetry` package with `AddMsgHatchInstrumentation()` on `OpenTelemetry.Api`. It holds two methods of one line each, and it is one more package to keep current. |
| D132 | Trace context of a message | Confirmed | Each received and outgoing message keeps the W3C `traceparent` of the span that made it. The `process` span of the inbox processor and each `send` span start a new trace, with a span link to this context, as the messaging semantic conventions describe for a send with a separate creation context. So a retry hours later does not stretch the trace of the event (section 4.21). | A parent and child relation. One trace then shows the event and each send, but a trace can last for hours of retries. |
| D133 | Tenant in telemetry | Confirmed | The spans have the tenant as the attribute `msghatch.tenant`. The metrics have no tenant tag, so many tenants do not multiply the time series, and no tenant id goes to the metrics backend. The OpenTelemetry .NET SDK keeps at most 2,000 tag combinations for each metric by default, and merges the rest into one overflow series. An opt-in option can come later with no breaking change (section 4.21). | A tenant tag on each metric, always or with an option. |
| D134 | Broker library | Confirmed | `MsgHatch.Channels.Broker` holds the broker channel and the contract `IBrokerClient`. Each broker library has its own client package, in the same way as email has `IEmailClient`. The first client is `MsgHatch.Channels.Broker.Wolverine`, because Wolverine has the MIT license and many transports. MassTransit 9 and later need a commercial license, and the support of MassTransit 8 ends on 2026-12-31, so a MassTransit client is a later phase (section 6). A direct client of one broker SDK is the second form of a client. The outbox, the retries, the down state, and the history of MsgHatch already exist, so a bus framework adds little to one publish. The first direct client is `MsgHatch.Channels.Broker.AmazonSns`, in the later phases. | One `MsgHatch.MassTransit` package with the channel and the transport together. A second broker library then needs a second copy of the channel. Or a framework with its own outbox, for example Brighter or CAP, which duplicates the MsgHatch outbox (section 7). |
| D135 | Envelope and loader of each channel | Confirmed | Each channel has its own envelope type. An envelope that carries the payload is generic over the payload type, for example `SignalREnvelope<TPayload>` and `BrokerEnvelope<TPayload>`. The email envelope `EmailMessage` carries no payload. So two channel registrations of one message type never share a composer `IEnvelopeComposer<TEvent, TEnvelope>`, also when they have the same payload type. Two channels of one message type with the same payload type use the same payload loader `IPayloadLoader<TEvent, TPayload>`. In one DI scope, their composers get the same loader instance, and each composer calls it with the routes of its own channel. The startup validation enforces the rule: a message type has at most one registration for each channel, its channel registrations have different envelope types, and each of its payload types has one loader type. `WithPayload` registers the loader of a payload type one time, apart from the channel registrations of that payload type (D136). | The payload itself as the envelope of SignalR, and a startup check that rejects two channel registrations with one envelope type. Or one composer registration for each channel registration, which needs a second DI key next to the tenant key. |
| D136 | Payload registration | Confirmed | A payload loader is a data service of the consumer, not a part of a channel or of a template engine. `MessageBuilder<TEvent>.WithPayload<TPayload, TLoader>(p => p.AsEmail("OrderShippedEmail"))` registers the loader of one payload type one time, and the channel registrations of that payload type inside its `PayloadBuilder<TEvent, TPayload>`. No channel method names a loader type. Each composer gets the loader through DI, so the channels of one payload type share it (D135). The channel packages add their `As<Channel>` methods to `PayloadBuilder<TEvent, TPayload>`, so C# infers the event type and the payload type of each call. C# binds explicit type arguments also to the type parameter of the receiver, so a method `AsEmail<TPayload, TLoader>` on `MessageBuilder<TEvent>` cannot compile in another assembly. For the same reason, another template engine is a factory argument of `AsEmail`, not a type argument. | A loader type argument on each `As<Channel>` method. It couples the data to a channel, and needs the event type as an explicit type argument. Or a flat `UsePayloadLoader` before each channel registration, which needs a startup rule for a channel with no loader. |
| D137 | A transport or an upgrade that throws | Confirmed | When a transport or an envelope upgrade function throws, the dispatcher logs the exception, and records a `TransientError` try with `Errors.Exception`. The retry schedule and `MaxTryCount` apply, so a message that always throws reaches `Failed`. This try does not change the error count of the down state of its channel (D86), because a throw is a fault of the code, not an outage of the provider (section 4.8). | No try for a throw. The lease then ends, a later claim sends the message again, and a message that always throws never reaches `Failed`. |
| D138 | Resend email client | Recommended | The package `MsgHatch.Channels.Email.Resend` calls the REST API of Resend through `IHttpClientFactory` and the named HTTP client `MsgHatch.Resend`, with source-generated JSON. Each send carries the outgoing message id as its `Idempotency-Key`, so Resend sends a repeated try within 24 hours only one time. `CheckHealthAsync` reads `GET /domains`. A 401 `restricted_api_key` answer comes from a valid key that can only send, so it counts as healthy. An error of the API key (401, or 403 `restricted_api_key` or `suspended_api_key`) gives `Errors.Unavailable` and a Critical log, so the messages stay pending until an administrator acts (section 4.4). | The official `Resend` SDK. Its client takes `IOptionsSnapshot`, a scoped service, so a singleton transport cannot hold it. It also brings an ASP.NET Core package and reflection-based JSON, and it loses the error names that its enum does not know. |
| D139 | The event as its own payload | Confirmed | `MessageBuilder<TEvent>.WithEventPayload(p => p.AsBroker(...))` sends the domain event unchanged, with no payload type and no loader of the consumer. It registers the internal loader `EventPayloadLoader<TEvent> : IPayloadLoader<TEvent, TEvent>` with `UsePayloadLoader`. The loader returns the event of the received message for each route, as one instance. So the payload type is the event type, and the D135 rule of one loader type for each payload type applies. A `WithPayload` call with an own loader for the event type then fails the startup validation. The receiver gets all properties of the event, for example a browser through SignalR. So the consumer selects it only when each property is fit for each receiver. `AsEmail` requires `IPlaceholderPayload<TPayload>`, so an email of the event needs `[PlaceholderPayload]` on a partial event record. D16 keeps a domain event free of MsgHatch types, so the event payload mainly fits SignalR and the broker. | A public `EventPayloadLoader<TEvent>` that the consumer names in `WithPayload<OrderShipped, EventPayloadLoader<OrderShipped>>`, with the event type two times in each call. Or an own payload type for each channel registration, with a loader that only copies the event. |
| D140 | Delivery guarantee of the Wolverine broker client | Confirmed | A receipt of `WolverineBrokerClient` means that the broker accepted the message. So the host configures Wolverine with `SendInline()` on the route of each broker payload type, `Durability.UseSyncRetryBlock = true`, and the publisher confirms of its transport. For RabbitMQ, these are `PublisherConfirmationsEnabled` and `PublisherConfirmationTrackingEnabled`. Then `SendAsync` throws when the broker does not accept the message, and the MsgHatch outbox retries, counts the down state, and keeps the history (D9). The startup validation fails when `UseSyncRetryBlock` is false. The route mode and the confirms stay a rule of the host, which the documentation of `UseWolverineBroker` gives. An exception that a retry cannot fix gives a permanent error, so the outbox does not retry it: a refused message, a message that is too large, and a serialization failure give `Errors.Rejected`. Two gaps stay. Wolverine sets the RabbitMQ flag `mandatory` only in `ServerlessWithOutbox`, so the broker confirms a message to a queue that does not exist, and drops it. And with endpoint telemetry, the default, the inline agent of Wolverine ignores a `NotSupportedException` of the transport. | The defaults of Wolverine. A buffered route, or an inline route with the default retry block, returns before the send, so an outage gives `Succeeded` for a lost message. Or the durable outbox of Wolverine after the MsgHatch outbox, which is a second outbox (D9). Or the Wolverine outbox in place of the MsgHatch outbox. The broker channel then loses the duplicate prevention (D11), the separate publisher (D101), the history and the management (D108), the test delivery, and the down state. |
| D141 | Exception classifiers of the host | Confirmed | `IExceptionClassifier<TClient>` gives the `ErrorType` of an exception, or null when it does not know the exception. The type argument is the client that asks it, so DI gives each client only its own classifiers. `AddExceptionClassifier<TClient, TClassifier>()` adds many classifiers for each client. The SES client, the Resend client, the Wolverine client, and the SignalR transport ask them in the order of their registration, before their built-in mapping, and the first result that is not null wins. A cancelled operation gives `Errors.OperationCancelled` before each classifier. So a host can classify the exceptions of a package that MsgHatch does not reference, for example a Wolverine transport package, and can change a built-in result. A classifier returns an `ErrorType`, not a bool, so that the history, the logs, and the metrics keep the code. The dispatcher stays the only part that decides about retries, from `IsTransient`. | One interface for all clients, with a key of the channel or a `Supports(Type client)` method. A wrong key then shows only at run time, and the SES and Resend clients share one channel. Or a bool "retry" result, which loses the error code. Or the built-in mapping before the classifiers, so that a host cannot change a built-in result. |
| D142 | Expiry of a received message | Confirmed | The inbox has no expiry of its own. The `ExpiresAtUtc` of an outgoing message is the `CreatedAtUtc` of its received message plus the `Expiry` of its channel registration. So an event that waited one hour in the inbox gives no SignalR update with a new 5 minutes, and an email with no expiry is still sent. `ProcessAsync` and `PreviewAsync` make their received message now, so their expiry does not change. The `ExpiresAtUtc` of a received message stays null, and the startup validation rejects `MsgHatch:Retry:Inbox:Expiry`, so the setting does not stay with no effect. | An expiry of the inbox from `MsgHatch:Retry:Inbox:Expiry`, which fails an expired event before the pipeline. Then no outgoing message exists, also for a channel with no expiry, and one value applies to all message types and channels. Or no expiry rule, so that a stale SignalR update after a long wait in the inbox gets a new 5 minutes. |
| D143 | JSON columns of a SQL store | Confirmed | `MsgHatchJson` writes and reads only whole messages. A SQL store writes the whole message with `MsgHatchJson`, and the database takes the event or the envelope, the route trace, and the trace discriminator out of that document into their columns, for example with `json_extract` in SQLite. To read a message, one SQL expression puts the columns back into one document, for example `json_object` in SQLite, and `MsgHatchJson` reads it as the closed type. So the domain has no public API for the parts of a message, and each store serializes one time (section 4.11). | Members of `MsgHatchJson` that write and read only the event, the envelope, or the route trace, with a public type for the other columns of a message. |
| D144 | Claim order | Confirmed | A claim takes and returns the due messages in the order of their `NextTryAtUtc` (section 4.6). Messages with the same `NextTryAtUtc` come in the order of their add: in the in-memory store and the SQL stores always, in the file store for the adds of one instance in a row (D26), and in the MongoDB stores for the adds of one process in a row (D187). This order is not the event order. The library sets `CreatedAtUtc` and `NextTryAtUtc` to the time of the add, so a backfill that must keep the event order adds the messages in that order. A SQL store keeps the order of the add in an explicit counter column, in SQLite `Sequence INTEGER PRIMARY KEY`, with the old key as a `UNIQUE` constraint. The SQLite documentation says that `VACUUM` can change the `rowid` of a table with no `INTEGER PRIMARY KEY`. SQLite puts the counter at the end of each entry of the due index, so the index gives the claim order with no sort step. | `CreatedAtUtc` in the claim key, which does not hold the event time and adds a sort step to each claim. Or the plain `rowid`, which a `VACUUM` can change. |
| D145 | Language of channel settings | Confirmed | `MsgHatch.Domain.Routing` has the interface `ILanguageSettings` with `string? Language { get; }`. `EmailChannelSettings` and `SignalRChannelSettings` implement it with their positional `Language`. The subscription router reads the language of the settings through it, because `MsgHatch.Subscriptions` does not reference the channel packages. Settings that do not implement it have no language. The broker settings and the JSON of each settings type do not change (section 4.4). | `public virtual string? Language => null` on `ChannelSettings`, which each settings type with a language overrides. Then each settings type has the member, and the JSON of `BrokerChannelSettings` gets `"language": null`. |
| D146 | Form of the subscription resolver | Confirmed | `SubscriptionResolver` is a static class. `Resolve(channels, scope, subscriptions, users, companies, defaultLanguage)` takes the channels of the message type, the scope, the subscriptions, the loaded user and company profiles, and `MsgHatchOptions.DefaultLanguage`. It does no I/O. The caller loads the profiles first, as step 4 of the routing procedure says (section 4.17). | A public input record for `Resolve`, which is one more public type to keep stable. Or a singleton in DI, which a test can reach only through a container. |
| D147 | Visibility of the subscription router | Confirmed | `SubscriptionRouter<TEvent>` is internal. `UseSubscriptions` selects it with `UseRouter`, and the tests reach it through `INotificationService` (D68). Its public constructor takes the internal `SubscriptionMessage<TEvent>`. | A public router that a host selects with `WithRouter` for one message type. Its constructor then needs a public type for the scope function. |
| D148 | Helpers of a subscription data package | Confirmed | `MsgHatch.Data.FileSystem` and `MsgHatch.Data.Sqlite` give `InternalsVisibleTo` only to their subscription data packages. So these packages use the internal helpers of D59 with no copy: the failure boundary, the escape of an id in a file name, the write through a temporary file, the SQLite values and commands, and the open of a connection that creates the schema of a store one time. The rule of D68 is for the test projects. The two packages of one technology ship with the same version, because a change of an internal member breaks the other package. | Public helpers. They add 6 types and 13 members to the public API that are not features of MsgHatch. Or a copy of the helpers in each subscription data package. |
| D149 | JSON of the channel settings of a subscription | Confirmed | The public class `ChannelSettingsJson` of `MsgHatch.Subscriptions` works on `IContentTypeRegistry`, as `MsgHatchJson` does for the messages. `Serialize(channel, settings)` writes the settings with the newest version of the content type `Settings:{Channel}` of the channel, and gives `SerializedSettings(ContentType, Version, Json)`. An unregistered channel, or settings of another type than the settings type of the channel, gives `Errors.SerializationFailed`. `Deserialize(contentType, version, json)` reads the settings as the registered type of that version (D77). A version that is newer than the code gives `Errors.UnsupportedVersion`. An unknown content type, a content type that is not a settings type, or invalid JSON gives `Errors.SerializationFailed` (section 4.13). Each subscription store uses it, also a consumer store (section 4.5). | An internal class with `InternalsVisibleTo`, so that a consumer store writes its own copy. Or a copy in each store. |
| D150 | Shared options of two setup methods | Confirmed | `MsgHatchBuilder.ConfigureSharedOptions<TOptions>(string setupMethod, Action<TOptions> configure)` records the setup method that gives a value in code to a shared options class. It applies the value with `PostConfigure`, so the value from code wins over the `BindConfiguration` of each setup method in each order of the calls. The core startup validation fails when two setup methods give a value in code to one options class, and its message names both methods (D62). `UseFileStores`, `UseSqliteStores`, `UseFileSubscriptionStore`, and `UseSqliteSubscriptionStore` call it with a value from code. | An internal record and a validator in each core data package, which the subscription data package reaches with `InternalsVisibleTo`. Then each technology repeats the check. |
| D151 | Read group of the subscription store tests | Confirmed | The abstract class fixture `AcmeAndGlobexScenario<TStore>` in `tests/MsgHatch.Subscriptions.Tests.Unit/Contracts/` implements `IAsyncLifetime`. It resets its own storage, seeds the scenario of section 4 of `docs/subscription-domain.md` one time through the store, and resets the storage after the last test of the class. Each subclass of `SubscriptionStoreTestsBase<TStore>` declares `IClassFixture` of a sealed subclass of the fixture, which only builds the store on storage of its own, and gives the fixture to the base class. The tests that only read the scenario, for example S7, S8, and S9, read from it. So each store keeps one test subclass. | A second contract base class for the read tests, with a second subclass for each store. Or a seed of the scenario in each test. |
| D152 | Entry of the HybridCache backend | Recommended | HybridCache keeps a value of a mutable type in memory as serialized bytes, and gives each read a new copy. `Subscription` has the abstract `ChannelSettings`, so a read with the default serializer fails (checked in phase 11). So the backend keeps each list in the public class `HybridCacheSubscriptionEntry`, which is sealed and has `[ImmutableObject(true)]`. HybridCache then keeps the instance in memory with no serializer, and gives it to each read with no copy. A distributed second level needs a serializer of this type, which the consumer registers in HybridCache. The owner noted that the admin API needs subscription DTOs, so a later phase can give this serializer from those DTOs. | A serializer of `Subscription[]` in the package now. Then each read from memory makes a copy, and the package needs its own JSON contract of `Subscription`. |
| D153 | Ids in the subscription cache keys | Recommended | A tenant id, a company id, and a user id can hold each character except a control character, also `:`. So the keys of section 4.15 could be the same for two tenants, for example for the tenant `a` with the company `u:5` and the tenant `a:X` with the message type `c` and the user `5`. The caching wrapper writes each of these ids in a key or a tag percent-encoded with `Uri.EscapeDataString`. An encoded id has no `:`, so `:` separates the parts with no doubt. An id of only ASCII letters, digits, `-`, `_`, `.`, and `~` does not change, so most keys look as section 4.15 shows. | Other separators between the parts (the owner asked): no printable separator is safe, because an id can hold it. A control character is never in an id, but HybridCache 10.11.0 does not cache a key with one (checked in phase 11). Or the length before the tenant id: `t:{Length}:{TenantId}`. |
| D154 | `/me` routes of the subscription endpoints | Confirmed | A `/me` route takes the owner only from the signed-in user, and ignores the owner fields `level`, `companyId`, and `userId` of a body. It reads the subscription before a `PUT` or a `DELETE`. An unknown id and a subscription of another owner give the same 404 with the same body, also for a `DELETE`, so a user cannot find out which ids exist in the tenant. When `CurrentUser` gives null, for example for a service account, a `/me` route gives 403 and calls no manager: the request passed the authorization of the host, and a 401 needs a `WWW-Authenticate` header, which the library cannot give. `CurrentTenant` with no `CurrentUser` throws `ArgumentException` when the routes are mapped (section 4.17). | A 400 for owner fields that name another owner. Or a 204 for an unknown id, as the `DELETE` with no `/me` gives: a 404 then shows that the id exists for another owner. Or a 401 with no `WWW-Authenticate` header. |
| D155 | Channel features in JSON | Confirmed | `GET {prefix}/channels` writes the flags enum `ChannelFeatures` as one text, for example `"Languages, RichText"`, as the string enum converter writes it. So the rule "enum values as strings" applies with no extra code (section 4.17). | An array of names. It needs a list in the response type and a mapping of the flags. |
| D156 | Request bodies of the subscription endpoints | Confirmed | The endpoints read and write each body with their own source-generated `SubscriptionEndpointJson`, not with the JSON options of the host, so that the names and the enum values do not change with the host. A `POST` or a `PUT` with a content type that is not JSON gives 415 and calls no manager. Without this check, a form of another site can post JSON as `text/plain` with the cookie of a signed-in user, and the browser sends no CORS preflight for it. Invalid JSON, a missing required property, an undefined enum value, and an invalid id or name give 400 (section 4.17). | The parameter binding of minimal API with the `JsonOptions` of the host. The names and the enum values then depend on the host, and the default options write enum values as numbers. |
| D157 | Startup work of the example app | Confirmed | At startup, the example app writes the users, the companies, and the subscriptions of the shared test scenario only when the example directory has no user. It accepts the 7 example events at each start, with new message ids. So a restart on the same database file gives no `Errors.Conflict`, shows new messages, and keeps the changes made through the subscription endpoints (roadmap, phase 13). | A new database file at each start: each run gives the same result, but a restart loses the changes made through the endpoints. Or all startup work only on an empty database: a restart then sends no message. |
| D158 | Sign-in of the example app | Confirmed | The example app signs in a caller with a JWT bearer token, through `Microsoft.AspNetCore.Authentication.JwtBearer`. `dotnet user-jwts create --name 1` makes a local token for the user `"1"`, and writes its signing key into the user secrets of the app, so the app needs no identity server and no network. `CurrentUser` of `MapMsgHatchSubscriptions` reads the claim `ClaimTypes.NameIdentifier`, and the subscription routes require an authenticated caller (roadmap, phase 13). | A header with the user id, only in the `Development` environment: no package, but not safe outside development. Or no sign-in: each `/me` route then gives 403 until a host adds its own authentication. |
| D159 | Budget breaks of a measurement | Confirmed | When a measurement of phase 13 breaks a budget line of section 4.11, phase 13 fixes it in its own commit, and measures again: a hit of the email template cache allocated 328 B, and the name of a span with no listener allocated a string. Bytes that a budget line covers, but that the measurement did not attribute, are recorded as measured, and a follow-up item of the roadmap attributes and reduces them (section 4.11). | Record the breaks and fix them in a later phase. Or change the budget lines to the measured values. |
| D160 | Unknown test user of a verification | Confirmed | A test user that the recipient directory does not know gives one `Failed` row with `Errors.NotFound` for each test case and language of that user, with no owner, no target, and no channel. The verifier still checks the other test users (section 4.18). | `Errors.ValidationFailed` for the whole request. Or no special case: the routing then gives a `Passed` row with no owner, and hides a typo in a test user. |
| D161 | Error of one verification variation | Confirmed | When the routing or the preview of one variation fails, for example a directory error or a payload loader error of the event, the verifier adds one `Failed` row with the error code and name, with no channel and with the language of the variation, and continues. Only `Errors.OperationCancelled` stops the verification, and `VerifyAsync` returns it (section 4.18). | `VerifyAsync` returns the first error with no report. One failed payload loader then hides all other results. |
| D162 | Report log of the example app | Confirmed | In the `Development` environment, the example app logs one Information record with the counts of the verification report, one Warning record for each `Failed` row, and one Information record for each `Skipped` row. Each row record has the message type, the test case, the owner level, the channel, the language, and the error (section 4.18). | One record for each row, about 30 at each start. Or only the counts, which do not show the failed check. |
| D163 | No `/me` route for the verification | Confirmed | `POST {prefix}/verification` exists only in the route group with no `/me`. A verification reads all subscriptions and profiles of a tenant, so it is an administrator action, not a change of the own user-level subscriptions (section 4.17, D154). | The same route under `{prefix}/me`. |
| D164 | Real and test counts apart | Confirmed | `MessageCount` has the field `Test`, so a count row is for one message type, channel, status, and test flag. With `IncludeTest` false, `CountAsync` gives only the rows of the real messages. With `IncludeTest` true, it also gives the rows of the test messages, apart. `GetChannelsAsync` counts with `IncludeTest` true, and `ChannelStatus` has `Pending` for the real messages and `PendingTest` for the test messages. `OldestDueAtUtc` is one value over both, because a pending test message waits in the channel too, and a publisher must claim it. So one call gives the real and the test counts apart, their sum is the total, and a failure alert ignores the test rows with no second query (section 4.19). | Count rows that mix the real and the test messages with `IncludeTest` true. A report with no test messages then needs a second query, and the channel page cannot show the test part. Or the channel state with no test messages: a stuck test message then does not show. |
| D165 | Delivery policies of the example app | Confirmed | The example app puts the policy `DeliveryAdmin` on the group of `MapMsgHatchDelivery` and the policy `DeliveryContent` on the group of `MapMsgHatchDeliveryContent`. Each policy needs the role of its name. `dotnet user-jwts create --name 1 --role DeliveryAdmin` makes a token for the overview and the actions, and a second `--role DeliveryContent` adds the content (D109, D158). | A signed-in user for both groups. Less setup, but the two policies then do not differ. |
| D166 | Status after a cancelled try | Confirmed | `PublishTry.StatusAfter` gives `Cancelled` for the outcome `Cancelled`. So one rule maps each outcome to its status, a store can use it for the cancel try too, and a wrong call of `RecordTryAsync` never marks a cancelled message as `Failed` (section 4.6). | `Failed` from the default arm, with no change of `StatusAfter`. |
| D167 | Unreadable file in a query of a file store | Confirmed | A query and a count of a file store skip a message file that the code cannot read, for example invalid JSON or a newer version. The page and the counts show each readable message, so one bad file does not break the UI. A claim already moves an invalid file to `failed` (section 4.5). | The query returns the read error. The UI then shows nothing until a person repairs or deletes the file. |
| D168 | First read of the stored registry | Confirmed | `StoredRegistry` starts its first store read when the container creates it. The pipeline waits for the end of that read, with success or with an error, before step 2. So after a restart, a disabled message type or channel sends nothing, also with an async store. The in-memory, file, and SQLite stores end the read before the container returns the registry (section 4.20). | No wait. The code values then apply until the first read ends, and an async store can let a disabled message type send after a restart. |
| D169 | Preview and the registry settings | Confirmed | Only `ProcessAsync` and the inbox processor apply the registry settings, because they store the messages. `PreviewAsync` and the routing verification ignore them, and check the routing and the composition of the code. `RouteResult` keeps its two states (section 4.8). | The preview applies them. `RouteResult` then needs a third state for a skipped route, and the verification of a disabled message type has no routes. |
| D170 | Registry policy of the example app | Confirmed | The example app puts the policy `RegistryAdmin` on the group of `MapMsgHatchRegistry`, with the role of its name, as D165 does for the delivery. `dotnet user-jwts create --name 1 --role RegistryAdmin` makes a token for it. | The policy `DeliveryAdmin` for the registry too. Fewer roles, but no split of the two rights. |
| D171 | Empty text in a registry entry | Confirmed | A `DisplayName` or a `Description` that is empty or has only white space gives `Errors.ValidationFailed`. Null is the one way to say "the value of the code", and a UI never shows a blank name (section 4.20). | An empty `Description` is valid, and hides the description of the code. |
| D172 | File name of the database of a tenant | Confirmed | `UseSqliteTenantStores` and `UseSqliteTenantSubscriptionStore` put the tenant id into `{tenant}` with the escape of the file stores: `Uri.EscapeDataString`, and then each capital letter A to Z as `^` and the small letter (section 4.5). Windows and macOS ignore the case of a file name, but a tenant id is case-sensitive, so two tenants never share one file. The tenant `a|b` gives `a%7^cb`, because the escape also changes the hexadecimal letters of `%7C`. | `Uri.EscapeDataString` only. The tenants `Acme` and `acme` then share one file on Windows and macOS, and the dispatch composite counts their messages two times. |
| D173 | Failed tenant store in a claim | Confirmed | A claim of a dispatch composite skips the store of a tenant that returns an error. The composite logs the error with the tenant, and returns the messages of the other stores. So one broken tenant storage does not stop the dispatch of the other tenants. An error of the tenant source or of the store of `TenantKey.None` fails the claim, and so does `Errors.OperationCancelled`. `CountAsync`, `DeleteTestDataAsync`, and `PurgeAsync` return the first error (section 4.5). | Each store error fails the claim, as with one shared storage. One broken tenant file then stops the dispatch of all tenants. |
| D174 | Host of the example test with a store for each tenant | Confirmed | The test in `ExampleApp.Tests.Unit` builds its host from the setup methods `UseSqliteTenantStores` and `UseSqliteTenantSubscriptionStore`, with `AddExampleMessages` and a tenant source of the test. The example app does not change. | `ExampleSetup` gets a second setup with a tenant source from the configuration. This is more code for a setup that the example app does not run. |
| D175 | EF Core provider with no SQL statements | Confirmed | `UseEntityFrameworkStores` adds a startup check, as `UseWolverineBroker` does. An EF Core provider with no SQL statements of the stores fails the startup validation with an `OptionsValidationException` that names the provider. A store call on such a provider gives `Errors.NotFound`, for a host that does not run the validation. The integration test uses the EF Core SQLite provider as the second provider (section 4.7). Phase 48 changes the check (D318): it accepts each relational provider, and refuses a provider that is not relational, for example the EF Core in-memory provider. It also fails for the dialect of a provider package that serves another provider. A store call on a provider that is not relational gives `Errors.NotFound`. The unit tests use the in-memory provider as the provider that is not relational. | `Errors.NotFound` only at the first claim or try record. The host then finds the error only in the logs of the dispatch. |
| D176 | Document of a row of an EF Core store | Confirmed | EF Core reads the columns of a row, and the store writes the document that `MsgHatchJson` reads with `Utf8JsonWriter`. A column with invalid JSON goes into the document as a JSON string, so an edited row fails only itself. This refines D143 for the EF Core stores. On SQL Server 2025, `JSON_OBJECT` writes a `datetime2` with no `Z`, and `JSON_QUERY` fails the whole statement on invalid JSON (checked facts of phase 18 in the roadmap). | `JSON_OBJECT` in SQL, as D143 says. It needs SQL Server 2022 or later, a `CASE` with `ISJSON` for each JSON column, and a time text with `Z`. |
| D177 | Ryuk container of Testcontainers | Confirmed | The test code keeps the Ryuk container of Testcontainers, so the stage after the merge removes the containers of a crashed run. A machine with no Ryuk image sets `TESTCONTAINERS_RYUK_DISABLED=true` for its own run. Each test fixture disposes its own container (section 5.5). | `ResourceReaperEnabled = false` in the test code. No machine then pulls Ryuk, but a crashed run leaves its container on each machine. |
| D178 | Column size of a name in the EF Core stores | Confirmed | A message type, channel, content type, or trace type name is `varchar(256)`, the same maximum as an id. An id of a tenant, a company, a user, or a target is `nvarchar(256)` with a binary collation, so it stays case-sensitive. The largest real key of each index stays below the SQL Server limit of 1,700 bytes. A longer name gives `Errors.Unavailable` from SQL Server (section 4.5). Phase 48 (D318): the collation of each text column comes from the option `TextCollation`, or else from the dialect, `Latin1_General_100_BIN2` in the SQL Server package, or else from the database. Phase 50 (D322): with the dialect of SQL Server or PostgreSQL, a longer name gives `Errors.ValidationFailed`. Phase 52 (D327): the unique key of a subscription is a hash column of 32 bytes, so the size of its key columns no longer counts for an index. On MySQL, an id is `varchar(256)` in `utf8mb4` with the collation `utf8mb4_bin`, and the dialect of MySQL also gives `Errors.ValidationFailed` for a longer name. | `varchar(128)`. The declared size of each index then also stays below 1,700 bytes, but a name of 129 to 256 characters fails in this store only. |
| D179 | License of a transitive package of a test database provider | Confirmed | A transitive package of the database provider that a test project uses may have the license of the vendor of that provider, when no `src` package references it. So the EF Core integration tests use `Microsoft.Data.SqlClient.SNI.runtime` 6.0.3 and `Microsoft.Identity.Client.NativeInterop` 0.20.6 under the Microsoft Software License Terms. The `src` packages keep the license rule of section 5.5. | `ExcludeAssets` for both packages and the managed networking switch of SqlClient on Windows. This adds test setup, and a later SqlClient can need the packages. |
| D180 | Server of the EF Core integration tests | Confirmed | The EF Core integration tests use the SQL Server of `SqlServerIntegration:ConnectionString` in the git-ignored `appsettings.Local.json` of each test project, for example the local container dev-mssql. With no value, they start SQL Server with Testcontainers (D124). A set server that the tests cannot reach fails the run. Each test database has a name that starts with the name of its test project, with each `.` as `_`, and the tests touch no other database. At the start of a run, they drop each such database, for example after a crashed run, and create it again at its first use. After the run, they drop each such database. The owner: "integ tests can and should use dev-mssql if available, but test database should get deleted after each run and recreated at the start of the test run" (section 5.5). Phase 49 adds PostgreSQL with the same rules: the PostgreSQL of `PostgreSqlIntegration:ConnectionString`, for example the local container dev-postgres, or else `postgres:18.7` through Testcontainers. PostgreSQL keeps at most 63 bytes of a database name, and the name of the subscription test project of PostgreSQL alone has 63 characters. So on PostgreSQL the name starts with the name of the test project in lower case, with no `.Tests.Integration`, each `.` as `_`, and `__`. The `__` keeps the prefix of one project from being the start of the prefix of another project, and the fixture refuses a name of more than 63 bytes. | Testcontainers only. Each run then starts a container, also where a SQL Server already runs. |
| D181 | Name of the EF Core packages | Confirmed | The EF Core packages, their folders, and their namespaces use `EFCore`: `MsgHatch.Data.EFCore` and `MsgHatch.Subscriptions.Data.EFCore`, with the test projects `MsgHatch.Data.EFCore.Tests.Integration` and `MsgHatch.Subscriptions.Data.EFCore.Tests.Integration`. The type names keep `EntityFramework`, for example `EntityFrameworkInboxStore` and `UseEntityFrameworkStores`, and so does the configuration section `MsgHatch:EntityFramework`. No namespace that the MsgHatch code uses conflicts with it. A package with the root namespace `EFCore`, for example `EFCore.BulkExtensions`, needs `global::EFCore` inside a `MsgHatch.Data` namespace. | `EntityFrameworkCore`, the name of most packages of the ecosystem, for example `Npgsql.EntityFrameworkCore.PostgreSQL` and `MassTransit.EntityFrameworkCore`. |
| D182 | Startup check of the EF Core subscription store | Confirmed | `UseEntityFrameworkSubscriptionStore` adds the startup check of D175 for its own context `MsgHatchSubscriptionDbContext`. A provider with no SQL statements of the subscription store fails the startup validation with an `OptionsValidationException` that names the setup method and the provider. With both setup methods, each context gets its own check. Phase 48 changes the check as D175 does (D318). The owner: "Fix items 1 to 3", from a list that named this check (section 4.7). | No check, because D175 names only `UseEntityFrameworkStores`. The host then finds the error only at the first subscription call, which gives `Errors.NotFound`. |
| D183 | Server of the MongoDB integration tests | Confirmed | The MongoDB integration tests use the MongoDB of `MongoDbIntegration:ConnectionString` in the git-ignored `appsettings.Local.json` of each test project, for example the local container dev-mongo. With no value, they start MongoDB with Testcontainers (D124). The other rules of D180 apply too: a set server that the tests cannot reach fails the run, each test database has a name that starts with the name of its test project, the tests touch no other database, and they drop each such database at the start of a run and after the run. The owner: "Phase 19 test server: yes, use dev-mongo if available, fallback to testcontainers only if not available" (section 5.5). | Testcontainers only. Each run then starts a container, also where MongoDB already runs. |
| D184 | Record format of the MongoDB stores | Confirmed | A MongoDB store writes the whole message with `MsgHatchJson`, and converts that JSON document into one BSON document with the same names: an object becomes a BSON document, an array a BSON array, and a string, a number, a boolean, or null its BSON value. The store writes the times of the delivery state and of the publish tries as BSON times (D185). The event, the envelope, the route trace with its `$type`, and the channel settings of a subscription stay BSON sub-documents. To read a message, the store converts the BSON document back into the JSON document that `MsgHatchJson` reads. So the MongoDB stores use the serializer, the version rules, and the errors of the other stores, and a consumer type needs no BSON mapping. This replaces the class maps of section 4.5 and refines D22 and D143 for MongoDB. The owner: "BSON from MsgHatchJson (Recommended)". | BSON class maps of the content type registry, with the discriminators of D30. The driver keeps the class maps for the whole process and freezes each one at its first use, and a consumer type then needs a second serializer that ignores its `System.Text.Json` attributes. |
| D185 | Times of the MongoDB stores | Confirmed | A MongoDB store keeps each time of a message, a publish try, a down state, a registry entry, and a subscription as the sub-document `{ d: <BSON date>, t: <ticks> }`. `d` is the time cut to the millisecond, for the tools, the date operators, and a TTL index. `t` is the number of UTC ticks of 100 nanoseconds as a 64-bit integer, which is the exact time. Each filter, sort, and index uses `t`, and a read takes `t` only. So a time round-trips unchanged, as in the other stores, and two due times in one millisecond keep their order (D144). A person who edits a time changes `t`. The owner asked for a choice after a look at an example of a precise date format, and then confirmed this choice: "Confirm D185". | A plain BSON date. It keeps milliseconds only, so a time comes back cut, and two due times in one millisecond are equal. Or ISO 8601 text with 7 fractional digits (D57), which the tools do not show as a date. |
| D186 | Batch add of the MongoDB stores | Confirmed | `AddAsync` of a MongoDB store writes the messages of a call with one unordered `insertMany` and no transaction, and ignores the error of a duplicate key. So each key is stored one time, also with parallel adds, and the store works on a server with no replica set. A server failure can store a part of a batch. The next add of the same messages, for example after the retry of the received message, adds the rest, because a stored key changes nothing (D104). The owner: "No transaction (Recommended)". | A transaction for each batch, as in the EF Core stores. It needs a replica set, and parallel adds of one id give write conflicts that the store must retry. |
| D187 | Add order of the MongoDB stores | Confirmed | A MongoDB store gives each new message document an ObjectId as `_id`, and a claim sorts by the due time and then by `_id`. The driver makes the ObjectIds of one process in increasing order. So messages with the same due time come in the order of their add for the adds of one process in a row, as in the file store (D26, D144). The owner: "ObjectId _id (Recommended)". | A counter document with `$inc`. The order then holds across processes, but each add needs one more write, and the adds of all processes write the same document. |
| D188 | Transport of the first dispatch signal between processes | Confirmed | Named pipes of `System.IO.Pipes`, in a package of their own (D189). A named pipe needs no server and no new NuGet package, and works on Windows, Linux, and macOS. On Linux and macOS, .NET makes a named pipe a Unix domain socket in the temp folder. So the signal reaches only the processes of one machine. This fits the SQLite and file stores, which already need one machine, and the two hosts of the example. Requirement 22 names a named pipe. Between machines, a host keeps the poll, until a later phase adds a signal between machines, for example with RabbitMQ (roadmap, later phases). The owner: "Named pipes (Recommended)". | RabbitMQ with a fanout exchange first. It reaches each machine, but each host then needs a broker, and its tests are integration tests that skip with no broker. Or both in one phase, which is about two times the work. |
| D189 | Package of the named pipe signal | Confirmed | `src/MsgHatch.Dispatch.NamedPipes` with `UseNamedPipeDispatchSignal`, and the test project `MsgHatch.Dispatch.NamedPipes.Tests.Unit`. This follows the file stores: one package for each technology, also when the technology needs only the BCL. The core keeps only `InProcessDispatchSignal`. The owner: "New package (Recommended)". | The signal in `MsgHatch.Domain`. This needs no new project, but the core then holds pipe code that most hosts never use. |
| D190 | Poll with a signal between processes | Confirmed | `UseNamedPipeDispatchSignal` adds a startup check: `EnablePolling` false in the `DispatchOptions` of the inbox or of the outbox fails the startup validation with an `OptionsValidationException`. A signal between processes can get lost, for example when the waiting process does not run, crashes, or loses its pipe name to a second process on Linux. With no poll, a lost signal can then delay a send with no limit, but D102 allows only a delay until the next poll or due time. The signal lets a host set a longer `MaxPollInterval`. The owner: "Yes, fail startup (Recommended)". | Only documentation. A host with `EnablePolling` false then finds a lost signal only through messages that wait. |
| D191 | Users of the named pipe signal | Confirmed | Both ends of each pipe use `PipeOptions.CurrentUserOnly`, so only the processes of one OS user exchange signals. The default rights of a pipe already block a writer of another user: on Windows, the default DACL gives other users only read access, and on Linux the socket file has the mode `srwxr-xr-x`. On macOS, each user has a different temp folder (checked facts of phase 21 in the roadmap). The owner: "Same user only (Recommended)". | Any local user. This needs a Windows-only ACL API, a `chmod` of the Linux socket file, and a shared folder on macOS. Any local process can then wake the loops. |
| D192 | Pipes of the named pipe signal | Confirmed | Each topic has one pipe: `{PipeNamePrefix}.inbox`, and `{PipeNamePrefix}.outbox.{Channel}` for a channel. The process that waits for a topic serves its pipe. A process that notifies the topic is a client of that pipe, and keeps its connection. A notification is one byte. So the signal needs no hub and no list of the processes. In the normal layout, one process on each machine waits for a topic (D101). With two waiting processes for one topic, a client reaches one of them on Windows, and on Linux the process that started last takes the pipe name. Each instance can claim the messages, and the poll of D190 wakes the other instance. A server starts with no `PipeOptions.FirstPipeInstance`, so a process that starts after a crash takes over the socket file that the crashed process left on Linux (section 4.9). A writer that cannot connect 3 times in a row logs one Warning with the topic, and one Information at its next connect, because a waiting process of another OS user or with another prefix is otherwise not visible. | A hub: the first process serves one pipe, and sends each notification to all other processes. This needs an election of the hub, and a new election after a crash. Or a folder with one socket file for each process, which each notifier reads, and stale files that a process must delete. |
| D193 | Subject and text of a Razor email | Replaced | Replaced by D204. A Razor email takes its subject and its optional text part from the template store, as placeholder templates with `{{Name}}`, and its html from a Razor component. So the subject and the text stay editable after deployment, and get no HTML entities: Razor writes each value through the HTML encoder (checked facts of phase 22). The payload type has `[PlaceholderPayload]`, and the component gets the same payload. The owner: "Placeholder templates". | Static members `Subject` and `Text` of the component, in C#. They get compile-time checks, but a change of a subject then needs a deployment. Or separate Razor components for the subject and the text, rendered with an encoder that does not encode: 3 components for each message type and language. |
| D194 | Language of a Razor email | Replaced | Replaced by D204. One language for each email. The engine tries the language of the request, and then the default language. A language wins only when both the component and the store template exist in it. So the subject, the html, and the text of one email are always in one language. The owner: "One language per email (Recommended)". | A separate fallback for each source. An incomplete language then gives a mixed email, for example an English subject on a Hungarian html body. |
| D195 | Lookup of a Razor component | Replaced | Replaced by D204. In one language, the engine tries the component of the company of the request (the template scope, D80), and then the component with no company. A component has no tenant level: the Razor SDK compiles it into the host, so it is the same for all tenants, as an embedded template is. The store template keeps the lookups of D123 in that language. The owner: "D123 order, no tenant (Recommended)". | Only an exact match. A company with no component of its own then gets no email. Or a tenant level, with a tenant id in the name of each component. This is more API for a need that no requirement names. |
| D196 | Package of the Razor engine | Replaced | Replaced by D204. `src/MsgHatch.Email.Razor` references the NuGet package `Microsoft.AspNetCore.Components.Web` 10.0.14 (MIT), and its test project is `MsgHatch.Email.Razor.Tests.Unit`. A host of the package needs only the runtime `Microsoft.NETCore.App`, so a worker host needs no ASP.NET Core runtime (checked facts of phase 22). The owner: "Package + NuGet ref (Recommended)". | A `FrameworkReference` to `Microsoft.AspNetCore.App`, as `MsgHatch.AspNetCore` has. Each host then needs the ASP.NET Core runtime. Or the engine in `MsgHatch.Channels.Email`. Each email host then gets the Razor packages. |
| D197 | Parts of the store template of a Razor email | Replaced | Replaced by D204. The rules of the placeholder engine stay unchanged. `PlaceholderEmailEngine<TPayload>` gets a public constructor with the parameter `PlaceholderParts parts`: `All` is the default, and `SubjectAndText` allows only the parts `subject` and `text`. The Razor engine uses `SubjectAndText`, so a store template of a Razor email with an `html` part gives `Errors.TemplateInvalid` at prepare. The owner: "Parts mode (Recommended)". | The `html` part optional in each store template. A missing html file of a placeholder email then sends only text, with no error, and an `html` part of a Razor email shows only at render. |
| D198 | Names of the Razor templates | Replaced | Replaced by D204. A Razor template is the file `{root}/{MessageType}.{Language}.body.razor`, and the template of a company is `{root}/{CompanyId}/{MessageType}.{Language}.body.razor`. The subject and the text have the same names with `.subject.txt` and `.body.txt` in the template store (D199). So a company can override only the subject or only the body. `UseRazorEmailTemplates(assembly, rootNamespace)` names the assembly and the namespace of `{root}`. The engine calculates the class of a file: the Razor SDK compiles `A.hu.body.razor` into the class `A_hu_body`, and `42/A.hu.body.razor` into `_42.A_hu_body` (checked facts of phase 22). Each component inherits `RazorEmailComponent<TPayload>`, which has the parameter `Payload`, and host code names no component class. A message type of a Razor email starts with an uppercase letter, because the Razor SDK stops the build for a component name that starts with a lowercase letter (RZ10011). The owner gave this layout. | A map of component classes in code at each registration or for each payload type, an attribute on each component with a scan, or the folders `{MessageType}/{CompanyId}/{Language}.razor`. The owner rejected component classes in host code and a folder for each message type. |
| D199 | Company templates in the template stores | Confirmed | `FileSystemTemplateStore` and `EmbeddedResourceTemplateStore` read the template of a company from the subfolder `{CompanyId}`, for both engines, for example `42/A.hu.subject.txt`, or the resource `{prefix}._42.A.hu.subject.txt`. This replaces the scope part of the file name, for example `A.hu.42.subject.txt`. So the files of a company are in one folder. The owner: "One layout for all (Recommended)". | The scope in the file name in the stores, and the folder only for the Razor body. One email then has two layouts. |
| D200 | Name of the subject part | Confirmed | The part stays `subject`, with the file suffix `.subject.txt`. So one part has one name in the code, the files, and the documents. The owner: "subject (Recommended)". | `title`, as in the first layout of the owner. This renames the files of both stores. |
| D201 | Company ids in template names | Confirmed | In the file store, each company id that is not empty has a folder: the id escaped with `Uri.EscapeDataString`, each `.` as `%2E`, and each capital letter as `^` and the small letter, as the tenant file names of D172. So a GUID id or `acme-1` has company templates, `acme` and `ACME` never share a folder on Windows or macOS, and `..` cannot lead out of the template folder. In the name of an embedded resource, MSBuild changes each character that is not valid in a namespace, for example `acme-1` to `acme_1`, and puts `_` before a folder name that starts with a digit, for example `_42` (checked facts of phase 22). So in the embedded store, only a company id of ASCII letters, digits, and `_`, which does not start with `_`, has company templates, and each other id uses the template with no company. The owner chose the escape after D204 removed the Razor engine, which was the reason for one strict rule in both stores. | One strict rule in both stores. A GUID company id then never gets its company templates in the file store, with no log, and on Windows `ACME` reads the folder `acme`. |
| D202 | Render of a Razor component | Replaced | Replaced by D204. The engine makes one `HtmlRenderer` for each render, and disposes it after the render. A shared renderer keeps about 2 KB for each render until it is disposed. A new renderer for each render allocates about 0.9 KB more than a shared one, with no measurable difference in time (checked facts of phase 22). The renderer gets a service provider that gives `HtmlEncoder.Create(UnicodeRanges.All)`, as the placeholder renderer uses (section 4.10), and each other service from the tenant services of the engine, so that a component can inject a service. The render runs in `Dispatcher.InvokeAsync`, which first sets the culture of the route. An exception of a component is an exception of consumer code: the engine does not catch it, and the boundary of section 4.12 changes it into `Errors.Exception`. | One renderer for each process. Its memory then grows with each render. Or one renderer for each DI scope. A caller with a scope that lives long then has the same growth. |
| D203 | Engine for templates that a host adds at run time | Confirmed | A host must add an email template at run time, with no build and no deployment: a file that an operator copies into the template store is enough. A later phase adds a Liquid engine with `Fluid.Core` 2.41.1 (MIT, it targets `net10.0`). It reads the subject, the html, and the text from `ITemplateStore`, so the file store, the embedded store, a store of each tenant, the company folders of D199, and the fallback of D123 all apply. Liquid has loops, conditions, filters, and partials. Fluid encodes each value with the `HtmlEncoder` of the render, the filter `raw` turns this off, and each render has its own culture. A template reads only the payload members that the host registers, so a copied file cannot run .NET code (checked facts of phase 22). The owner: "Fluid / Liquid (Recommended)". | Scriban 7.6.1 (BSD-2-Clause): HTML encoding is manual, so a missing filter gives an XSS hole. Handlebars.Net 2.4.3 (MIT): each condition beyond true or false needs a helper in C#. Loops and conditions in the placeholder parser: an own template language to maintain. |
| D204 | Removal of the Razor engine | Confirmed | Phase 22 built `MsgHatch.Email.Razor`, and the owner then removed it with the parts mode `PlaceholderParts.SubjectAndText`. The Razor SDK compiles a component into the assembly at build time, so a copied `.razor` file is never read. No supported library compiles a component at run time: the package `Microsoft.AspNetCore.Razor.Language` stops at 6.0.36, and the runtime compilation of .NET 10 is obsolete and compiles only MVC views. A component is also C# code, so a template that a host compiles at run time can run any .NET code. The company folders of D199 and D201 and the part name of D200 stay. This replaces D193 to D198 and D202. The owner: "Revert Razor, keep folders (Recommended)". | Razor as an option next to the Liquid engine, for an email whose html is fixed at build time. A host then has two kinds of templates, and the code is more to maintain. Or a revert of all of phase 22, also of the company folders. |
| D205 | Package of the Liquid engine | Confirmed | `src/MsgHatch.Email.Liquid` references `Fluid.Core` 2.41.1 (MIT), and its test project is `MsgHatch.Email.Liquid.Tests.Unit`. So only a host with the Liquid engine gets Fluid, Parlot (BSD-3-Clause), and TimeZoneConverter (MIT). `MsgHatch.Channels.Email` makes its template selector public as `TemplateSelector`, so the lookups of D123 with the company folders of D199 are the same for each engine. The owner: "New package (Recommended)". | The engine in `MsgHatch.Channels.Email`. It then needs no new public type, but each email host gets Fluid and its two packages. |
| D206 | Liquid engine next to the placeholder engine | Confirmed | Each payload type selects one engine: `AsEmail` the placeholder engine, or `AsLiquidEmail` the Liquid engine (D62). Existing templates and the allocation budget of the placeholder engine stay. A Liquid render allocates about 700 B, and Liquid writes a `DateTime` as `2030-01-02 15:04:05Z` unless the template uses `format_date`, so a placeholder template is not a Liquid template with the same output (checked facts of phase 23). The owner: "Liquid next to it (Recommended)". | Liquid in place of the placeholder engine, with no placeholder parser and no source generator. One engine, but a breaking change for each template with a date and for the allocation budget of section 4.11. |
| D207 | Unknown names in a Liquid template | Confirmed | A name that the model does not have, for example `{{ Nmae }}`, gives `Errors.TemplateInvalid` at render, as D5 does for a placeholder at prepare. The engine uses the `Undefined` callback of Fluid, which runs only for an unknown name and not for a property with a null value. The strict mode of Fluid 2.41.1 also fails for a null property, for example in `{% if Note %}`, so the engine does not use it (checked facts of phase 23). The owner: "Error 422 (Recommended)". | The Liquid default: a typo renders an empty text, with no error, and the email goes out with a gap. |
| D208 | Partials of a Liquid template | Confirmed | A later phase adds `render` and `include` from the template store, with the lookups of the tenant, the company, and the language. In phase 23, a template with one of them gives `Errors.TemplateInvalid` at render. In Fluid 2.41.1, a partial comes from the synchronous `IFileProvider` of `TemplateOptions`, which has no context of the request. The asynchronous `ITemplateFileProvider` exists only in the prerelease 3.0.0-beta.9. The owner: "Later phase (Recommended)". | Partials in phase 23. Each partial lookup then blocks a thread on the asynchronous template store, and each prepared template needs its own `TemplateOptions`. |
| D209 | Model of a Liquid template | Confirmed | The payload is the model of the template, so a template reads a property with `{{ Name }}`, as a placeholder template does, and a payload type needs no `[PlaceholderPayload]`. Fluid reads the members of the model type with no registration. The engine also registers, one time for each payload type, each class or record type that the public properties of the payload reach, also as the element type of a collection, so that a loop over the lines of an order works. It does not register a type of the .NET runtime. Member names are case-sensitive (checked facts of phase 23). | A registration in code for each type of a template. Or the unsafe member access of Fluid, which lets a template read each member of each object. A copied template then reads more than the payload. |
| D210 | Render of a Liquid template | Confirmed | The html part renders with `HtmlEncoder.Create(UnicodeRanges.All)`, as the placeholder renderer does, and the subject and the text with `NullEncoder.Default`. Each render has the culture of the route, the time zone UTC, and the `now` of the `TimeProvider` (section 4.14). A render stops after 100,000 steps, so a copied template with an endless loop gives `Errors.TemplateInvalid`. A parse error gives `Errors.TemplateInvalid` at prepare. Each `TemplateKey` is parsed one time for the life of the process, as `ParsedTemplateCache` does. An exception of the payload code reaches the caller, and the boundary of section 4.12 changes it into `Errors.Exception`. | No step limit. One copied template with an endless loop then stops the inbox processor. |
| D211 | Unknown filters in a Liquid template | Confirmed | A filter that Fluid does not know, for example `{{ Name \| upcse }}`, gives `Errors.TemplateInvalid` at render. The engine sets `TemplateOptions.StrictFilters`, so Fluid throws a `FluidException`, which the engine already turns into `Errors.TemplateInvalid` (D207). Filter names are case-sensitive. As for an unknown name, a filter in a branch that does not run is found only when the branch runs. Phase 23 has no tag yet, so the change is part of phase 23. The owner: "At render (Recommended)", and for the plan: "Add to phase 23 (Recommended)". | A check at prepare with `Fluid.Ast.AstVisitor`. It also finds a filter in a branch that does not run, but it needs about 20 lines, and the visitor of Fluid 2.41.1 does not enter the `{% liquid %}` tag, so `StrictFilters` stays as a backstop. Or the Fluid default: a typo writes the value with no filter and no error. |
| D212 | Template library | Confirmed | `MsgHatch.Templates` is a separate library of MsgHatch, as `MsgHatch.Subscriptions` is. It references only `MsgHatch.Domain` and knows no channel. It holds the template store contract and its file, embedded, and tenant stores, the template selector (D123, D199), the template engine contract (D213), the placeholder engine (D45), and the generator of the placeholder writers (D18), which its package includes as an analyzer. The owner: "It should be a separate library of MsgHatch, like the Subscriptions." | The template code stays in `MsgHatch.Channels.Email`, and another channel copies it. |
| D213 | Template engine contract | Confirmed | `ITemplateEngine<TPayload>` is a tenant service (D126) that another implementation can replace. `PrepareAsync(TemplateRequest, CancellationToken)` gives the template of one message type, language, scope, and part, and the implementation caches the parsed objects that it needs. `ITemplate<TPayload>.RenderAsync(TPayload, CultureInfo, CancellationToken)` gives the output string for one payload. Both return `ErrorOr` values (section 4.12). The owner: "With an interface general enough that it can be replaced by another implementation that can produce a template for a given scope and use caching for the parsed objects it needs for rendering the output string for a given payload instance". | `IEmailTemplateEngine<TPayload>`, whose template holds all parts of an email. |
| D214 | One text for each template | Confirmed | A template is one text: a part of a message type, for example the subject. `TemplateRequest` and `TemplateLookup` name the part and its kind (`PlainText` or `Html`). The file of a part is `{MessageType}.{Language}.{Part}.txt` or `.html`, in the folder of its company (D199), so the files of today stay: `subject.txt`, `body.html`, and `body.txt`. A store reads only the file of the requested part. Each required part falls back on its own with the lookups of D123, so a company can change only its subject, and a part with no translation takes the default language. An optional part, for example the text body of an email, has no fallback: the composer asks for it in the language and the scope where the required html body was found (`ITemplate.Lookup`), and with no such part the email has no text body. So no email mixes two languages or two companies in its bodies. | One template with all parts of a channel. It keeps one language and one company for each email, but a company cannot change only one part, and the store must know the parts of each channel. |
| D215 | Email layer on the template library | Confirmed | `MsgHatch.Channels.Email` references `MsgHatch.Templates`. The email composer prepares the parts `subject` (`PlainText`, required), `body` (`Html`, required), and `body` (`PlainText`, optional), and renders each one into `RenderedEmail`. A missing optional part gives no text body. `AsEmail(name)` keeps the placeholder engine, which needs `IPlaceholderPayload<TPayload>`. `AsEmail(name, ITemplateEngineFactory engine)` selects another engine. `IEmailTemplateEngine`, `IEmailTemplate`, and `EmailTemplateRequest` are removed. The template setup methods (`UseTemplateStore`, `UseEmbeddedTemplates`, `UseFileTemplates`, `UseTenantTemplateStore`) move to `MsgHatch.Templates`. | A bridge package `MsgHatch.Email.Templates`, so that `MsgHatch.Channels.Email` does not reference the library. An email host then needs three packages. |
| D216 | Selection of an engine | Confirmed | `ITemplateEngineFactory` has the method `Create<TPayload>(IServiceProvider, TenantKey)` with no constraint, so one object selects an engine for each payload type. C# cannot infer the type argument of a generic method group from its return type (CS0411), so a factory object is necessary. The placeholder engine cannot implement this method, because its writer needs a constraint (CS0425), so it stays the engine of `AsEmail(name)`. `MsgHatch.Email.Liquid` becomes `MsgHatch.Templates.Liquid` with no channel reference, and `LiquidTemplates.Engine` is its factory: `p.AsEmail("OrderShipped", LiquidTemplates.Engine)`. `AsLiquidEmail` is removed. Two registrations of one payload type with two factories fail the startup validation (D62). | `AsLiquidEmail` in a thin package for each channel and engine. |
| D217 | HTTP API of the templates | Confirmed | Phase 24 gives only the C# API. An HTTP API to list, read, and write templates is an item of "Later phases", as the package `MsgHatch.Templates.AspNetCore`, in the same way as `MsgHatch.Subscriptions.AspNetCore`. | The HTTP API in phase 24. The phase then becomes much larger. |
| D218 | Writer of a template store | Confirmed | `ITemplateWriter` lists, saves, and deletes the templates of one store. It is apart from `ITemplateStore`, so a read-only store needs no change. A store is writable when it implements both interfaces. `FileSystemTemplateStore` implements it, as the shared store and as the store of each tenant. The embedded store stays read-only, because the build fixes its resources. A database template store is a later item. The owner: "ITemplateWriter, file (Recommended)". | The write methods on `ITemplateStore`. Each custom store then adds three methods. Or template stores in SQLite, EF Core, and MongoDB in this phase, which makes the phase about three times larger. |
| D219 | Check of a written template | Confirmed | `ITemplateEngine<TPayload>.CheckAsync(TemplateLookup, string, CancellationToken)` checks the text of one part before a write, with no store read and no cache entry. The placeholder engine parses and binds the text, so a syntax error and an unknown placeholder give `Errors.TemplateInvalid` (D5). The Liquid engine parses the text, and walks the parsed tree with `Fluid.Ast.AstVisitor`. So an unknown filter (D211) and a `render` or `include` tag (D208) also give the error. A check has no payload, so an unknown Liquid name (D207) gives the error only at render. A text with a `{% liquid %}` tag gives the error (D226). The owner: "CheckAsync + Liquid tree walk". | A Liquid check with a parse only. Less code, but an unknown filter then passes the write. Or an optional `ITemplateChecker<TPayload>`. A custom engine with no checker then lets each text through. |
| D220 | Parts of a write | Confirmed | A channel declares the parts that it reads when it registers its engine. `AddTemplateEngine` takes the message type from its payload builder, and a list of `TemplatePart(Name, Kind)`. The email channel declares `subject` (`PlainText`), `body` (`Html`), and `body` (`PlainText`). A save of another part or kind gives `Errors.ValidationFailed`, so a typo never makes a file that no send reads. A delete accepts each part, so an operator can remove an old file. The owner: "Declared parts (Recommended)". | Each valid part name. Less code, but a typo then makes a file that no send reads, with no error. |
| D221 | Template store of the example app | Confirmed | The build of the example app copies its `Templates` folder to the output folder. The app uses `UseFileTemplates` on that copy, so its template endpoints can list, read, write, and delete. `MsgHatch:FileTemplates:RootPath` can set another folder. An example test that writes a template sets a copy in a temporary folder there, so a test never changes the build output. The owner: "File store (Recommended)". | The embedded store. A write then gives 400, so the example shows no write. |
| D222 | Template manager | Confirmed | `ITemplateManager` in `MsgHatch.Templates.Management` is the C# API of the template administration, as `ISubscriptionManager` is for the subscriptions (D53). It is a tenant service (D126). The manager of `TenantKey.None` uses the shared store, and the manager of a tenant uses only the store of that tenant. It validates each request, and checks a text with the engine of each registration of the message type (D219). It calls the writer only after all checks pass. The HTTP endpoints only map a request to it. | The checks in the HTTP package. A host with no HTTP then writes with no check. |
| D223 | Version of a write | Confirmed | The parse caches key on `TemplateKey`, so each save gives a new version. The file store writes a temporary file in the same folder, and moves it over the file, so a reader never reads half a text. When the new last write time is not later than the version before the save, the store sets the time one tick after that version. A save takes the expected version, or 0 for a new file. Another version gives `Errors.Conflict`, as the revision of D79 does. One lock for each store makes the check and the move atomic in one process. | The last write time of the file system only. On a file system with a coarse time, two saves in one second then keep one key, and the old parsed template stays in use. Or the last write wins. One operator then overwrites the change of another with no error. |
| D224 | HTTP endpoints of the templates | Confirmed | `MapMsgHatchTemplates(prefix = "/templates")` in `MsgHatch.Templates.AspNetCore` maps a list, and a read, a save (`PUT`), and a delete of one part at `{prefix}/{messageType}/{language}/{part}/{kind}`, with the optional query parameters `companyId` and `tenantId`. Like the subscription endpoints, it has its own source-generated JSON context, gives 415 for a body that is not JSON (D156), and gives a `ProblemDetails` body. `Errors.TemplateInvalid` gives 422. A response never has the template id, because the id of the file store is a path on the host. | The file name as the path, for example `/templates/42/A.hu.subject.txt`. This ties the HTTP API to the layout of the file store. |
| D225 | Template policy of the example app | Confirmed | The example app puts the policy `TemplateAdmin`, with the role of its name, on the group of `MapMsgHatchTemplates`, as D165 and D170 do. `dotnet user-jwts create --name 1 --role TemplateAdmin` makes a token for it. | The policy `RegistryAdmin` for the templates too. Fewer roles, but a person who changes the registry then also changes each email text. |
| D226 | The `{% liquid %}` tag | Confirmed | The Liquid engine rejects each text with a `{% liquid` tag, also with `{%-` or with no space, as a parse error. So the tag gives `Errors.TemplateInvalid` at prepare, and 422 at the check of a write (D219). With the parser of the engine, Fluid 2.41.1 parses `a{% liquid %}b` and `a{% liquid echo Name %}{{ Name }}b` with no error, and drops all text after the tag. With the option `AllowLiquidTag`, the statements of the tag run, but an empty tag still drops the text after it (checked facts after phase 25). The engine finds the tag in the text before the parse, so it also rejects a tag in `raw` or `comment`. Each statement of the tag has a tag of its own, for example `{% assign %}` or `{% echo %}`. The owner: "fix ... the open choice". | `AllowLiquidTag`, and a walk into the tag. The tag then runs, and the check finds a filter or a `render` tag in it, but an empty tag still drops the text after it with no error. Or no change: a template with the tag then sends a part with no text after the tag, and no error. |
| D227 | Packages of the HTTP APIs | Confirmed | Each management API has a contracts package and a client package. `MsgHatch.Api.Contracts` references `MsgHatch.Domain`, and holds the error metadata of all APIs (D233). `MsgHatch.Subscriptions.Api.Contracts` references `MsgHatch.Subscriptions` and `MsgHatch.Api.Contracts`. `MsgHatch.Templates.Api.Contracts` references `MsgHatch.Templates` and `MsgHatch.Api.Contracts` (D228). The `.AspNetCore` package of each API and its client package, `MsgHatch.Subscriptions.Api.Client` or `MsgHatch.Templates.Api.Client`, reference its contracts package, so the server and the client use one set of types. A client package has only the package `Microsoft.Extensions.Http`, and no ASP.NET Core reference, so a worker, a console host, or Blazor WebAssembly can use it. A source generator writes its HTTP calls (D236). The owner: "Use a shared Api.Contracts NuGet package referenced by both the API and the generated API client", and for the layout: "Per API + shared base (Recommended)". | One package `MsgHatch.Api.Contracts` for the bodies of both APIs. It then references both domain packages, so each client host gets both. Or the bodies in the core packages, which then hold the route paths of an HTTP API. Or one client package for both APIs. |
| D228 | Contract of the HTTP APIs | Confirmed | The bodies of the subscription and template endpoints, their source-generated JSON contexts, and `SettingsJson` move from the `.AspNetCore` packages into the contracts packages of D227. Each body record gets public mappings in both directions, for example `SubscriptionResponse.ToSubscription(IContentTypeRegistry)`. A contracts package holds no HTTP concern and no exception: no route path, no status code, no `HttpClient`, and no ASP.NET Core type. The error codes are the types of `Errors` in `MsgHatch.Domain.Failures`, which each contracts package references (D238). The route paths of each API are one internal source file in `src/Shared`, which the `.AspNetCore` package and the client package both compile, as each source project compiles `src/GeneratedCodeSuppressions.cs`. So a change of a body or of a route changes both sides in one commit. `RequestValues` and `Caller` stay on the server, because they check the input from outside and hold the rules of `/me` (D154). The owner: "Api.Contracts contains request/response DTOs, error code definitions, and shared error metadata contracts, but does not contain HTTP-specific concerns or exceptions." | Public route constants in a contracts package, which puts an HTTP concern there. Or copies of the records in the client, which give two sources. |
| D229 | Settings of an unknown channel in a client | Confirmed | A client reads the `settings` of a subscription with the entry `Settings:{channel}` of the content type registry of its host. When the host registers no settings type for the channel, the client gives a public `JsonChannelSettings(JsonElement Json)` of `MsgHatch.Subscriptions.Api.Contracts`, and an update sends that JSON back with no change. `ChannelDescriptor.SettingsType` is then `typeof(ChannelSettings)`. The owner: "Accept all with the only change in A5". | `Errors.ValidationFailed` for that subscription, so a list fails because of one unknown channel. Or each client host must register the settings type of each channel. |
| D230 | A host with only API clients | Confirmed | `MsgHatchRoles.None` becomes valid for a host that registers no message type. Such a host only calls the API clients, so it needs no router, no transport, and no store. The setup check gives an error for `None` with a message type. Today the validation of `MsgHatchOptions` rejects `None` (D105). The owner: "Accept all with the only change in A5". | A new role `Client`. Or the host sets `Consumer` and registers no message type, but the role name is then not true. |
| D231 | Query and command interfaces | Confirmed | Each management API has a query interface and a command interface, with the current signatures. `ISubscriptionQueries` has `GetChannelsAsync`, `GetMessageTypesAsync`, `GetSubscriptionsAsync`, and `GetSubscriptionAsync`. `ISubscriptionCommands` has `CreateAsync`, `UpdateAsync`, and `DeleteAsync`. `ITemplateQueries` has `GetTemplatesAsync` and `GetTemplateAsync`. `ITemplateCommands` has `SaveTemplateAsync` and `DeleteTemplateAsync`. `ISubscriptionManager` and `ITemplateManager` go away, and `SubscriptionManager` and `TemplateManager` implement both interfaces of their package. `IRoutingVerifier` stays, and goes with the queries. `UseSubscriptions` and each template setup method register the in-process implementations as the defaults. A selection replaces the default of one interface: `UseSubscriptionQueries`, `UseSubscriptionCommands`, `UseRoutingVerifier`, `UseTemplateQueries`, or `UseTemplateCommands`, each with a factory of `(IServiceProvider, TenantKey)`. Each selection adds the logging wrapper of its package, and calls `Select`, so two selections of one interface give a setup error. So a host can write in process and read over HTTP, or the reverse, or use one implementation for both. The interfaces keep the return types `ValueTask<ErrorOr<T>>` and `ValueTask<Error?>`, so the in-process implementation and the HTTP client give one programming model (D236). The owner: "I prefer the design as having separate domain service interfaces for read operations and write ones". | One interface with a flags enum `ManagerOperations` for the reads and the writes. This needs a composed manager at resolution. Or `ISubscriptionManager` and `ITemplateManager` as a combination of both interfaces, which a host with two implementations cannot resolve. Or the names `…Reader` and `…Writer`, which clash with `ITemplateWriter` (D218). |
| D232 | Routing verification over HTTP | Confirmed | The subscription client also implements `IRoutingVerifier` with `POST {prefix}/verification` (D163). It goes with the queries, because a verification changes no stored subscription. Phase 27 checks this first. The owner: "Accept all with the only change in A5". | A later phase. |
| D233 | Errors of the HTTP APIs | Confirmed | An endpoint writes each `Error` as problem details (RFC 9457), with `title` as the name, `detail` as the message, and the extension member `errorType` with `code`, `name`, and `isTransient`. The record `ErrorTypeBody(int Code, string Name, bool IsTransient)` of `MsgHatch.Api.Contracts` is the wire form of `errorType`, with its source-generated JSON. The status table and the rules of this row are the internal class `ErrorProblems` in `src/Shared/ErrorProblems.cs`, which the `.AspNetCore` packages of the management APIs and the client packages compile. So no public type has an HTTP status, and the domain gets no ASP.NET Core reference. The error codes stay in `Errors` of `MsgHatch.Domain.Failures`. A code equals the HTTP status where one fits, but the domain uses it only as a number, and only `ErrorProblems` turns a type into a status, or a status into a type. The status of `ValidationFailed` is 400, `Rejected` 403, `NotFound` 404, `Conflict` 409, `Expired` 410, `TemplateInvalid` 422, `RateLimited` 429, `Unavailable` 503, and each other type 500. A client reads `errorType`, and gives an error with the type of that code (D238) and `detail` as the message. So its error equals the error of the in-process implementation, also for an error type of the consumer. A body with no `errorType` comes from the host, a proxy, or the framework. Then 401 and 403 give `Rejected`, 429 gives `RateLimited`, 408, 502, 503, and 504 give `Unavailable`, and each other status gives `Exception`. So a 404 of a wrong route does not give `NotFound`. An `HttpRequestException` or a timeout gives `Unavailable`. A cancelled call gives `Errors.OperationCancelled`, as the EF Core and MongoDB stores do. A success body that does not parse gives `SerializationFailed`. The server never sends `Error.Exception`. The owner: "Api.Client should handle errors exactly the same way as the in-process implementation". | The current table: 400, 404, 409, 422, and 500 for each other type, with the name only in `title`. A client then cannot tell `Unavailable` from a crash, or a problem of the library from a problem of the framework. |
| D234 | Validation before an HTTP call | Confirmed | The checks that need no state move from `SubscriptionManager` and `TemplateManager` into the public rule classes `SubscriptionDraftRules` and `TemplateLookupRules` of the core packages. Each implementation runs these rules first. Then it runs the checks that need the registry, the directory, the store, the engines, or the culture data of the machine. So for each input, the in-process implementation and the client give the same first error, with the same message. A client gives the error of a rule with no HTTP call. The server runs all rules again. The check of a language stays on the server, because Blazor WebAssembly loads the culture data of some languages only. The owner: "Api.Client libs should do basic input validation client-side before any HTTP calls." | A copy of the rules in the client, which gives two sources. Or no check in the client, so each bad input costs a call. |
| D235 | Admin UI of the subscriptions and the templates | Confirmed | A Blazor admin UI uses the API clients for all writes. So the API host runs the validation, the engine checks, and the cache invalidation in one place. It also uses them for the reads, so that it reads its own writes, and needs no store credentials and no message type registration. An in-process read in the UI host uses the local cache level of that host, and a write in the API host does not invalidate it (section 6). The hosts that run the router keep their own cache level, so they need a short expiration or a FusionCache backplane. One setup serves Blazor WebAssembly and Interactive Server. The pages of D239 inject only the query and command interfaces, so the host makes this choice in its setup. The owner: "Setup the Admin UI to use HTTP for all writes to make cache invalidation easy if all writes go through the API." | In-process reads in the UI host, only with no cache or with a FusionCache backplane. |
| D236 | Generated HTTP clients | Confirmed | `MsgHatch.SourceGeneration` gets a second incremental generator. Its input is an internal interface in a client package. An attribute on the interface names the JSON context of the contracts package, and an attribute on each method gives the HTTP method and a route of `src/Shared`. A method takes route values, query values, at most one body, and a `CancellationToken`, and returns `Task<ErrorOr<TResponse>>` or `Task<Error?>`. The generator writes an internal sealed class on a typed `HttpClient`. It escapes each path segment, writes each body and reads each success body with the `JsonTypeInfo` of the context, and turns each failure into an `Error` with `ErrorProblems` (D233). So no method throws for an error status, a network error, or a timeout. The generator adds its attributes as internal types, so they are no public API, and it gives an error diagnostic for a method that it cannot write. It uses no reflection at run time and no proxy (D237). Hand-written internal adapters, for example `HttpTemplateQueries`, implement the interfaces of D231. Each one runs the rules of D234, maps the domain values to the bodies and back (D228), and calls the generated class. So the code of a consumer sees only the interfaces of D231, the contract types, and `ErrorOr`, and no HTTP type. The owner: "Generated API client methods return Task<ErrorOr<T>> instead of throwing exceptions for non-success HTTP responses", and "Own source generator (Recommended)". | Refit with `ApiResponse<T>` and adapters. Refit builds each request with reflection at run time, and throws `ApiException` for an error status by default. Or a client from an OpenAPI document, for example with Kiota or NSwag. The library has no host that writes the document, and such a client has its own model types. Or hand-written typed clients, which are not generated. |
| D237 | Native AOT and trimming | Confirmed | The packages of the API boundary set `IsAotCompatible`: `MsgHatch.Domain`, `MsgHatch.Subscriptions`, `MsgHatch.Templates`, the three contracts packages, the two client packages, `MsgHatch.Subscriptions.AspNetCore`, `MsgHatch.Templates.AspNetCore`, and the admin UI packages of D239 and D242. So the trim, AOT, and single-file analyzers run in each build, and each warning is an error. Each JSON read and write uses a source-generated `JsonSerializerContext`. The endpoints use the request delegate generator (`EnableRequestDelegateGenerator`), so a minimal API needs no code generation at run time. No code of these packages uses `MakeGenericType`, `Activator.CreateInstance`, an assembly scan, or a dynamic proxy. Today `MsgHatch.Domain` calls `MakeGenericType` in 4 places, and phase 26 replaces them. The other packages keep their state, because some of their dependencies, for example Fluid, WolverineFx, and the MongoDB driver, are not AOT-compatible. A later phase checks each of them. The owner: "The design is fully Native AOT and trimming friendly, using source-generated serialization (System.Text.Json source generators) and avoiding runtime reflection/proxy generation", and "API boundary now, rest later (Recommended)". | All packages in phase 26, which first needs exemptions or replacements of their dependencies. Or no AOT rule: a Blazor WebAssembly host then trims code that a reflection call needs, with no warning at build time. |
| D238 | Identity of an error | Confirmed | The code of an `ErrorType` is its identity. `ErrorType` compares and hashes by `Code` only, and its name and transient flag are data about the code. `Errors.Find(int code)` gives the type of a code of `Errors`, or null. A client gives the type of `Errors.Find` for a known code, so `error.Type == Errors.NotFound` is true, and the name and the flag come from the local definition. Only an unknown code, for example of an error type of the consumer, gives `new ErrorType(code, name, isTransient)` from the wire. No code compares an exception type or the text of a message, and a test asserts the code (section 5.5). The owner: "Error identity is based on stable shared error codes, not exception types or error messages." | The record equality of all three values. A client of another library version, with another name for one code, then gives a type that does not equal the local definition. |
| D239 | Admin UI packages | Confirmed | `MsgHatch.Subscriptions.AdminUI` and `MsgHatch.Templates.AdminUI` are Razor class libraries with the list and edit pages of their API. Each one references its contracts package and `MsgHatch.AdminUI`, a Razor class library with the parts that both use: the list layout with the states loading, error, and empty, the pager, the delete dialog, and the error text of each error type. The pages inject only the query and command interfaces of D231. So a host selects the HTTP clients or the in-process implementations in its setup (D235). A page handles each failure as an `ErrorOr` value: `Conflict` loads the stored version again, `Rejected` shows that the signed-in user has no permission, `Unavailable` shows that the API is not available, and each other error shows its message next to the form. A library page sets no render mode, so the host selects Interactive Server or WebAssembly. The pages use the CSS classes of Bootstrap 5, which the host loads, and no component library. A page keeps a draft of its edit, shows the pending changes, and asks before a delete. Each library has public route builders, so that a host can link to a page. The library adds no authorization, and the host adds it, as for the endpoints (D224). `examples/AdminUiExample` is an Interactive Server host that calls the APIs of `ExampleApp` through the clients. The text of a template has the editor of D240. The owner: "New phase 28 with RCLs (Recommended)". | No Blazor project, so each consumer writes the same pages. Or a component library, for example MudBlazor or FluentUI, which adds a dependency and its look to each host. |
| D240 | Editor of a template text | Confirmed | The edit page of `MsgHatch.Templates.AdminUI` knows no template engine. It shows the text of a part with the editor of the engine that reads the part. An editor is a component that derives from `TemplateEditorBase` of `MsgHatch.Templates.AdminUI`, with the parameters `Lookup`, `Text`, `TextChanged`, `Issues`, and `Disabled`. A package registers it for an engine name with `AddTemplateEditor<TEditor>(string engine)`, and the page shows it with `DynamicComponent`. The type parameter has `DynamicallyAccessedMembers`, so the trimming keeps each registered editor (D237). `ITemplateEngineFactory` gets `string Name`: `placeholder` for the placeholder engine, and `liquid` for `LiquidTemplates.Engine`. `ITemplateQueries.GetTemplatePartsAsync` gives each declared part of a message type (D220) with the engine names of the registrations that read it, and `GET {prefix}/parts` maps it. A part with one engine and a registered editor gets that editor. Each other part gets the plain editor of `MsgHatch.Templates.AdminUI`, a text area. The owner: "I want the AdminUI be a modular UI that can host the templating engine specific editor input with its validations and syntax highlighting without making the whole admin UI tightly coupled for one templating engine." | An editor for each engine in `MsgHatch.Templates.AdminUI`, which then references each engine. Or a map of the message types to the editors in the setup of the host, which a host with only API clients cannot fill, because it registers no message type (D230). |
| D241 | Check of a draft | Confirmed | `ITemplateQueries.CheckTemplateAsync(TemplateLookup lookup, string text, CancellationToken ct)` checks a draft with the engine of each registration that reads the part, and saves nothing. It gives `ErrorOr<IReadOnlyList<TemplateIssue>>`. `TemplateIssue(string Message, int? Line, int? Column)` is one problem of the text, and an empty list means that the text passes. An error means that the check did not run, for example `ValidationFailed` for a lookup or `Unavailable`. So `ITemplateEngine<TPayload>.CheckAsync` gives the same type in place of `Error?` (D219), and a save with an issue gives `Errors.TemplateInvalid` with the message of the first issue, as today. The Liquid engine gives the line and the column of a parse error of Fluid, and no position for an unknown filter or a `render` tag. `POST {prefix}/{messageType}/{language}/{part}/{kind}/check` maps the check, with the text as the body. It is a query, because it changes nothing. The owner: "Confirm D241". | The check with `Error?` only, so an editor cannot mark the position of a problem. Or a parse in the browser, which needs Fluid in Blazor WebAssembly (D237), and does not know the filters of the host. |
| D242 | Liquid editor | Confirmed | `MsgHatch.Templates.Liquid.AdminUI` is a Razor class library with `LiquidTemplateEditor`, which `AddLiquidTemplateEditor()` registers for the engine `liquid` (D240). It references `MsgHatch.Templates.AdminUI`, and not `MsgHatch.Templates.Liquid`, so a WebAssembly host loads no Fluid code. The editor puts the text area over a highlighted copy of the text. The tokenizer `LiquidTokenizer` of the package gives the spans of the plain text, of `{{ }}` and `{% %}` with their `-` marks, of the tag names, of the filter names after `\|`, of the strings and numbers, and of `comment` and `raw`. A small JavaScript module of the package keeps the scroll of the two layers in step. With no call, the editor marks an open `{{` or `{%` and a `{% liquid %}` tag (D226). After the user stops typing for 500 ms, and before a save, it calls `CheckTemplateAsync` (D241). It marks the line and the column of each issue, and shows each message under the text. So the check of the server stays the only full check, with the filters of the host. The owner: "Add a separate Admin UI components project for the Liquid templating engine specific text editor with syntax highlighting and validation." | A code editor in JavaScript, for example CodeMirror 6 with its Liquid language, which needs an npm build in the repository. Or the editor in `MsgHatch.Templates.Liquid`, which then references ASP.NET Core components, and puts Fluid into each admin host. |
| D243 | Position of a Liquid parse error | Recommended | Fluid 2.41.1 gives a parse error only as text. So the Liquid engine reads the line and the column from the text ` at (line:column)` at the end of the first line of the message of Fluid, and a message with no such text gives an issue with no position (D241). A test row for each kind of parse error pins the format, so an update of Fluid that changes it fails a test. Reason: the grammar of `FluidParser` gives a Parlot `ParseError` with a position, but an unknown tag, for example `{% endif %}` with no `if`, throws `Fluid.ParseException` with only a message (checked facts of phase 26). | A second parse with the Parlot grammar after a failed parse, which still gives no position for an unknown tag. Or no position for a parse error, which breaks D241. |
| D244 | Logging wrappers of the template queries and commands | Recommended | `TemplateQueriesLoggingWrapper` and `TemplateCommandsLoggingWrapper` wrap the in-process default and each selection of `ITemplateQueries` and `ITemplateCommands`, with the event ids 2017 to 2028, as the subscription queries and commands have their wrappers. Reason: phase 26 needs a logging wrapper for each selection (D231), and the HTTP clients of phase 27 log each call only through these wrappers. One rule for the default and the selection keeps the setup simple. Section 4.22 said before that the template manager has no logging wrapper of its own, because the store and engine wrappers log each change and each check. | A wrapper only for a selection, so the default logs no call of its own. Or no wrapper, which breaks the selection rule of D231. |
| D245 | Router factory and message shapes for AOT | Recommended | `MsgHatchBuilder.UseRouter` takes an `IOpenRouterFactory` in place of an open generic `Type`. Its generic method `Create<TEvent>(tenantServices, key)` makes the router of one event type, and `UseSubscriptions` gives the internal factory of `SubscriptionRouter<TEvent>`. The content type entries of messages come from the public factories `ContentTypeEntry.ForReceivedMessage<TEvent>` and `ForOutgoingMessage<TEnvelope>`, which give each entry an internal shape with the typed code that `MsgHatchJson` needs. Reason: D237 removes the 4 `MakeGenericType` calls of `MsgHatch.Domain`, and the type of an open router and the closed type of an entry are known only as `Type` objects at run time, so only code that knows the type parameter at compile time can replace them. This changes the `Type` form of `UseRouter` of D99, which is Recommended. | Annotations with `RequiresDynamicCode`, which move the AOT warnings to each caller, for example `UseSubscriptions`. Or a router type and a factory delegate together, which keeps two ways to select one router. |
| D246 | Language rule of a template lookup | Recommended | `TemplateLookupRules.Check(TemplateLookup)` also gives `Errors.ValidationFailed` for a null or empty language, with the message of the rule (D234). The check of the culture of a language stays on the server. Reason: the template routes have the language as a path segment (D224), and an HTTP route cannot hold an empty segment, so the template API client cannot send such a lookup. With no rule, the client gives `Errors.Exception` for the 404 of the framework, and the in-process implementation gives `Errors.ValidationFailed`, which breaks D233. A check for an empty language needs no state, so it is a rule of D234. `TemplatePartResponse.ToPartInfo` checks a part with the language `und`, because a declared part has no language. | A check in the client only, which gives two sources of one message. Or the language as a query value, which changes the route of D224. |
| D247 | Settings type of a subscription request | Recommended | `SubscriptionRequest` gets the optional field `settingsType`, the content type of the settings, for example `Settings:Email`. `SubscriptionRequest.From` writes the content type of the newest settings entry of the type of the settings, and null for `JsonChannelSettings`. The server reads the settings with that settings type, so the manager sees the settings of the type of another channel, and gives its own error in its own order. A `settingsType` that is not a registered content type `Settings:{channel}` gives `Errors.ValidationFailed`. With no `settingsType`, the server reads the settings with the newest settings type of the channel, as before. Reason: the manager checks that the settings type is the settings type of the channel (rule 2.3.4). The JSON of the settings has no type, so with no `settingsType` the server reads the settings of another channel as the settings of the channel, and can store wrong settings. Then the HTTP client gives another result than the in-process implementation, which breaks D233 and D234. | A check of the settings type in the client with the registry of the client host, which gives the error before the checks of the server, so an input with two problems gives another first error. Or a JSON discriminator in each settings type, which changes the JSON of each channel package. |
| D248 | Rules of a routing verification request | Recommended | `RoutingVerificationRules.Check(RoutingVerificationRequest)` in `MsgHatch.Subscriptions.Verification` gives `Errors.ValidationFailed` for no list of test users, no list of languages, or a language that is null, empty, or white space. The in-process verifier and the HTTP client run it first, as the managers run the rules of D234. The endpoint keeps its check of the body. Reason: the endpoint of the verification rejects an empty language, but the in-process verifier accepted it, so the HTTP client gave `ValidationFailed` and the in-process verifier gave a report for the same input, which breaks D233 and D234. | The check only on the server, which keeps the difference. Or a check only in the in-process verifier, so the client sends a request that the rule rejects. |
| D249 | Tenant of the admin pages | Recommended | Each admin page resolves the query and command interfaces of `TenantKey.None` through the `IServiceProvider` of its DI scope (section 4.24). A host with a store for each tenant uses one admin host for each tenant, or registers the services of `TenantKey.None` in its own way. Reason: phase 28 has no item for a tenant selection, and the attribute `[Inject]` takes only a constant key, which a `TenantKey` is not. A tenant selection on each page needs the tenant in each address of the route builders and in each link. | A query parameter `tenantId` on each page and in each route builder. Or a scoped service of the host that gives the tenant key of the circuit. |
| D250 | Trimming of the admin pages | Recommended | Each setup method of an admin UI package has a `DynamicDependency` with all members of each page of the package, and `AddMsgHatchTemplatesAdminUI` also of `PlainTemplateEditor`. The template edit page suppresses the trim warning IL2111 of its `DynamicComponent`, with the reason. Reason: the checks of phase 28 showed that a Razor class library with `IsAotCompatible` gives IL2111 for each `DynamicComponent`, because the generic `OpenComponent<DynamicComponent>` keeps all members of `DynamicComponent`, and its setter of `Type` has `DynamicallyAccessedMembers`. A WebAssembly host with `PublishTrimmed` and the default `TrimMode` `partial` trims each assembly with `IsTrimmable`, which `IsAotCompatible` sets, so it removed the pages that only the router finds, and its router gave "Not found". With the `DynamicDependency` of the setup method, the trimmed host showed the registered editor and the plain editor through `DynamicComponent`, with no trim warning. The editor types come only from the plain editor and from `AddTemplateEditor`, whose type parameter keeps all members (D240), so the suppression is safe. | Each host adds `TrimmerRootAssembly` for each admin package, which each host must know. Or the edit page renders the editor with a `RenderFragment` that `AddTemplateEditor` makes, with no `DynamicComponent`, which changes D240. |
| D251 | Check of an editor before a save | Recommended | `TemplateEditorBase` has the virtual method `CanSaveAsync`, which gives true by default. The edit page calls it on the editor of the part before each save, and saves only when it gives true. The Liquid editor checks the text with `CheckTemplateAsync` there, shows the issues, gives false for a text with an issue, and gives true when the check did not run, so that the save gives its error. The protected method `TemplateEditorBase.CheckAsync` calls `CheckTemplateAsync` of `TenantKey.None` for the lookup of the editor. Reason: D242 says that the Liquid editor checks the text before a save, but the save button is on the edit page, and the page knows no engine (D240). | The edit page calls `CheckTemplateAsync` before each save, for each editor. The page then holds a check that D242 gives to the editor, and a plain editor costs a call before each save. |
| D252 | HTML highlight of a template text | Confirmed | `MsgHatch.Templates.AdminUI` has the public `HtmlTokenizer`, which gives a span for each part of an HTML text: `Text`, `Markup` (`<`, `</`, `<!`, `>`, `/>`, and `=`), `TagName`, `AttributeName`, `AttributeValue` with its quotes, `Comment`, and `Entity`. It is no parser. The public component `HighlightedTextArea` puts the text area over a `pre` with the highlighted segments of the text, and its JavaScript module keeps the scroll of the two layers in step. The plain editor shows an `html` part of each engine in `HighlightedTextArea` with the HTML spans, and a `txt` part in a text area. The Liquid editor keeps its Liquid spans, and for an `html` part it gives the characters of each Liquid `Text` span their HTML span kinds. It runs `HtmlTokenizer` over the full text with each character of the Liquid code replaced by `x`, so `href="{{ ResetLink }}"` stays one attribute value, and a `<` in Liquid code starts no tag. Reason: the kind of a part decides whether its text is HTML, not its engine, and each editor gets the kind in `Lookup.Kind`. So one HTML layer serves each engine, and an engine package adds only its own tokens (D240). The owner: "I see no syntax highlighted HTML email template editor in the UI for any of the html templates", and then "Shared HTML layer". | An HTML highlight only in the Liquid editor, so a part of the placeholder engine stays a plain text area. Or an editor for the placeholder engine with its placeholders and the HTML, which is more work for the same HTML layer. |
| D253 | Engine of the password reset email of the example app | Confirmed | `examples/ExampleApp` renders the email of the message type F with the Liquid engine, with `AsEmail("FEmail", LiquidTemplates.Engine)`, and `PasswordResetEmail` has no `[PlaceholderPayload]` (D209). The templates `F.*` stay, because `{{ResetLink}}` is also a Liquid output. The emails of A to E keep the placeholder engine. Reason: with only the placeholder engine, the admin UI of the example never showed the Liquid editor. One app with emails of both engines shows both editors. The owner: "One email in Liquid". | All six emails in Liquid, so the example shows no placeholder engine. Or no change, so the example shows no Liquid editor. |
| D254 | Sample payload of a template registration | Confirmed | `WithTemplateSample<TEvent, TPayload>(this PayloadBuilder<TEvent, TPayload> payload, TPayload sample)` of `MsgHatch.Templates` stores one sample payload for the template registrations of a payload type and a message type, for example `.WithPayload<PasswordResetEmail, PasswordResetEmailLoader>(p => p.AsEmail("FEmail", LiquidTemplates.Engine).WithTemplateSample(new PasswordResetEmail("https://example.com/reset")))`. A second sample of one payload type and one message type gives a setup error (D62). `TemplatePartInfo.HasSample` tells whether a registration of a part has a sample. Reason: the engines render a typed payload (D209, D216), and the admin UI and the HTTP client know no payload type. A typed sample in the setup is AOT-safe (D237), needs no data in a store, and lets the host decide what an operator sees. The owner: "Sample in the setup". | The loader of the registration on a test event of `WithTestEvent`, which needs a test event and a test user for each message type, and reads real data. Or sample JSON in the UI, which needs a JSON contract for each payload type. |
| D255 | Render of a draft text | Confirmed | `ITemplateEngine<TPayload>` gets `RenderAsync(TemplateLookup lookup, string text, TPayload payload, CancellationToken ct)`, which gives `ErrorOr<string>`. It renders a text that is not stored, with the culture of the language of the lookup, the html part with the HTML encoder, and a text part with no encoder (D210). A text that does not parse gives `Errors.TemplateInvalid` with the first issue (D241). The placeholder engine and the Liquid engine implement it, and the logging wrapper of an engine logs each render with no text and no payload. Reason: `PrepareAsync` reads a template from the store, and a preview must show the draft of the editor. No package is published yet, so a new member costs only the two engines and the test doubles. The owner: "New engine member". | An optional interface `ITemplatePreviewEngine<TPayload>`, so an engine with no preview gives "No preview", with two interfaces for one engine. |
| D256 | Preview query of a template draft | Recommended | `ITemplateQueries.PreviewTemplateAsync(TemplateLookup lookup, string text, CancellationToken ct)` gives `ErrorOr<IReadOnlyList<TemplatePreview>>`, with `TemplatePreview(string Registration, string Text)` for each registration of the part with a sample (D254). It runs `TemplateLookupRules` first (D234), and saves nothing, so it is a query, as the check of D241 is. A part with no sample gives an empty list, and a text that does not parse gives `Errors.TemplateInvalid`. `POST {prefix}/{messageType}/{language}/{part}/{kind}/preview` maps it with the text as the body, and the HTTP client gives it behind the same interface (D231). Reason: the render needs the engine and the sample of the server, so a page calls the query, and the host selects the in-process or the HTTP implementation. One registration can differ from another in its payload, so the query gives one preview for each registration. | A render in the browser, which needs the engines in the browser (D242). Or a preview of the stored text only, which shows no draft. |
| D257 | Template pages with a preview | Confirmed | The template list has the buttons View and Edit on each row. `TemplateViewPage` at `/msghatch/templates/{messageType}/{language}/{part}/{kind}/view` shows the stored text read-only with the highlight of D252, and the preview. The edit page shows the editor and the live preview. On a wide screen, the text is on the left and the preview on the right, and on a narrow screen the preview is under the text. The edit page calls the preview 500 ms after the last change, and a new change cancels the call before it. An html preview is an `iframe` with `sandbox=""` and `srcdoc`, so no script of a template runs, and a text preview is a `pre`. Both pages show the message type, the language, the company, the part with its kind, and the version on one line. The owner: "It would be good to have a live preview of a template both in the editor and in a read-only view mode. Also it would be better to have all those Message type, Language, Company, Part, Version metadata or templates in a more compact view, consuming less vertical space.", then "Side by side", and "Template list should have 2 action buttons: View for the read-only view, and Edit for the editor." | Tabs for the text and the preview, so the text and the preview are not visible together. Or a view mode inside the edit page, with no button of its own in the list. |
| D258 | Log level of a draft that does not render | Recommended | `TemplateEngineLoggingWrapper` logs `Errors.TemplateInvalid` of the render of a draft (event 2032), and `TemplateQueriesLoggingWrapper` logs `Errors.TemplateInvalid` of a preview (event 2030), at the Information level. Each other error of a render or a preview has the level of `ErrorLogLevels.For` (section 4.15). Reason: the edit page asks for a preview 500 ms after each pause in typing (D257), so a draft with an open tag gives `TemplateInvalid` many times while an operator types. At the Error level, each pause of an operator logs an error that is no fault of the system. The check of a draft has the same exception (D219, D241). | The level of `ErrorLogLevels.For`, which logs `TemplateInvalid` at the Error level, as a save does. Or no log of a preview, which breaks the selection rule of D231 and D244. |
| D259 | Layout of the admin list pages | Confirmed | `AdminListPage` of `MsgHatch.AdminUI` is one column: a header with the title, a toolbar with the filters on the left and the actions on the right, and the list. `AdminList` puts its table in an area that fills the rest of the column and scrolls by itself, with a sticky row of column names, and puts its pager under that area. The column grows to the height that the host gives it: a host whose layout has the height of the window, with a main area of `flex-grow: 1` and `min-height: 0`, scrolls only the list. In a host with no such layout, the column has the height of its content, and the page scrolls as before. The owner: "Change list pages to scroll only the list, not the whole page". | A fixed height of the table area, which does not fit each screen. Or a scroll of the whole page, so the filters and the column names leave the screen. |
| D260 | New item in a dialog | Confirmed | Each list page with a new item has the button "+ New …" on the right of its toolbar, above the list. The button opens a dialog. The dialog of a new template has the part, the language, and the company, and opens the edit page of the lookup. The dialog of a new subscription shows `SubscriptionEditor`, the public component that also holds the form of the edit page, and a create closes the dialog and loads the list again. `SubscriptionAdminRoutes.New` gives the address of the list with the query `new=true`, which opens the dialog, so a link to it still opens the create form. The page address `/msghatch/subscriptions/new` goes away. The HTTP API does not change. Reason for one editor component: a Razor component is always public, and the form model `SubscriptionForm` is internal (checked facts of phase 30). The owner: "unify adding a new list item to a button top-right above the list", "make it a dialog", and "Keep New as list?new=true". | A form under the list, as before. Or the `/new` page next to the dialog, which keeps two ways to create. Or a public form model, which adds a type with no use for a host to the public API. |
| D261 | Buttons of the dialogs and the pages | Confirmed | `AdminActions` of `MsgHatch.AdminUI` puts Cancel or Back on the bottom left, and the main action on the bottom right, for example Save, Create, Edit, or Delete. `AdminDialog` has it as its footer, and each dialog of the admin UIs uses `AdminDialog`. The view page has Back on the left and Edit on the right. An edit page has Back on the left, and Delete and Save on the right, with Save last. The owner: "make all dialogs to use the same layout to have a cancel or back button on the bottom left and the Save or Edit button on the bottom right", and then "Pages too". | The buttons of each dialog and page in one group, as before. |
| D262 | Details in the delete question | Confirmed | `DeleteDialog` shows its question, the content `Details` that the page gives, and the text that the delete cannot be undone. A template shows its metadata line (D257), and says that a send then uses the next template of the fallback (D123), and that with no fallback a required part gives an error and an optional part is left out. A subscription shows its owner, message type, kind, channel, recipients, mandatory flag, and revision, and says that the router does not use it for the next messages. The owner: "Delete action should show a confirmation dialog with the details of what gets deleted". | A question with no details, as before, so the user confirms with no view of the item. |
| D263 | Delete from a list row | Confirmed | Each row of the template list and of the subscription list has Delete. It shows the same question as the edit page, with the details of D262, and a confirmed delete loads the list again. An error of the delete shows above the list. `TemplateDeleteDetails` and `SubscriptionDeleteDetails` hold the details, so each page shows the same text. The owner: "Add the delete action to the list pages as well, behaving the same". | A delete only on the edit page, as before. |
| D264 | Actions of an edit page on screen | Confirmed | `AdminEditPage` of `MsgHatch.AdminUI` is one column: a header, a body that scrolls by itself, and the bar of the actions under the body. The template edit page, the template view page, and `SubscriptionEditor` use it, so in a host whose main area has a fixed height (D259), the page does not scroll as a whole, and the actions stay on screen. The body of `AdminDialog` is a flex column, so the editor in a dialog keeps its bar under its form. The owner: "Editor pages (like Edit Subscription) shouldn't scroll the whole page either, to keep the action buttons always on screen". | A sticky bar at the bottom of a page that scrolls as a whole, which covers the end of the form. |
| D265 | Filter bar of a list | Confirmed | The filters of `AdminListPage` are in a bar with the background `--bs-tertiary-bg`, a border, and round corners, and each label over an input is small, uppercase, and gray. A list with no filters has no bar. The owner: "Separate the list filter inputs above the list by a border or other style", and "Add labels above the list filter inputs". | The filters with no frame, as before. |
| D266 | Dark mode | Confirmed | The admin parts use only the color variables of Bootstrap 5.3, so they follow the attribute `data-bs-theme` of the host. The host selects the mode. `AdminUiExample` sets it before the first paint from the choice in the local storage of the browser, or from the system setting, and its navigation bar has the button "Light or dark". The iframe of an html preview keeps a white background and the light color scheme, because an email renders on white. The owner: "Add the dark mode if it's not big of a task". | A switch of the mode in the admin packages, which then decide the look of each host. |
| D267 | Text of a template version | Confirmed | `ITemplateStore.RenderVersion(long version)` gives the text of a version for a user, because only the store knows the format of its versions, and each other part treats a version as an opaque number. The default is the number. The file store gives the last write time in UTC, and the embedded store "Built in". The logging wrappers give the text of the inner store. The template manager sets `VersionText` on each `TemplateEntry` and `TemplateSource` that it gives, and the HTTP bodies carry it as the optional `versionText`, so an admin UI with the HTTP client shows the text of the server store. The owner: "Add a Render or GetVersionStringForUi method to the raw version value so the owner knowing the format is responsible to format it before showing to the user". | A date made from each version in the admin UI, which is wrong for the embedded store and for a store with a counter. Or a typed version struct in place of `long`, which changes `TemplateKey` and each engine cache. |
| D268 | Registry query interface | Confirmed | `IRegistryQueries` of `MsgHatch.Domain.Management` gives the registry to a management client. `GetChannelsAsync(CancellationToken ct)` gives `ErrorOr<IReadOnlyList<RegistryChannel>>`, and `GetMessageTypesAsync(CancellationToken ct)` gives `ErrorOr<IReadOnlyList<RegistryMessageType>>`. `RegistryChannel(ChannelId Id, string Name, string? Description, ChannelFeatures Features, bool Enabled, int Revision)` and `RegistryMessageType(MessageType Type, string? DisplayName, string? Description, IReadOnlyList<ChannelId> Channels, bool Enabled, IReadOnlyList<ChannelId> DisabledChannels, int Revision)` hold the values of `IMessageRegistry` and the revision of the stored entry. The revision is 0 with no entry or no writer. `RegistryManager` implements the interface with the join that the GET routes of section 4.20 do now. The types do not use `ChannelDescriptor`, because its settings type cannot go over HTTP. The phase adds no command interface, because the pages are read-only. The owner chose "Core admin UI + registry API". | The channel list of `ISubscriptionQueries` (A1) in the subscription admin UI. It has no revision, and a host with no subscriptions does not have it. |
| D269 | Tenant of the registry queries | Recommended | The registry belongs to the whole system (D114). `IRegistryQueries` is still a tenant service of `AddDefaultTenantService`, and each key gives the same values. So the pages keep the rule of D249, and the client uses the selection methods of D231, with the setup error of a second selection and the logging wrapper of D244. The registry routes have no `tenantId`, so the client sends none. | A plain singleton, and pages that make an exception to D249. |
| D270 | Registry contracts and errors | Recommended | The six registry bodies and `RegistryEndpointJson` move from `MsgHatch.AspNetCore` to `MsgHatch.Api.Contracts`, so the bodies of one API are in one package (D228). `RegistryChannelResponse` and `RegistryMessageTypeResponse` get `From` and the mappings back to `RegistryChannel` and `RegistryMessageType`. A name that is not valid gives `Errors.SerializationFailed`. `RegistryApiRoutes` in `src/Shared` holds the route paths. Each registry route writes its errors with `ErrorProblems` (D233), so that a client reads the error type. The GET routes call `IRegistryQueries` of `TenantKey.None`. | Only the two GET bodies in the contracts package, and the request bodies in `MsgHatch.AspNetCore` until the command interface. |
| D271 | Registry API client | Recommended | The new package `MsgHatch.Api.Client` implements `IRegistryQueries` over HTTP (D227, D236). It is the client of the core APIs, next to `MsgHatch.Api.Contracts`. It has the internal `IRegistryApi` with `[HttpApi]`, the adapter `HttpRegistryQueries`, and `RegistryApiClientOptions` in the section `MsgHatch:RegistryApiClient` with the default prefix `/registry`. It also has the source-generated validator of the options, and `UseRegistryApiClient` with an overload with `Uri baseAddress`. The options and the HTTP client follow section 4.23. A query has no input, so the adapter runs no rule. | A package for each API, for example `MsgHatch.Registry.Api.Client`, which a later delivery client repeats. |
| D272 | Channel pages | Confirmed | `MsgHatch.AdminUI` gets `ChannelListPage` at `/msghatch/channels` and `ChannelViewPage` at `/msghatch/channels/{channel}`. Their route builders are `RegistryAdminRoutes.Channels` and `RegistryAdminRoutes.Channel(ChannelId)`, and the setup method is `AddMsgHatchAdminUI`. The list shows the id, the name, the features, the enabled flag, and the number of message types of each channel, with the button View. The view page shows the id, the name, the description, the features, the enabled flag, and the revision. It also lists the message types that register the channel, each with its display name and its state on the channel: "Enabled", "Message type disabled", or "Disabled on this channel". It has Back and no main action. An unknown channel shows the text of `NotFound` and the link back. The owner chose "Descriptor + message types". | The channel pages in `MsgHatch.Subscriptions.AdminUI`, with the descriptor only. |
| D273 | Message types pages | Confirmed | `MsgHatch.AdminUI` gets the read-only `MessageTypeListPage` at `/msghatch/message-types`, with the route builder `RegistryAdminRoutes.MessageTypes`. It shows each message type with its display name, its description, its enabled flag, and its channels. Each channel links to its view page, and a channel in `DisabledChannels` has the mark "disabled". The subscription page `MessageTypesPage` shows the applied subscriptions of a company or a user. So its title and its links become "Applied subscriptions". Its address, its class, and `SubscriptionAdminRoutes.MessageTypes` stay. The owner chose "Rename + core page". | The subscription page as the only page of the message types, with its old title. |
| D274 | Order of the admin pages | Confirmed | The menu and the home page of `AdminUiExample` have the order Channels, Message types, Subscriptions, Templates, and Verification. The subscription page "Applied subscriptions" goes away: `MessageTypesPage`, `SubscriptionAdminRoutes.MessageTypes`, and its link on the subscription list. The message types page of D273 shows the subscriptions of each message type in its place (D276). `ISubscriptionQueries.GetMessageTypesAsync`, its HTTP route, and its client stay, because another client can use them. This decision replaces the subscription page of D273. The owner: "change page order to: Channels, Message Types, Subscriptions, Templates and Verification". | The old order with the subscription pages first, and two pages of the message types. |
| D275 | Columns of the message types page | Recommended | `MsgHatch.AdminUI` gets `IMessageTypeColumn` and `AddMessageTypeColumn<TColumn>()`, because the core packages never reference `MsgHatch.Subscriptions`. A column has a header, and gives the items of its cell for each message type in one call. An item `MessageTypeCellItem(string Text, string? Href)` is a badge, and an item with an address is a link. The page shows the columns after its own columns, in the order of registration. An error of a column shows above the list with its header, and the other columns still show. The page resolves the registered columns, so this is an exception to D249. A column resolves only the query interfaces of `TenantKey.None`. DI makes the column, so `TColumn` has `DynamicallyAccessedMembers(PublicConstructors)`, as the rule of the AOT packages asks (D237). | A reference from `MsgHatch.AdminUI` to `MsgHatch.Subscriptions`, which breaks the rule of the core packages. Or a render fragment for each cell, which needs more API for the same badges. |
| D276 | Subscription counts of a message type | Confirmed | `MsgHatch.Subscriptions.AdminUI` registers the column "Subscriptions" of D275. It counts the stored subscriptions of each message type for each `SubscriptionLevel`, of each kind, with one call `GetSubscriptionsAsync(new SubscriptionQuery())`. Each cell shows "Default N", "Company N", and "User N", also for 0, so the badges of the rows align. The counts are of the stored subscriptions, not of the applied subscriptions of one scope, so the page needs no filter of a company or a user. Each count links to the subscription list with the filters of its message type and its level: `SubscriptionAdminRoutes.ListOf(MessageType type, SubscriptionLevel level)` gives the address with the query parameters `type` and `level`, and `SubscriptionListPage` starts with these filters. A value that is not valid gives no filter for that value. The owner: "Can we show multiple counts there, one number per each SubscriptionLevel?", and then "add the count links to the filtered subscription list". | The number of the applied subscriptions of a scope, with the filters of the company and the user from the old page. |
| D277 | Channel state history | Confirmed | The outbox dispatch store keeps the state of each channel as insert-only `ChannelState` rows (D106). A row is `Up`, or `Down` with the reason, `NextCheckAtUtc`, the error, and the note. A store adds a row only when the state changes: `SetChannelDownAsync` with the reason `Errors` only on an up channel, with `Operator` unless the channel is paused, and `SetChannelUpAsync` only on a down channel with the given reason. The current state is the latest row, and `ChannelDown` stays as its view. Each row has the version of its channel, 1, 2, 3, with a unique key on the channel and the version. So a change is one insert, and of two parallel changes only the first adds its row. The other reads the new latest row and tries again, at most three times, and then gives `Errors.Conflict`. So no store needs a transaction, also MongoDB on a standalone server. Each row keeps `PreviousKind` and `PreviousDuration`: the kind of the previous row of its channel and the time since it in whole milliseconds. So a report of the up time is `SUM(PreviousDurationMs) WHERE PreviousKind = 'Up'`, with no join. `TryStartChannelCheckAsync` updates only `NextCheckAtUtc` of the latest row. The rows stay, with no removal. `SetChannelUpAsync` gets `DateTime upAtUtc`, because a store reads no clock. `GetChannelStatesAsync(DateTime fromUtc, CancellationToken ct)` gives the rows at or after `fromUtc` and the row of each channel that was current at `fromUtc`, newest first. The tables and the collection `MsgHatchChannelDown` and the file `down.json` go away with no data migration, because the library has no release yet. The owner: "Refactor the ChannelDown in a way that we have ChannelState records, each recorded only if the state changed", "Each channel state change record should store not just the timestamp, but the duration from the previous state change event", "Keep all rows", and "no need to any data migration, the library is still in beta". | A down state row with a second table of events, which needs two writes in one transaction, so MongoDB needs a replica set. Or a removal after 30 days. |
| D278 | Channel state in the registry queries | Confirmed | `RegistryChannel` gets `ChannelState? Current`, the latest row of the channel, and `IRegistryQueries` gets `GetChannelHistoryAsync(ChannelId channel, CancellationToken ct)`. `Current` is null for a channel with no row. So one field gives the current state, its start, and the kind and the duration of the previous state, for example "up since 16:13, after 4 minutes down". The history is the rows of one channel in the window of `ChannelState.HistoryWindow`, 30 days, and the row that was current at the start of the window, newest first. The registry route `GET /channels/{channel}/history` gives the history, with the policy of the registry group (D170). So the pages need only the role `RegistryAdmin`. `ChannelDownResponse` moves to `MsgHatch.Api.Contracts`, so the registry bodies and the delivery bodies use one type. `ChannelStateResponse` has `previousKind`, and `previousDurationMs` as a number, and `RegistryChannelResponse` has `current`. The owner chose "Registry queries". | A delivery query interface with its own contracts and client, which the later delivery phase adds. The pages then also need the role `DeliveryAdmin`. |
| D279 | Channel state on the channel pages | Confirmed | The channel list gets the columns "State" and "Since", and the button "History". "State" is the badge "Up" or "Down", with the error name for `Errors`, or "Paused" and the note for `Operator`. "Since" is the start of the current state in UTC and the time since then from `TimeProvider`, for example "2026-11-18 13:32 UTC (3 d 4 h)", with the previous state and its duration, for example "after Down for 4 min", or "No down state recorded" for a channel with no row. "History" opens `AdminDialog` with a timeline bar of the last 30 days, red where the channel was down, and the list of the rows, newest first, with the duration of the previous state. The row that was current at the start of the window gives the state at the start of the bar. The bar uses the class `progress-stacked` of Bootstrap 5.3, with no JavaScript library. The channel view page shows the same "State" and "Since" fields, and has "History" as its main action on the right (D261). The list and the view page use the same parts for the state and the dialog. The owner chose "Timeline and list", and then asked to add "the channel state to the channel view page". | Only the list of the events, or only the timeline. |
| D280 | Allocations of the event path | Recommended | The pipeline keeps one result array for each event, and copies the routes at most one time (section 4.11). A first pass checks each route, writes the error of a failed route into the result array, and counts the routes of each channel. When all routes are on one channel, no route failed, and the router gave an array, the composer gets that array with no copy. Else one new array holds the routes of all channels in groups by channel, in the order of the router, with the position of each route. `IChannelRegistration.ComposeAsync` writes each outgoing message into the result array at the position of its route. The set of the targets gets the capacity of the route count, and one route needs no set. The registry lookup of a channel is a loop with no closure. The boundary and the measurement of `ProcessAsync` get their values as a state argument, with no closure, and `RouteGrouping` builds its groups with no closure. Each channel registration makes its dispatch topic one time, and the store step notifies each channel with a stored message one time. A tenant key gets its box one time, for at most 1,024 tenants, so a keyed service lookup does not box `TenantKey` again. The probe of the plan of phase 34 found 776 B for each route: the outgoing message 231 B, two copies of the routes and the list of the checked routes 212 B, the set of the targets 108 B, the closure of the registry lookup 94 B, the two result arrays 49 B, the topic string 46 B, and bucket arrays 30 B. The closure and the topic string are the 150 B that phase 13 did not attribute (D159). | Keep `RouteGrouping.ByChannel` with its copies, and add budget lines for the copies, the closures, and the boxes. Or a pool of arrays, which needs a return path through each composer. |
| D281 | Allocations of a send | Recommended | `MsgHatchMetrics` gives the tag `msghatch.outcome` from a switch of literal names, so `RecordTry` and `RecordSendDuration` box no `PublishOutcome`. `RunOnceAsync` of the dispatch loop uses `Parallel.ForAsync` over the positions of the batch in place of `Parallel.ForEachAsync` over `Enumerable.Range`. The lock of `ForEachAsync` allocates a `SemaphoreSlim` waiter for each contended item, and `ForAsync` takes the next position with no lock: a probe measured 100 B for each item with `ForEachAsync` and 4 B with `ForAsync`. The work queue of `RunAsync` keeps `Channel<T>`. A worker that waits on an empty queue allocates one read operation of about 80 B, because the stop token can be cancelled, and section 4.11 says so in place of "pooled value-task sources". With an instant transport, as in the benchmark, the workers wait one time for each message, which is the worst case. The two boxes and the waiter are the 150 B of each send that phase 13 did not attribute (D159). | A work queue of its own with reusable waiters, which is more code for a queue that is empty only when the workers are faster than the claims. Or a check of `Instrument.Enabled` before each tag list, which still boxes when a listener is on. |
| D282 | Allocations of the subscription lookup and resolution | Recommended | A cache key is one string with the owner part written into it, so a cache hit builds no owner part string. The text of each key and tag stays the same, so a cache that a host shares between versions keeps its entries. The caching wrapper makes the list of the missing owners only on a miss, and reads the companies and the users by index, so it boxes no enumerator. The resolution reads its lists by index, gives its sets, dictionaries, and lists the capacity of the scope, keeps the first candidate of a target and channel in the pair, and makes the skipped subscriptions, the routes, and the opt-out decisions as arrays of their exact size. A pair with one candidate keeps the skipped array of that candidate, because it is already in level order. Section 4.11 gets a budget line for the resolution: for each target and channel, the route with its trace or the opt-out decision, the skipped subscriptions, and the scratch of the pair and its index entry. A unit test measures the bytes of each user with `GC.GetAllocatedBytesForCurrentThread()`, because the resolution is synchronous and has no dependency. The probe of the plan of phase 34 found about 1,740 B for each user of an opt-out, and about 180 B of it are the result. | Only a budget line with the measured 1.7 KB. Or a resolution that writes into buffers of the caller, which changes the public method. |
| D283 | Channel check | Confirmed | `IRegistryCommands.CheckChannelAsync(ChannelId channel, CancellationToken ct)` runs the health check of the transport of one channel in the host that publishes it. It records a new state with the store methods of D277, as the outbox loop does: a check with no error brings a channel that is down for errors up, and a failed check marks an up channel down with the reason `Errors`. The store adds no row when the state does not change, and a check never ends a pause (D106). The command is part of the registry API, with the route `POST {prefix}/channels/{channel}/check` in the registry group and the HTTP client of `MsgHatch.Api.Client`, so the admin UI calls it with the token of the registry pages. The channel list gets the button Check. The owner: "Add a new "Check" or "Test" action to the Channels list rows for checking a channel for up/down and register a state change if the channel state changed", and "The test should not save a new ChannelState record if the state did not change". | A check in `IDeliveryManager` next to pause and resume, which needs a delivery client package and a token with `DeliveryAdmin`. Or a check that only reports an error, and marks no channel down. |
| D284 | Registry commands of the settings | Recommended | `IRegistryCommands` gets `SaveChannelAsync(ChannelEntry entry, CancellationToken ct)`, `SaveMessageTypeAsync(MessageTypeEntry entry, CancellationToken ct)`, `ResetChannelAsync(ChannelId channel, CancellationToken ct)`, and `ResetMessageTypeAsync(MessageType type, CancellationToken ct)`, with the results of `IRegistryWriter`. The revision of an entry is the expected revision (D79), and a save ignores `LastModifiedAtUtc`. `RegistryManager` runs `RegistryEntryRules` first (D234, D285), and then calls the writer. With no writer, each command gives `Errors.NotFound` with a message that names the missing writer. The `PUT` and `DELETE` routes call `IRegistryCommands` of `TenantKey.None`, and `MapMsgHatchRegistry` maps them in each host. So a host with no writer gives 404 with the problem body of the command, and the client gives the same error and message as the in-process commands. Reason: an admin page resolves only the query and command interfaces (D249), and `IRegistryWriter` is a singleton of the routing, not a tenant service. One entry type for the writer, the commands, and the bodies needs no new type. | A draft type for each entry, as `SubscriptionDraft`, with one more mapping. Or no `PUT` and `DELETE` routes in a host with no writer, so the client gets a 404 with no message. |
| D285 | Rules of a registry entry before an HTTP call | Recommended | `RegistryEntryRules` of `MsgHatch.Domain.Routing` becomes public, with `Check(ChannelEntry entry)` and `Check(MessageTypeEntry entry)`. They check the display name and the description (D171), the revision, and that `DisabledChannels` is not null and has each channel one time. The check that each disabled channel is a channel of the message type needs the registry, so it stays in `StoredRegistry`, after the rules. `RegistryManager` and `HttpRegistryCommands` run the rules first. So the client gives the error of a rule with no HTTP call, with the same message as the server. Reason: a client checks its input before a call (D234). | No check in the client, so each invalid entry costs a call. |
| D286 | Stored entry in the registry queries | Recommended | `RegistryChannel` gets `ChannelEntry? Entry`, and `RegistryMessageType` gets `MessageTypeEntry? Entry`: the stored entry, or null for no entry and in a host with no writer. `RegistryManager` takes each entry from the entries that it reads for the revisions. `RegistryChannelResponse` and `RegistryMessageTypeResponse` get `entry`, with the entry bodies and their new mappings back. Reason: the names and the descriptions of the queries are the merged values. An edit page that saves a merged name stores the name of the code as a stored name, and a later change of the name in the code then does not show. With the entry, an empty field saves null, which is the value of the code. | Two fields for the stored texts, with no time of the last change. Or a third query that gives the entries. |
| D287 | Registry edit pages | Recommended | `MsgHatch.AdminUI` gets `ChannelEditPage` at `/msghatch/channels/{channel}/edit` and `MessageTypeEditPage` at `/msghatch/message-types/{type}/edit`, with the route builders `RegistryAdminRoutes.ChannelEdit(ChannelId)` and `RegistryAdminRoutes.MessageTypeEdit(MessageType)`. Each page uses `AdminEditPage` with the display name, the description, and the enabled flag. The message type page also has a check box "Send on {channel}" for each of its channels. An empty text field saves null. A help text tells that a change applies to the next events, that the outbox still sends the messages that it holds, and that another process applies the change within its refresh interval. The actions are Back on the left, and Reset and Save on the right (D261). Reset shows only for a stored entry, and asks with `DeleteDialog`, which gets the parameter `Action`. The view page of a channel and each row of the channel list and of the message type list get Edit. | An edit dialog on the list pages (D260). Or fields on the view page, with no edit page. |
| D288 | Example history of the channel states | Recommended | In the `Development` environment, `ExampleApp` writes past state rows at startup through `SetChannelDownAsync` and `SetChannelUpAsync` of the outbox dispatch store, only for a channel with no state row. Email gets four down states with a pause among them, and one of them starts before the 30-day window, so the History dialog shows the row that was current at its start. SignalR gets one down state, and Broker gets no row. Each down state ends with an `Up` row, so each channel is up after the seed. A channel with a row gets no seed, so a second start adds nothing, and a real history stays as it is. The owner: "Include some code for seeding test data simulating channel down events to be able to test the history feature on the UI". | An option of the example app that turns the seed on. Or a seed in each environment. |
| D289 | Query and command interfaces of the delivery | Recommended | `IDeliveryManager` goes away, as `ISubscriptionManager` and `ITemplateManager` did (D231). `IDeliveryQueries` has `GetChannelsAsync`, `GetTrafficAsync`, `GetReceivedMessagesAsync`, `GetOutgoingMessagesAsync`, `GetOutgoingMessageAsync`, `GetReceivedContentAsync`, and `GetOutgoingContentAsync`. `IDeliveryCommands` has `PauseChannelAsync`, `ResumeChannelAsync`, `RetryReceivedAsync`, `CancelReceivedAsync`, `RetryOutgoingAsync`, `CancelOutgoingAsync`, `DeleteTestDataAsync`, and `PurgeAsync`. `DeliveryManager` implements both. `AddMsgHatch` registers both as scoped default tenant services with their logging wrappers, and `UseDeliveryQueries` and `UseDeliveryCommands` select another implementation (D62). The wrappers keep the event ids of the delivery manager. Reason: an admin page resolves only the query and command interfaces (D249), and the HTTP client needs a DI scope for its handlers, as the registry client does. | One interface `IDeliveryManager` that the client implements, which breaks D231. |
| D290 | Content and trace as JSON text | Confirmed | `GetReceivedContentAsync` and `GetOutgoingContentAsync` of `IDeliveryQueries` give `ErrorOr<MessageContent>`, with `MessageContent(Guid Id, string Json)`: the whole message with its event or envelope, as `MsgHatchJson` writes it with the content type registry of the server. So the body of the content routes does not change, and the client gives its text. `OutgoingMessageDetail.Trace` becomes the JSON text of the route trace, written with the newest registered version of its type, as the route writes it today. A host that needs the typed message reads its tenant store with `IInboxStore.GetAsync` or `IOutboxStore.GetAsync`, as the manager did. The Wolverine example does this for its SignalR catch-up. Reason: an admin host has no message types (D235), so a client cannot make a typed event, envelope, or trace again. The owner: "JSON text". | No content in the admin UI. Or typed content over HTTP, with the message types in each admin host. |
| D291 | Delivery contracts and errors | Recommended | The delivery bodies and `DeliveryEndpointJson` move from `MsgHatch.AspNetCore` to `MsgHatch.Api.Contracts`, with public `From` methods and mappings back to the domain types (D228, D270). A name that is not valid gives `Errors.SerializationFailed`. The internal source `src/Shared/DeliveryApiRoutes.cs` holds the route paths, the names of the query values, and the text of a cursor `{CreatedAtUtc}_{Id}`, so the routes and the client use one format. Each delivery route writes its errors with `ErrorProblems` (D233), so `Errors.Unavailable` gives 503 in place of 500. The new route `DELETE {prefix}/test-data` calls `DeleteTestDataAsync`, and gives 200 with `inboxMessages` and `outboxMessages`. It acts on all tenants, so a `tenantId` gives 400. Reason: a client reads the error type, and each method of the command interface needs a route. | The bodies stay in `MsgHatch.AspNetCore`, so the client needs its own copies. |
| D292 | Rules of a delivery request before an HTTP call | Recommended | The public `DeliveryRules` of `MsgHatch.Domain.Management` holds the checks that need no state: the limit of a page, the ids, the order of the created times, a bulk action with no filter, the length of a pause note, a global action of a tenant key, and a `maxCount` less than 1 of a purge. `DeliveryManager` and the client adapters run them first (D234), so the client gives the error of a rule with no HTTP call. The check that `finishedBeforeUtc` is not after the current time stays on the server, because the clock of a client can differ. | No check in the client, so each invalid request costs a call. |
| D293 | Lists and times as query values | Recommended | The generator of D236 also writes a query value of type `IReadOnlyList<T>`, with `T` a type that it writes already, as one `name=value` pair for each item, and no pair for null or an empty list. It writes a `DateTime` as the ISO 8601 text of D57, which is the format `O` of a UTC time. A value of another type still gives the diagnostic of an unsupported value. Reason: the delivery queries have repeated `ids` and the created and due times. | The adapter writes each list and time into a string parameter, with a format in each client. |
| D294 | Delivery API client | Recommended | `MsgHatch.Api.Client` also implements `IDeliveryQueries` and `IDeliveryCommands` over HTTP. It has the internal `IDeliveryApi` with `[HttpApi(typeof(DeliveryEndpointJson))]`, the adapters `HttpDeliveryQueries` and `HttpDeliveryCommands`, `DeliveryApiClientOptions` in the section `MsgHatch:DeliveryApiClient` with the default prefix `/delivery` and its validator, and `UseDeliveryApiClient` with an overload with `Uri baseAddress`. The adapter of a tenant key sends its tenant as `tenantId`, as the subscription client does, and the global commands send none. One prefix serves both route groups, so a host maps them with the same prefix. Reason: the core client package holds the clients of the core APIs (D271). | A package `MsgHatch.Delivery.Api.Client`. Or a second prefix for the content group. |
| D295 | Delivery list pages | Recommended | `MsgHatch.AdminUI` gets `OutboxListPage` at `/msghatch/outbox` and `InboxListPage` at `/msghatch/inbox`, with the route builders of `DeliveryAdminRoutes`. Their filters go into the queries of the server, because an outbox can hold millions of messages. So they are an exception to the filters in the page of `phase-30.1`. The filters apply at each change. `AdminList` gets the parameter `Pager`, and a delivery list gives the buttons Newer and Older on the cursors of D112. "Retry filtered" and "Cancel filtered" act on the query of the filters, with a question first and the counts after, and they are disabled with no filter. The owner chose the groups "Outbox and messages" and "Inbox". | Page numbers with a count query, which the stores do not have. Or filters in the page, which load the whole store. |
| D296 | Message pages | Recommended | `OutgoingMessagePage` at `/msghatch/outbox/{id}` shows the summary, the tries, and the trace as formatted JSON (D290). `ReceivedMessagePage` at `/msghatch/inbox/{id}` reads its summary with a page of its id. Each page reads its content only after a click on "Show content". So a look at the tries reads no personal data, and a token with no role `DeliveryContent` sees the text of `Rejected` only in the content area. Cancel and Retry act on the query of the one id. | The content at each open of the page. |
| D297 | Pause and resume on the channel pages | Recommended | The channel list and the channel view page also call `IDeliveryQueries.GetChannelsAsync`, and show the pending messages, the pending test messages, and the oldest due time. Pause asks for a note of at most 500 characters in `AdminDialog`, and Resume shows for a down channel. An error of the delivery query shows above the list, and the registry columns still show. The owner chose the group "Pause and resume". | A separate delivery channel page next to the registry channel page. |
| D298 | Traffic page and the menu | Recommended | `TrafficPage` at `/msghatch/traffic` shows the counts of `GetTrafficAsync`, with links to the lists with the filters of each row. It has "Delete test data" with `DeleteDialog`, and "Purge" with a dialog of the finish time and the maximum count. The menu of `AdminUiExample` gets "Traffic", "Outbox", and "Inbox" after "Verification" (D274), and its token needs the roles `DeliveryAdmin` and `DeliveryContent`. The owner chose the group "Traffic and cleanup". | Delete and purge only through HTTP, as before. |
| D299 | Content type and payload JSON of a broker publish | Recommended | `BrokerPublish` gets `string ContentType`, the content type of the outgoing message, for example `Broker:DEvent`, and `JsonTypeInfo PayloadJson`, the JSON type information of the run-time type of the payload. The broker transport finds the entry of the content type in `IContentTypeRegistry`, and takes the type information of the payload from the options of the JSON type information of the envelope. A direct client writes the payload with it, and needs no JSON setup of its own. The Wolverine client ignores both, because Wolverine writes the payload itself. Reason: a direct client of an SDK must write the payload and select a destination, and `BrokerPublish` had neither. | Each direct client reads the registry itself, so each one repeats the lookup. Or JSON options in the options of each client. |
| D300 | Amazon SNS broker client | Recommended | The package `MsgHatch.Channels.Broker.AmazonSns` has `AmazonSnsBrokerClient : IBrokerClient` on `IAmazonSimpleNotificationService` of `AWSSDK.SimpleNotificationService`, with the same `AWSSDK.Core` as the SES client. Each publish is one `Publish` call: the topic of the content type, the payload JSON as the message, and each header as a `String` message attribute. The message id of the response is the provider message id of the receipt. `AmazonSnsOptions` in the section `MsgHatch:AmazonSns` holds `Region`, `DefaultTopicArn`, and `Topics`, a map from the name of a broker registration to a topic ARN: `DEvent` for the content type `Broker:DEvent`. The key is not the content type, because a colon in a configuration key separates two sections (checked fact of phase 38). A content type with no topic and no default gives `Errors.NotFound`, as a message with no route of the Wolverine client does. `UseAmazonSnsBroker` registers the options, the validator, the SDK client, and the client with `UseBrokerClient`. Reason: D134 names SNS as the first direct client, and the host keeps one AWS SDK and one credential setup. | One topic for each message type with a name rule and no map. Or a topic in the channel settings of each message type, which puts an AWS value into the core settings. |
| D301 | Limits of an SNS publish | Recommended | The client gives `Errors.ValidationFailed` with no call for a message with more than 10 headers, because SNS sends at most 10 message attributes to an SQS subscription with raw message delivery, and discards a message with more attributes there with no error to the sender. It also gives `ValidationFailed` for a header name or value that SNS does not accept as a message attribute. A message over the size limit of SNS gives `Errors.Rejected` from the error of the service. The options validator rejects a FIFO topic, an ARN that ends with `.fifo`. A FIFO topic needs a message group, and the dispatch sends the messages of a channel in parallel, so a group gives no order. | Support of FIFO topics with a message group from a header, and a deduplication id from `msghatch-id`. |
| D302 | Errors and health of the SNS client | Recommended | An `IExceptionClassifier<AmazonSnsBrokerClient>` of the host comes first. `ThrottledException` and `KMSThrottlingException` give `Errors.RateLimited`, because a throttle needs no administrator. `InvalidParameterException` and `InvalidParameterValueException` give `Errors.Rejected`. `NotFoundException`, `AuthorizationErrorException`, and the other KMS exceptions give `Errors.Unavailable` and a Critical log, so the messages stay pending until an administrator acts, as for an SES account that cannot send. `HttpRequestException`, `InternalErrorException`, and a service status of 500 or more give `Errors.Unavailable`. Each other exception gives `Errors.Exception`. `CheckHealthAsync` calls `GetTopicAttributes` for each configured topic, and gives the first error, so the channel check of D283 finds a missing topic or a missing permission. Reason: one outage gives the same down state on each AWS channel. D306 adds the throttle codes and the wait of the provider. | `NotFound` for a missing topic, which fails each message of the topic for good. Or a health check with no call, as the Wolverine client has. |
| D303 | MongoDB change stream signal | Recommended | `MsgHatch.Data.MongoDb` gets `MongoDbDispatchSignal` and `UseMongoDbDispatchSignal()`. One watch of the database of the stores sees the inserts into the inbox and the outbox collections, and notifies the topic of each insert on a local `InProcessDispatchSignal`. The MongoDB stores keep each tenant in that database (checked fact of phase 39). A broken watch makes `WaitAsync` return an error, so a loop polls, and the watch starts again. The signal keeps no resume token, because a missed change costs at most one poll. Reason: the insert of a message is its own signal, so a notify can never be lost between the write and the signal, and the stores need no other infrastructure. The owner chose it as phase 39. | A separate package `MsgHatch.Dispatch.MongoDb`. Or a resume token in the store. |
| D304 | Explicit notify of the MongoDB signal | Recommended | `NotifyAsync` of the MongoDB signal inserts a document with the topic into `msghatch_signals`, a collection with a TTL index of one minute, which the watch also sees. So a retry, a resume, or a channel check on one machine wakes the loops of each machine. | No explicit notify, so a resume or a retry waits for the next poll on the other machines. |
| D305 | RabbitMQ dispatch signal | Recommended | The package `MsgHatch.Dispatch.RabbitMq` uses `RabbitMQ.Client` 7.2.2 directly. `WolverineFx.RabbitMQ` 6.61.0 needs the same version, so a host with both packages has one client version (checked fact of phase 40). `NotifyAsync` notifies the loops of its own process at once, and one connection in the background publishes the topic name with no persistence to a fanout exchange, default `msghatch.dispatch`. Each process binds one exclusive, auto-delete queue with a name from the server, and gives each topic to a local `InProcessDispatchSignal`. RabbitMQ does not implement the flag `no-local` of a consumer, so the signal skips its own messages by their `AppId` (checked fact of phase 40). A lost connection makes `WaitAsync` return an error, so a loop polls, and `UseRabbitMqDispatchSignal` requires `EnablePolling` true (D190). Reason: it works with each store, and a signal is a small ping that needs no bus framework. The owner chose a direct client as phase 40. | A signal through Wolverine, which adds its host and its routing to a ping. |
| D306 | Wait of the provider after a rate limit | Recommended | `Error` gets `TimeSpan? RetryAfter`, the wait that the provider asked for before the next try, or null when the response gives no wait. The next try time of a transient error in the inbox and in the outbox is the later of its scheduled time and the try time plus this wait, and is at most `RetryOptions.MaxDelay` after the try (section 4.14). The SNS client reads the header `x-amz-retry-after` in milliseconds, or else `Retry-After` in seconds, and gives no wait for a missing, negative, or other value. The SES client reads the same headers through the internal source `src/Shared/AwsRetryAfter.cs`, which both AWS packages compile. The Resend client reads the header `retry-after` in seconds, which Resend documents for a 429 response, and gives no wait for a date or another value. The SNS client also gives `Errors.RateLimited` for the error codes `Throttled`, `Throttling`, and `KMSThrottling`, and for the status 429, because the SDK gives a throttle of `Publish` as the base exception with the code `Throttled`, and does not retry it (checked fact of phase 38). The owner asked for it after the record of phase 38. Reason: a try before the wait of the provider fails again and uses a try, so a long throttle can use all tries of a message. | The wait with no limit, so that a wrong header can stop a message for a long time. Or a try that does not count while the provider asks to wait. |
| D307 | One shared SemVer | Recommended | `Directory.Build.props` sets one `VersionPrefix` for each project. The first version is 0.1.0. A phase that changes a library project raises it one time: the patch for fixes only, the minor for a feature, and the major for a breaking change. Before 1.0.0, a breaking change raises the minor. Reason: the owner chose one version, as EF Core and Wolverine do. A consumer takes the same version of each MsgHatch package, and no version range between two MsgHatch packages can break. 0.x says that the API is not stable yet, and later phases still add members to interfaces. | A version for each project. Or a first version 1.0.0 or 1.0.0-preview.1. |
| D308 | Package metadata and pack check | Recommended | The source projects get a description, tags, symbol packages, Source Link of the SDK, package validation, and a CI build flag. `MsgHatch.SourceGeneration` is not a package: `MsgHatch.Templates` carries its generator, and a pack turns each other project reference into a package dependency of the same version. `pack.ps1` packs each package and checks its content. Reason: a package with missing metadata must fail before a publish. | A separate generator package. Or no pack check. |
| D309 | GitHub workflow | Recommended | `.github/workflows/build.yml` runs `coverage.ps1` and `pack.ps1` on each push of a branch and each pull request. On a tag `v<version>` that equals `VersionPrefix`, its job `publish` in the environment `nuget` pushes the packages to nuget.org with a temporary API key from trusted publishing: `NuGet/login` exchanges the OIDC token of GitHub for the key. Reason: the owner chose a workflow that also publishes, and a tag gives one clear release step. Since 2026-08-17, a new nuget.org API key lives at most 30 days, and Microsoft recommends trusted publishing for GitHub Actions. Phase 41 found it available, so the repository stores no API key. | An API key in the secret `NUGET_API_KEY`, which the owner replaces each 30 days. Or a publish from the owner's machine. |
| D310 | README of each package | Recommended | Each project under `src/` has a `README.md`, which the package carries as `PackageReadmeFile`. The README is the single source of the release notes, so the project files set no `PackageReleaseNotes`. Its parts: the purpose, the install command, the DI setup, the options, the examples, and the version history. A change of a library project keeps its README in sync in the same commit. Reason: nuget.org shows the README of a package, and one source cannot disagree with itself. | Release notes in `PackageReleaseNotes`, or one changelog for all packages. |
| D311 | README examples that the tests run | Recommended | Each C# block of a README equals a region `readme:name` of a test file, and each JSON block equals a test data file. The examples run as tests, and a linked test source compares them with the README. A test also compares the options table with the properties of the options class. Reason: the owner chose compiled and tested examples, and a README that no build reads drifts from the code. From phase 54 (D330), the check reads the regions and the files of the unit and the integration test project of the package together, and it runs in the unit test project. | Plain text examples. Or the tool MarkdownSnippets, which adds a tool for a check of about 100 lines. |
| D312 | Atomic state rows of the file store | Recommended | `MessageFiles.TryCreate` writes a state row to a temporary file, and gives it the path of its version with `link()` on Linux and macOS, and with `File.Move` with no overwrite on Windows. So of two parallel inserts of one version, only the first succeeds, and a reader never sees half a row. Reason: on Unix, .NET does `File.Move` with no overwrite as `lstat` of the target and then `rename`, and `rename` replaces a file that another caller created between the two calls. Then both callers get true, and a lost row can lose the pause of an operator. Each error of `link()` other than `EEXIST` is an `IOException`, so a file system with no hard links gives `Errors.Unavailable`, not a silent race. | A read-back of the file after a wait, which only makes the window smaller, and cannot tell two equal rows apart. A unique file name for each writer, which needs a rule that picks one of two rows of one version. A lock file with `FileShare.None`. `FileMode.CreateNew` on the path of the version, which lets a reader see half a row. |
| D313 | Build on Linux | Recommended | The job `build` of the GitHub workflow runs on `ubuntu-latest`. The 3 unit tests that check a file lock or an ACL of Windows are skipped on the other OSes, and run on a Windows machine of a developer. Reason: the owner asked for a build on Linux, and after D312 each other unit test passes on Linux. | The job on `windows-latest`. A matrix of Windows and Linux, which runs each test two times. |
| D314 | One bind of the core options | Recommended | `AddMsgHatch` binds `MsgHatchOptions`, the inbox `RetryOptions`, and the inbox and outbox `DispatchOptions`, and adds their validators, only on its first call, when it creates the shared setup. Each call applies its own action from code. So a value from code wins over the configuration in each order of the calls, and a later value from code wins over an earlier one. Reason: a second call bound the section `MsgHatch` again after the action of the first call, so a second call with no action gave the configuration value. `AddChannelOptions` binds the options of a channel one time in the same way. From phase 54 (D328), the first call also sets the root of the configuration sections, and the core binds its options under that root. A later call with null keeps the root, and a later call with another root fails the startup validation. | `PostConfigure` for the action from code, which keeps the second bind, and also runs after each `Configure` of the host. |
| D315 | Tenant-wide SignalR messages | Recommended | `DeliveryTargetKind` gets `Tenant`, a target for all users of one tenant. Its target id is empty, and the tenant comes from the message. The SignalR transport sends a tenant target to the group `msghatch:tenant:{TenantId}`, or `msghatch:all` with no tenant, which `SignalRGroups.ForTenant` gives. A tenant target with an id gives `Errors.ValidationFailed`. The hub of the host adds each connection to the group of its tenant, as it does for the group of its company. The deterministic id gives the tenant kind its own byte `a`. The outbox filters of the HTTP endpoints and of the admin page accept a tenant target with no id. They refuse a tenant target with an id. Only a router of the host returns a tenant target. The subscription router has no tenant target, because the owner wants no tenant level in the subscriptions. Reason: a multi-tenant UI needs messages for a whole tenant. | A message to each company of the tenant, which misses a user with no company, and sends one message for each company. The group `msghatch:{TenantId}:tenant`: with the tenant id `company`, it is the same name as the group of the company `tenant` with no tenant. |
| D316 | SignalR demo dashboard | Recommended | The example project `examples/SignalRDemo` shows the resolution of a tenant, a company, and a user on one screen: a publish panel and a grid of session panes. Each pane has its own `HubConnection`, keeps its token in memory, and has a host field that stands for the domain of a real page. A demo login with no password gives a token of the app. The domain map uses `*.localhost`, which a browser resolves with no hosts file, and a host label names one tenant or one company only. The hub tells each connection its tenant, company, user, and groups. The dashboard sends the state of each pane with a notification, the server gives the expected hits, and the dashboard marks a miss or an extra hit. Reason: the owner wants to demo and check the routing of SignalR for many users of several tenants and companies at once. | Separate browser tabs with `sessionStorage`, which need one window for each user. Tokens of `dotnet user-jwts`, which need a setup step for each user. A page in `ExampleApp`, which mixes the demo with the subscription example. |
| D317 | Package versions that float within their major | Confirmed | `Directory.Packages.props` sets `CentralPackageFloatingVersionsEnabled`, and each package has the version `{major}.*`, so each restore takes the latest stable version of the major. A package with the major 0 floats within its minor. `dotnet pack` writes the resolved version as the lowest version of a dependency, for example `10.0.15` for `10.*`. The Roslyn packages of the source generator keep the exact version of the compiler of the SDK, and a package keeps an exact version only with the reason in its label. The owner: "please pin only to the MAJOR version with latest minor and revision". | Exact versions with a bot such as Dependabot, which NuGet recommends, because each update is then a commit. A lock file, which keeps each restore fixed until a forced update. |
| D318 | Provider packages of the EF Core stores | Confirmed | `MsgHatch.Data.EFCore` has no SQL text and no provider name, and runs on each relational EF Core provider with no change. Its claim is a LINQ read of the due candidates and an `ExecuteUpdate` with a claim token, which claims the next candidates when another dispatcher took one first. An add is an insert on the unique key, the JSON work is in C#, and the due indexes have no filter. The option `TextCollation` sets the collation of the ids and the names. A provider package implements a public dialect contract of the base package: the provider name, the default collation, and an optional claim statement with the quoted names of the model. `MsgHatch.Data.EFCore.SqlServer` (phase 48) and `MsgHatch.Data.EFCore.PostgreSql` (phase 49) give the claim with `READPAST` or `SKIP LOCKED`, the binary collation, and a setup with one call. The subscription store has provider packages of its own for its migrations (D320), which use the dialect of the store provider package. The owner: "I wanted to keep it agnostic and ensure that any database having an EFCore provider can be used", and then chose "Base + provider packages". | A set of SQL statements for each provider in `MsgHatch.Data.EFCore` (D120), which needs a change of the package for each new database. A base package only, with a LINQ claim on each database and a collation that each host must set. |
| D319 | Times of the EF Core stores | Confirmed | Each time is a `DateTime` in the time type of the provider: `datetime2(7)` on SQL Server and text on SQLite keep 100 ns, and `timestamptz` on PostgreSQL keeps microseconds, so a time comes back cut down to the microsecond there. `Sequence` orders two due times in one microsecond (D144). The owner chose this from the two forms of a store with no SQL text: "Native time type". | A `bigint` of ticks on each database, exact as D185, with a readable column in the migration of the host. A generated readable column next to the ticks, which needs SQL for each database. |
| D320 | Schema migrations of the EF Core stores | Confirmed | A migration of EF Core holds the column types and the annotations of one provider, and the EF Core documentation keeps one set of migrations for each provider. So each provider package ships the migrations of its context for its provider, which the EF Core tools generate: `MsgHatch.Data.EFCore.SqlServer` and `MsgHatch.Data.EFCore.PostgreSql` for `MsgHatchDbContext`, and `MsgHatch.Subscriptions.Data.EFCore.SqlServer` and `MsgHatch.Subscriptions.Data.EFCore.PostgreSql` for `MsgHatchSubscriptionDbContext`. The base packages give the explicit method `MigrateMsgHatchAsync`, which applies the migrations of each registered MsgHatch context, with the provider setup of the stores or with a setup of its own, for example a connection string with the rights to change the schema. A host calls it at its start, or a separate console app of the pipeline calls it, so the service needs only the rights to read and write rows. `ScriptMsgHatchMigrationsAsync` gives an idempotent SQL script for a pipeline or an administrator that runs only SQL. The option `RequireCurrentSchema` makes the start of a host fail when a migration is not applied, with only a read of the history tables. The example `examples/SchemaMigrator` is such a console app. Each context has a history table of its own in the schema of the option, and each migration takes that schema at run time. For a provider with no MsgHatch package, the host generates the migrations of the MsgHatch contexts in its own assembly, and the same method applies them. The owner asked: "Shouldn't MsgHatch provide a provider agnostic way to do them?", and then chose the migrations in the provider packages, provider packages of the subscription store, an explicit call, and the phases 48 and 49. The owner added: "in enterprise setups it's common to have such DDL permissions in the CICD pipeline, but not runtime for the service". | One set of migrations for each provider, which EF Core does not generate. Migrations that only the host makes (D120), so each host repeats the work for each new version. An option that applies the migrations at the start of each host. |
| D321 | Table names of the stores | Confirmed | A store puts the prefix `MsgHatch` on its table names only when its database has no schemas. SQLite, a file with no schemas, and MongoDB, with no schemas inside a database, keep the names `MsgHatchInbox`, `MsgHatchOutbox`, and the others. The EF Core stores put their tables with no prefix into the schema of `EntityFrameworkStoreOptions.Schema`, default `msghatch`: `Inbox`, `Outbox`, `ChannelState`, `ChannelEntry`, `MessageTypeEntry`, and `Subscription`, with the history tables `__StoreMigrations` and `__SubscriptionMigrations`. The names of the indexes drop the prefix too, for example `IX_Inbox_Due`. The names keep PascalCase on each provider. One transaction of SQL Server or PostgreSQL can change the tables of each schema of one database, so a schema keeps a shared transaction with the host possible, and a separate database does not on PostgreSQL. No package is on nuget.org yet, so the provider packages make their `Initial` migrations again, with no rename migration. The owner: "only those databases should prefix that does not support multiple schemas within the database, like for example SQLite". | One set of names for each store, so a name such as `msghatch.MsgHatchOutbox` repeats the schema. Lower-case names on PostgreSQL, which need the names of each provider in its dialect. |
| D322 | Exception classifiers of the EF Core stores | Confirmed | The EF Core stores classify an exception in the order of D141. A cancelled operation gives `Errors.OperationCancelled` first. Then the classifiers of the host: `IExceptionClassifier<TContext>`, with the context of the store as the client, so `IExceptionClassifier<MsgHatchDbContext>` serves the inbox, the outbox, and the registry settings, and `IExceptionClassifier<MsgHatchSubscriptionDbContext>` serves the subscription store. Then the dialect of the provider package, with the new method `ErrorType? ErrorTypeOf(DbException exception)` of `IEntityFrameworkStoreDialect`. It is a default method that gives null, so the dialect of a host still compiles. It gets the `DbException` also when EF Core wraps it, for example in `DbUpdateException`. Then the built-in mapping. The SQL Server and PostgreSQL dialects give `Errors.Rejected` for a missing permission or a failed login, and `Errors.ValidationFailed` for a missing table or column and for a value that is too long, so these errors are not transient. Each other database error stays `Errors.Unavailable`. The SQLite and MongoDB stores keep their mapping. The owner: "Yes, introduce the same pattern for an exception classifier, add it into phase 50". | An overridable method of the internal `StoreContexts`, from which a provider package cannot derive, and which a host cannot change. A type argument for each store, so a host registers one classifier four times. |
| D323 | JSON column type of the EF Core stores | Confirmed | Each JSON column, `Content`, `Trace`, `Tries`, `DisabledChannels`, `Settings`, and `NamedUsers`, gets the type of the new option `EntityFrameworkStoreOptions.JsonColumnType`, or else the JSON type of the dialect, or else the text type of the provider. The PostgreSQL dialect gives `json`, which checks each write and keeps the text unchanged. `jsonb` changes the order of the keys and drops duplicate keys, so an event type of a host that needs `$type` as the first key can fail to read. So a host selects `jsonb` only with the option. The SQL Server dialect keeps `nvarchar(max)`, because the type `json` needs SQL Server 2025 or Azure SQL, and a host on such a server selects `json` with the option. The migrations take the type at run time, as they take the schema and the collation (D320). The property stays a `string`, and the store keeps its JSON work in C# (D176). The stores write a document one time and read it whole, so `text` is faster, because `json` parses each write, and `jsonb` also converts each read. The owner: "Yes, add it", and then chose "json, for validation" over `text` for speed. | Text columns on each provider, so a write of invalid JSON is stored, and PostgreSQL needs a cast for its JSON operators. `jsonb` as the default on PostgreSQL. `json` as the default on SQL Server, which stops the package on SQL Server 2022 and earlier. |
| D324 | Names of the channel packages | Confirmed | Each channel package gets the role segment `Channels`, as the other package families have `Data`, `Dispatch`, `Subscriptions`, and `Templates`: `MsgHatch.Channels.Email`, `MsgHatch.Channels.Email.AmazonSes`, `MsgHatch.Channels.Email.Resend`, `MsgHatch.Channels.SignalR`, `MsgHatch.Channels.Broker`, `MsgHatch.Channels.Broker.Wolverine`, and `MsgHatch.Channels.Broker.AmazonSns`. The folder, the project, the assembly, the package id, and the root namespace follow the name (D50). The channel ids `Email`, `SignalR`, and `Broker` do not change. `AmazonSES` becomes `AmazonSes`, because it was the only package name with the casing of the brand, and `AmazonSns`, `RabbitMq`, `MongoDb`, and `PostgreSql` use PascalCase. The class names do not change, so the SES classes keep `Ses`. `EFCore` stays in the names of the store packages. No package is on nuget.org yet, so the old ids need no deprecation. The owner: "Shouldn't we add the Channels to the library names in the Channels solution folder to make it similar to Data, Dispatch, Subscriptions and Templates?", then "Yes, plan phase 51", and "Yes, AmazonSes". About `EFCore`: "it's much shorter and everyone calls it that way. It's better to avoid namespace collision as well". | The current names with no role segment, unlike the other package families. `EntityFrameworkCore` in place of `EFCore`, which is longer, and which gives a namespace segment with the name of `Microsoft.EntityFrameworkCore`. |
| D325 | MySQL provider of the EF Core stores | Confirmed | The MySQL packages use `Microting.EntityFrameworkCore.MySql` `10.*`, a fork of Pomelo on `MySqlConnector`. It has the MIT license, which fits the Apache-2.0 license of MsgHatch. It supports EF Core 10, and its tests run on MySQL 8.0 to 9.5 and on MariaDB 10.5 to 12.1. Its 11.x line needs .NET 11, so the packages take the 10.x line. The public API of the packages has no type of the provider, for example no `ServerVersion`, so a later move to Pomelo is not a breaking change for a host. The README of each MySQL package gives the reason for the provider. The facts come from nuget.org and GitHub on 2026-11-24. The owner: "Use Microting and mention the reason for the selection in its README". | Oracle `MySql.EntityFrameworkCore` on `MySql.Data`: its license `GPL-2.0-only WITH Universal-FOSS-exception-1.0` limits a closed-source host that ships its binaries, and it supports MySQL only. `Pomelo.EntityFrameworkCore.MySql`: its latest release is 9.0.0, with no EF Core 10 build. |
| D326 | Table names on a database with no schemas | Confirmed | In MySQL a schema is a database, so a database has no schemas inside it. On such a database, the EF Core tables and history tables keep the prefix `MsgHatch` in the database of the host, as the rule of D321 asks: `MsgHatchInbox`, `MsgHatchOutbox`, `MsgHatchChannelState`, `MsgHatchChannelEntry`, `MsgHatchMessageTypeEntry`, and `MsgHatchSubscription`, with the history tables `__MsgHatchStoreMigrations` and `__MsgHatchSubscriptionMigrations`. The dialect contract gets the default property `bool HasSchemas => true`. With false, the model adds the prefix and uses no schema. Then the option `Schema` has no effect, so the startup validation fails when it is not the default `msghatch`. MySQL keeps the index names for each table, so the index names do not change. The owner: "MsgHatch prefix". | `Schema` as the name of a second database on the same server, for example `msghatch.Outbox`. One MySQL transaction can change the tables of two databases, but the migrator then needs the right to create a database, and D321 asks for the prefix. |
| D327 | Subscription key of the EF Core store | Confirmed | The unique key of a subscription is the new column `OwnerKey`, a binary column of 32 bytes, on each provider. The store calculates it in C# before each insert and each update (D318): the SHA-256 of `TenantId`, `MessageType`, `Level`, `CompanyId`, `UserId`, `Channel`, and `Recipients`. Each value goes into the hash as its byte count and then its UTF-8 bytes, so two different keys never give the same input. The unique index `UX_Subscription_Owner` has only this column. The new index `IX_Subscription_Type` on `TenantId` and `MessageType` is not unique, and serves the delivery read. The 7 key columns have 1,344 characters. On MySQL the 3 ids alone take 3,072 bytes in `utf8mb4`, which is the InnoDB limit of an index key, so MySQL refuses the old index. On SQL Server the declared key of the old index has 2,112 bytes, over the limit of 1,700 bytes. A real key has at most 1,553 bytes, because an owner has a company id or a user id, never both, so the old index kept each real key on SQL Server (D178, checked fact of phase 52). PostgreSQL allows about 2,700 bytes in one B-tree entry. The hash has the same size on each provider, and needs no index option of a provider. The key compares bytes, which is the rule of the default binary collations. A host that sets a `TextCollation` that ignores case gets one owner for each spelling of an id. No package is on nuget.org yet, so the SQL Server and PostgreSQL subscription packages make their `Initial` migrations again. This decision changes D178 for the subscription key. The owner: "Hash for all now". | A hash on MySQL only, through a dialect member, which gives two forms of the subscription model in the base package and keeps the limit of SQL Server. Column prefixes in the unique index on MySQL, which make two ids with the same first characters one owner. An `ascii` column for the ids, which refuses the characters that `StringIds` accepts. |
| D328 | Root of the configuration sections | Confirmed | `AddMsgHatch` gets the parameter `string? configSectionPath = null`, the root of all configuration sections of MsgHatch. Null gives `MsgHatchOptions.DefaultSectionKey`, which is `"MsgHatch"`. A value is a full path, for example `App:Messaging`. An empty value or a value of white space throws `ArgumentException`. `AddMsgHatch` stays safe to call more than one time (D314), so a module of the host can get the builder again with a second call. The first call sets the root. A later call with null keeps the root. A later call with a different root adds a setup error, and the startup validation names both roots (D62). `MsgHatchBuilder` gets `ConfigSectionPath` and `SectionPathOf(sectionKey)`, which gives `{ConfigSectionPath}:{sectionKey}`. No setup method of a package gets a path parameter. A host that needs one section in another place adds its own `services.Configure<TOptions>(configuration.GetSection(...))` after the setup call, because a later configure step wins. Reason: a host keeps the settings of its parts under its own sections, and one value moves all sections of MsgHatch. The owner: "make only the root section path overridable and all MsgHatch related options keep their const sub section key, but the host is able to redirect the MsgHatch config". For more than one call of `AddMsgHatch`, the owner: "Keep them". | A path parameter on each setup method with options, next to the root. The setup methods of a shared options class, for example `UseFileStores` and `UseFileSubscriptionStore`, then need a rule for two different paths. Or one call of `AddMsgHatch` only, which changes about 20 README tests, and a modular host must then pass the builder to each module. |
| D329 | Section keys of the options classes | Confirmed | `SectionName` goes from all 22 options classes, because a constant full path cannot follow a root that the host gives at run time (D328). `MsgHatchOptions.DefaultSectionKey` is `"MsgHatch"`, the default root, which `configSectionPath` replaces. The other 21 classes get `const string SectionKey`, a fixed key under the root, for example `ResendOptions.SectionKey = "Resend"` and `RetryOptions.SectionKey = "Retry"`. Each bind site builds its path at run time: `{root}:{SectionKey}`, and the core child sections under it, for example `{root}:Retry:{channel}`, `{root}:Retry:{contentType}`, and `{root}:Dispatch:Inbox`. The setup methods of a shared options class bind through one helper of the base package, so they cannot bind two paths. A message that names a section gives the bound path. Each setup method keeps its own `BindConfiguration` call with a concrete type, because the configuration binding source generator binds only at such a call (D65, D237). No package is on nuget.org yet, so the phase adds no compatibility members. The owner: "Remove and rename the new SectionKey to DefaultSectionKey where they can get overridden so they define just the default to make them more obvious". | Keep `SectionName` with the default full path, which is wrong for each host that gives another root. Or `DefaultSectionKey` on each class, which says that a host can change each key. |
| D330 | Level of a test with no server | Confirmed | A test that needs no running server is a unit test, in the unit test project of its production project, which a phase adds when it is missing. A test needs a server when it covers the connection and the real communication with a database, a broker, or a cloud service: its result depends on the response and behavior of one given service. A unit test runs on a basic build agent in the cloud with no service, so it can use the local file system and local networking, for example SQLite on a file in a temporary folder (D71), a named pipe, or a TCP listener in the test process. A test of an error case, of an input validation, or of other code that can use a mocked connection is a unit test, for example a setup test, an options test, a dialect test, a check of the model against the migrations, and a README check. A test that tries to open a connection or makes a network call is an integration test, also with a connection string that is not valid, for example a test against a closed port. A test of a connection constructor that throws, with no open and no network call, is a unit test. A test that touches a server only because the assembly fixture of its project starts or connects to it needs no server. An integration test project keeps only the tests that need a server. The README check reads the regions and the `Readme` files of the unit and the integration test project of a package together, so the README example of a step that needs a server, for example a migration, stays in the integration project, and the check of each snippet runs in the unit stage. A region or a file in both projects fails the check. Phase 54 moves the existing tests by this rule, with their names and their asserts. Reason: the unit tests run fast in the validation of each pull request, and the integration tests run slower after the merge, on a container or a deployed environment with a server. The owner: "Move all tests that doesn't require a real running database server to unit tests because those run fast, as part of PR quality gate before merging and integration tests are slower and run on a container or a real deployed environment having a database server." Then: "move them as part of the same transition in phase 54", "Keep them as integ tests only if they test an interface or behavior that requires a real connection to a real database. If they don't, because they test for example some input validation before making any connection, then they are meant to be unit tests.", and "Data provider packages can have both unit and integration tests, the rule is the same: the test cases covering connection and real communication with the server requires integration tests, but those ones that cover error cases, input validations or other codes that can use a mocked connection for example, those should be unit tests." Then: "Even trying to create a database connection is an integration tests, even in the case when intentionally a not valid connection string is given. If that code tries to create a connection, or does a network call, then it is an integration test that needs an environment where there is disk, network, database or other resources available. If the connection constructor is expected to throw, that can be a unit test, assuming that the constructor does not want to open a connection or do network calls.", and "unit tests should test a unit, which is a method, or a decision branch within a method. It mocks certain dependencies and services and tests the decision direction." Then: "Would a basic build agent in the cloud be able to execute those tests as part of the PR validation? If they use only local file system, or general networking, not the response and behavior of one given service, like an API or database server, then they can stay as unit tests." For the README tests, the owner selected the check that reads both projects. For the tests of the MongoDB driver against a closed port, the owner: "Keep the closed-port MongoDB tests as integration tests". | Tests with no server in the integration project of their package, which the validation of a pull request does not run. Or a README check of each project alone, which keeps the README tests of a package with a migration example in the integration project. |

The field name `LastTryAtUtc` follows the naming rule for UTC times (section 5.3).

## 4. Architecture

### 4.1 Flow and components

| Flow step | Library | Consumer implements |
|---|---|---|
| 1 Trigger | `INotificationService`, `ReceivedMessage<TEvent>`, `IInboxProcessor` | Domain event records, their `JsonSerializerContext`, and an inbox store when it uses the inbox |
| 2 Route | `IMessageRouter<TEvent>`, `MessageRoute`, `RouteGrouping`. The package `MsgHatch.Subscriptions` with `SubscriptionRouter<TEvent>`, `SubscriptionResolver`, `ISubscriptionQueries`, `ISubscriptionCommands`, and its stores. | A router. Or, for the subscription router, `IRecipientDirectory` and a scope function for each message type. |
| 3 Templates | `ITemplateEngine<TPayload>` with `ITemplateEngineFactory`, the placeholder engine, the Liquid engine of `MsgHatch.Templates.Liquid` (D205, D216), the template selector, the template parser and cache, `EmbeddedResourceTemplateStore`, `FileSystemTemplateStore` | `ITemplateStore`, only for other storage, for example a database |
| 4 Data | `IPayloadLoader<TEvent, TPayload>`, which the library composers call, and the event loader of `WithEventPayload` (D139) | Template payload types and their loaders, only for a payload that is not the event |
| 5 Generate | `IEnvelopeComposer<TEvent, TEnvelope>`, one library composer for each channel, the generated placeholder writers, and the placeholder renderer | A composer, only for an own channel |
| 6 Store | `NotificationPipeline`, `DeterministicId`, the content type registry, `MsgHatchJson`, and the stores | `IOutboxStore` and `IOutboxDispatchStore`, only for other storage |
| 7 Publish | `IOutboxDispatcher`, `PublishTry`, and one `ITransport` for each channel | A transport, only for an own channel |

Steps 3 to 5 run inside the composer of each channel registration (D96). The pipeline knows only the router and the composers.

### 4.2 Projects

| Project | Target | References | Packages | Content |
|---|---|---|---|---|
| `src/MsgHatch.Domain` | `net10.0` | none | `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Options.ConfigurationExtensions`, `Microsoft.Extensions.Diagnostics` | Failures, identities, the tenant key, the routing contract, the registry reader and writer, delivery types, persistence, the in-memory stores, pipeline, inbox, outbox, dispatch, telemetry, and the DI builder |
| `src/MsgHatch.Api.Contracts` | `net10.0` | `MsgHatch.Domain` | none | The error metadata of the HTTP APIs: `ErrorTypeBody` and its JSON context `ApiErrorJson` (D227, D233), the bodies of the registry API with their JSON context `RegistryEndpointJson` (D270), and the bodies of the delivery API with their JSON context `DeliveryEndpointJson` (D291) |
| `src/MsgHatch.Api.Client` | `net10.0` | `MsgHatch.Api.Contracts`, and `MsgHatch.SourceGeneration` as an analyzer | `Microsoft.Extensions.Http` | The HTTP client of the core APIs: `IRegistryQueries` and `IRegistryCommands` over HTTP with `UseRegistryApiClient`, and `IDeliveryQueries` and `IDeliveryCommands` over HTTP with `UseDeliveryApiClient` (D271, D283, D294, section 4.23) |
| `src/MsgHatch.Subscriptions.Api.Contracts` | `net10.0` | `MsgHatch.Subscriptions`, `MsgHatch.Api.Contracts` | none | The bodies of the subscription HTTP API, their JSON context, `SettingsJson`, and `JsonChannelSettings`, with no HTTP concern (D227, D228, D229) |
| `src/MsgHatch.Templates.Api.Contracts` | `net10.0` | `MsgHatch.Templates`, `MsgHatch.Api.Contracts` | none | The bodies of the template HTTP API and their JSON context, with no HTTP concern (D227, D228) |
| `src/MsgHatch.SourceGeneration` | `netstandard2.0` | none | `Microsoft.CodeAnalysis.CSharp`, `Microsoft.CodeAnalysis.Analyzers` | The generator of placeholder writers (D18), and the generator of the HTTP API clients (D236, section 4.23) |
| `src/MsgHatch.Templates` | `net10.0` | `MsgHatch.Domain`, and `MsgHatch.SourceGeneration` as an analyzer | none | The template library, which knows no channel (D212): the template engine contract, the template stores, the template selector, the placeholder engine, and the template setup methods |
| `src/MsgHatch.Channels.Email` | `net10.0` | `MsgHatch.Templates` | none | The email channel, the email composer as the thin layer on the template library (D215), and `IEmailClient` |
| `src/MsgHatch.Channels.Email.AmazonSes` | `net10.0` | `MsgHatch.Channels.Email` | `AWSSDK.SimpleEmailV2` | The SES implementation of `IEmailClient` |
| `src/MsgHatch.Templates.Liquid` | `net10.0` | `MsgHatch.Templates` | `Fluid.Core` | The Liquid template engine, with its engine factory `LiquidTemplates.Engine` (D205, D216) |
| `src/MsgHatch.Templates.AspNetCore` | `net10.0` | `MsgHatch.Templates.Api.Contracts` | `<FrameworkReference Include="Microsoft.AspNetCore.App" />` | The HTTP endpoints of the template administration (D217, D224) |
| `src/MsgHatch.Templates.Api.Client` | `net10.0` | `MsgHatch.Templates.Api.Contracts`, and `MsgHatch.SourceGeneration` as an analyzer | `Microsoft.Extensions.Http` | The HTTP client of the template management API: `ITemplateQueries` and `ITemplateCommands` over HTTP, and `UseTemplateApiClient` (D227, D236, section 4.23) |
| `src/MsgHatch.Channels.Email.Resend` | `net10.0` | `MsgHatch.Channels.Email` | `Microsoft.Extensions.Http` | The Resend implementation of `IEmailClient` (D138) |
| `src/MsgHatch.Channels.SignalR` | `net10.0` | `MsgHatch.Domain` | `<FrameworkReference Include="Microsoft.AspNetCore.App" />` | The SignalR channel |
| `src/MsgHatch.Channels.Broker` | `net10.0` | `MsgHatch.Domain` | none | The broker channel, the broker composer and transport, and `IBrokerClient` |
| `src/MsgHatch.Channels.Broker.Wolverine` | `net10.0` | `MsgHatch.Channels.Broker` | `WolverineFx` | The Wolverine implementation of `IBrokerClient` (D134) |
| `src/MsgHatch.Channels.Broker.AmazonSns` | `net10.0` | `MsgHatch.Channels.Broker` | `AWSSDK.SimpleNotificationService` | The Amazon SNS implementation of `IBrokerClient`, the first direct broker client (D300 to D302) |
| `src/MsgHatch.Data.FileSystem` | `net10.0` | `MsgHatch.Domain` | none | The JSON file inbox and outbox stores (D26) |
| `src/MsgHatch.Data.Sqlite` | `net10.0` | `MsgHatch.Domain` | `Microsoft.Data.Sqlite` | The SQLite inbox and outbox stores (D55) |
| `src/MsgHatch.Data.EFCore` | `net10.0` | `MsgHatch.Domain` | `Microsoft.EntityFrameworkCore.Relational` | The EF Core stores on each relational provider, the dialect contract of a provider package, and the migration step (D120, D318, D320) |
| `src/MsgHatch.Data.EFCore.SqlServer` | `net10.0` | `MsgHatch.Data.EFCore` | `Microsoft.EntityFrameworkCore.SqlServer` | The SQL Server package of the EF Core stores: the claim with `READPAST`, the binary collation, the migrations of `MsgHatchDbContext`, and `UseEntityFrameworkSqlServerStores` (D318, D320) |
| `src/MsgHatch.Data.EFCore.PostgreSql` | `net10.0` | `MsgHatch.Data.EFCore` | `Npgsql.EntityFrameworkCore.PostgreSQL` | The PostgreSQL package of the EF Core stores: the claim with `SKIP LOCKED`, the collation `C`, the migrations of `MsgHatchDbContext`, and `UseEntityFrameworkPostgreSqlStores` (D318, D319, D320) |
| `src/MsgHatch.Data.EFCore.MySql` | `net10.0` | `MsgHatch.Data.EFCore` | `Microting.EntityFrameworkCore.MySql` | The MySQL package of the EF Core stores: the claim of the base package, the collation `utf8mb4_bin`, the tables with the prefix `MsgHatch`, the migrations of `MsgHatchDbContext`, and `UseEntityFrameworkMySqlStores` (D318, D320, D325, D326) |
| `src/MsgHatch.Data.MongoDb` | `net10.0` | `MsgHatch.Domain` | `MongoDB.Driver` | The MongoDB stores (D121), and the change stream dispatch signal between the hosts of one database (D303, D304) |
| `src/MsgHatch.Dispatch.NamedPipes` | `net10.0` | `MsgHatch.Domain` | none | The dispatch signal between the processes of one OS user on one machine, with named pipes (D188, D189) |
| `src/MsgHatch.Dispatch.RabbitMq` | `net10.0` | `MsgHatch.Domain` | `RabbitMQ.Client` | The dispatch signal between the hosts of one system on different machines, through a fanout exchange of RabbitMQ (D305) |
| `src/MsgHatch.AspNetCore` | `net10.0` | `MsgHatch.Domain`, `MsgHatch.Api.Contracts` | `<FrameworkReference Include="Microsoft.AspNetCore.App" />` | The HTTP endpoints of the delivery management API (D107) and of the registry administration (D117, D270) |
| `src/MsgHatch.Subscriptions` | `net10.0` | `MsgHatch.Domain` | none | The subscription router, the subscription model, the resolution, the management API, the subscription cache wrapper, and the routing verifier (D97) |
| `src/MsgHatch.Subscriptions.Data.FileSystem` | `net10.0` | `MsgHatch.Subscriptions`, `MsgHatch.Data.FileSystem` | none | The JSON file subscription store |
| `src/MsgHatch.Subscriptions.Data.Sqlite` | `net10.0` | `MsgHatch.Subscriptions`, `MsgHatch.Data.Sqlite` | none | The SQLite subscription store |
| `src/MsgHatch.Subscriptions.Data.EFCore` | `net10.0` | `MsgHatch.Subscriptions`, `MsgHatch.Data.EFCore` | none | The EF Core subscription store |
| `src/MsgHatch.Subscriptions.Data.EFCore.SqlServer` | `net10.0` | `MsgHatch.Subscriptions.Data.EFCore`, `MsgHatch.Data.EFCore.SqlServer` | none | The SQL Server package of the EF Core subscription store: the migrations of `MsgHatchSubscriptionDbContext` and `UseEntityFrameworkSqlServerSubscriptionStore` (D318, D320) |
| `src/MsgHatch.Subscriptions.Data.EFCore.PostgreSql` | `net10.0` | `MsgHatch.Subscriptions.Data.EFCore`, `MsgHatch.Data.EFCore.PostgreSql` | none | The PostgreSQL package of the EF Core subscription store: the migrations of `MsgHatchSubscriptionDbContext` and `UseEntityFrameworkPostgreSqlSubscriptionStore` (D318, D320) |
| `src/MsgHatch.Subscriptions.Data.EFCore.MySql` | `net10.0` | `MsgHatch.Subscriptions.Data.EFCore`, `MsgHatch.Data.EFCore.MySql` | none | The MySQL package of the EF Core subscription store: the migrations of `MsgHatchSubscriptionDbContext` and `UseEntityFrameworkMySqlSubscriptionStore` (D320, D325, D326) |
| `src/MsgHatch.Subscriptions.Data.MongoDb` | `net10.0` | `MsgHatch.Subscriptions`, `MsgHatch.Data.MongoDb` | none | The MongoDB subscription store |
| `src/MsgHatch.Subscriptions.Caching.Hybrid` | `net10.0` | `MsgHatch.Subscriptions` | `Microsoft.Extensions.Caching.Hybrid` | The HybridCache backend of the subscription cache (D39) |
| `src/MsgHatch.Subscriptions.Caching.FusionCache` | `net10.0` | `MsgHatch.Subscriptions` | `ZiggyCreatures.FusionCache` | The FusionCache backend of the subscription cache (D39) |
| `src/MsgHatch.Subscriptions.AspNetCore` | `net10.0` | `MsgHatch.Subscriptions.Api.Contracts` | `<FrameworkReference Include="Microsoft.AspNetCore.App" />` | The HTTP endpoints of the subscription management API (D54) |
| `src/MsgHatch.Subscriptions.Api.Client` | `net10.0` | `MsgHatch.Subscriptions.Api.Contracts`, and `MsgHatch.SourceGeneration` as an analyzer | `Microsoft.Extensions.Http` | The HTTP client of the subscription management API: `ISubscriptionQueries`, `ISubscriptionCommands`, and `IRoutingVerifier` over HTTP, and `UseSubscriptionApiClient` (D227, D236, section 4.23) |
| `src/MsgHatch.AdminUI` | `net10.0`, `Microsoft.NET.Sdk.Razor` | `MsgHatch.Api.Contracts` | `Microsoft.AspNetCore.Components.Web` | The parts that both admin UIs use: the list layout, the pager, the delete dialog, and the text of each error type (D239, section 4.24), and the pages of the channels and the message types with their edit pages (D272, D273, D287) |
| `src/MsgHatch.Templates.AdminUI` | `net10.0`, `Microsoft.NET.Sdk.Razor` | `MsgHatch.Templates.Api.Contracts`, `MsgHatch.AdminUI` | none | The template list and edit pages, and the editor extension point (D239, D240, section 4.24) |
| `src/MsgHatch.Templates.Liquid.AdminUI` | `net10.0`, `Microsoft.NET.Sdk.Razor` | `MsgHatch.Templates.AdminUI` | none | The Liquid editor of the template admin UI (D242, section 4.24) |
| `src/MsgHatch.Subscriptions.AdminUI` | `net10.0`, `Microsoft.NET.Sdk.Razor` | `MsgHatch.Subscriptions.Api.Contracts`, `MsgHatch.AdminUI` | none | The pages of the subscription management API and of the routing verification (D239, section 4.24) |
| `tests/{Project}.Tests.{Level}` | `net10.0`, `Exe` | The production project under test, and the test project with its contract base classes | The test packages of section 5.5 | One test project for each production project and each level (D66, section 5.5) |
| `examples/ExampleApp` | `net10.0`, `Microsoft.NET.Sdk.Web` | The `src` projects of the finished phases (roadmap), and `MsgHatch.SourceGeneration` as an analyzer | `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.OpenTelemetryProtocol`, `Microsoft.AspNetCore.Authentication.JwtBearer` (D158), `WolverineFx.RuntimeCompilation` for the code of its Wolverine handler | The subscription example end to end, as a web app |
| `examples/AdminUiExample` | `net10.0`, `Microsoft.NET.Sdk.Web` | Both API client packages, both admin UI packages, and the Liquid editor | none | The Interactive Server host of the admin UIs, which calls the APIs of `ExampleApp` (D239, section 4.24) |
| `examples/SchemaMigrator` | `net10.0` | `MsgHatch.Data.EFCore.SqlServer`, `MsgHatch.Subscriptions.Data.EFCore.SqlServer`, `MsgHatch.Data.EFCore.PostgreSql`, `MsgHatch.Subscriptions.Data.EFCore.PostgreSql`, `MsgHatch.Data.EFCore.MySql`, `MsgHatch.Subscriptions.Data.EFCore.MySql` | `Microsoft.Extensions.Hosting` | The console app of the migration step of a pipeline, which applies the MsgHatch migrations on SQL Server, on PostgreSQL, or on MySQL, or writes their script (D320) |
| `examples/SignalRDemo` | `net10.0`, `Microsoft.NET.Sdk.Web` | `MsgHatch.Channels.SignalR` | `Microsoft.AspNetCore.Authentication.JwtBearer` | The SignalR demo dashboard of the tenants, the companies, and the users, with Razor pages, plain JavaScript, and the `@microsoft/signalr` client in `wwwroot/lib` (D316) |

An analyzer reference is `<ProjectReference Include="..." OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`. The `MsgHatch.Templates` package includes the generator assembly under `analyzers/dotnet/cs`, so that a consumer gets the generator with the template library, also through the email package.

### 4.3 The generic boundary

The pipeline is generic in `TEvent`. The dispatchers, the stores, and the transports are not generic. Two closed generic classes connect them to the typed components. The DI registration creates each class one time, so no reflection runs for each message:

- `MessageRegistration<TEvent> : IMessageHandler` holds the channel registrations of `TEvent`. The inbox processor uses it to run the pipeline for a claimed `ReceivedMessage<TEvent>`. The pipeline gets `IMessageRouter<TEvent>` from the DI scope of the event, with the tenant key of the event (D126).
- `ChannelRegistration<TEvent, TEnvelope> : IChannelRegistration<TEvent>` gets the composer of the channel with the same tenant key, calls it, and writes each outgoing message into the result array of the event at the position of its route (D280).

The outbox dispatcher needs no generic class. It gives the claimed `OutgoingMessage` to the `ITransport` of its channel, and the transport reads the envelope from `OutgoingMessage.Content` (D129).

Lookups use a `FrozenDictionary` by content type and version.

### 4.4 Data types

Failures (`MsgHatch.Domain.Failures`, D50):

```csharp
public sealed record ErrorType(int Code, string Name, bool IsTransient); // equal and hashed by Code only (D238)

public sealed record Error(ErrorType Type, string? Message = null, Exception? Exception = null)
{
    public TimeSpan? RetryAfter { get; init; } // the wait of the provider before the next try, or null (D306)
}

public readonly struct ErrorOr<T>
{
    private readonly T _value;
    private readonly Error? _error;
    private readonly bool _hasValue;

    public T Value => _value;
    public Error? Error => _hasValue ? null : _error ?? new Error(Errors.Uninitialized);

    [MemberNotNullWhen(true, nameof(Error))]
    public bool IsError => !_hasValue;
    public bool IsSuccess => _hasValue;

    public static ErrorOr<T> ForValue(T value);
    public static ErrorOr<T> ForError(Error error);
    public static implicit operator ErrorOr<T>(T value);
    public static implicit operator ErrorOr<T>(Error error);
    public ErrorOr<T2> ThenIfSuccess<T2>(Func<T, ErrorOr<T2>> onSuccess);
}
```

A `default(ErrorOr<T>)` is an error with `Errors.Uninitialized`, so a forgotten value can never look like a success. The `Error` property of a `default` value allocates one `Error`. Callers pass static lambdas to `ThenIfSuccess`, so that no closure is allocated.

The static class `Errors` has one `static readonly ErrorType` field for each row:

| Field | Code | Transient | Use |
|---|---|---|---|
| `ValidationFailed` | 400 | no | Invalid input or configuration at run time |
| `Rejected` | 403 | no | A provider rejected the message, for example SES `MessageRejectedException` |
| `NotFound` | 404 | no | No template, or no registration for a type |
| `Conflict` | 409 | no | A duplicate subscription, or an update with a revision that is not the stored revision |
| `Expired` | 410 | no | An outgoing message after its expiry |
| `SerializationFailed` | 415 | no | A stored record that the code cannot read |
| `TemplateInvalid` | 422 | no | A template with a syntax error or an unknown placeholder |
| `UnsupportedVersion` | 426 | yes | A stored version that is newer than the code (section 4.13) |
| `RateLimited` | 429 | yes | A provider limit, for example SES `TooManyRequestsException` |
| `OperationCancelled` | 460 | yes | A cancelled operation. No try is recorded for it. |
| `Uninitialized` | 499 | no | A `default(ErrorOr<T>)` |
| `Exception` | 500 | yes | An unexpected exception at a boundary (section 4.12) |
| `Unavailable` | 503 | yes | A provider or network that is not available, for example `HttpRequestException` |

Identities (`MsgHatch.Domain.Identity`):

```csharp
public readonly record struct MessageType(string Name);
public readonly record struct ChannelId(string Name);

public readonly record struct UserId
{
    public UserId(string value);
    public string Value { get; }
    public static ErrorOr<UserId> Create(string? value);
    public static UserId From<T>(T id) where T : ISpanFormattable;
    public bool TryAs<T>([MaybeNullWhen(false)] out T id) where T : ISpanParsable<T>;
}

public readonly record struct CompanyId
{
    public CompanyId(string value);
    public string Value { get; }
    public static ErrorOr<CompanyId> Create(string? value);
    public static CompanyId From<T>(T id) where T : ISpanFormattable;
    public bool TryAs<T>([MaybeNullWhen(false)] out T id) where T : ISpanParsable<T>;
}

public readonly record struct TenantId
{
    public TenantId(string value);
    public string Value { get; }
    public static ErrorOr<TenantId> Create(string? value);
    public static TenantId From<T>(T id) where T : ISpanFormattable;
    public bool TryAs<T>([MaybeNullWhen(false)] out T id) where T : ISpanParsable<T>;
}

public readonly record struct TenantKey
{
    public TenantId? Tenant { get; }
    public static TenantKey None { get; }
    public static TenantKey For(TenantId? tenant);
}
```

A message type name, a content type name, and a channel name contain only ASCII letters, ASCII digits, and `_`, and start with an ASCII letter. The registration rejects other names.

User ids, company ids, and tenant ids (D73, D114):
- A `UserId`, a `CompanyId`, or a `TenantId` holds a string, so that each consumer keeps its own id type, for example `int`, `long`, `Guid`, or a string.
- A user id and a company id are unique only inside one tenant. The tenant services of one tenant see only the ids of that tenant (D126). A null tenant means "no tenant", for a setup with one tenant.
- `TenantKey` is the key of the tenant services. `TenantKey.For(null)` is equal to `TenantKey.None`. Two keys are equal when their tenants are equal.
- `From<T>` formats the id with `TryFormat`, the default format of its type, and `CultureInfo.InvariantCulture`. So `UserId.From(42)` gives `"42"`, and `CompanyId.From(guid)` gives the lowercase `D` format of the `Guid`. One id always gives the same text.
- `TryAs<T>` parses the text back with the invariant culture. It returns false when the text is not a valid `T`.
- The constructor takes a string id as it is, and throws `ArgumentException` for an invalid value, because that is a programmer error (section 4.12). `Create` checks a string from outside, for example from an HTTP request, and returns `Errors.ValidationFailed` for an invalid value. A consumer that uses string ids must give each id in the same form each time.
- The value is not empty, has at most 256 characters, and has no control characters. Two ids are equal when their texts are equal with ordinal comparison.
- Cache keys, deterministic outgoing message ids, store queries, and SignalR group names use the text, so a consumer must map each id in the same way each time.

Routing (`MsgHatch.Domain.Routing`, D95):

```csharp
public enum DeliveryTargetKind { User, Company, Tenant }

public readonly record struct DeliveryTarget(DeliveryTargetKind Kind, string Id);

public abstract record ChannelSettings;

public interface ILanguageSettings
{
    string? Language { get; }
}

[Flags]
public enum ChannelFeatures { None = 0, Languages = 1, RichText = 2, RawData = 4, GroupDelivery = 8 }

public abstract record ChannelDescriptor(
    ChannelId Id, string Name, ChannelFeatures Features, string? Description = null, bool Enabled = true)
{
    public abstract Type SettingsType { get; }
}

public sealed record ChannelDescriptor<TSettings>(
    ChannelId Id, string Name, ChannelFeatures Features, string? Description = null, bool Enabled = true)
    : ChannelDescriptor(Id, Name, Features, Description, Enabled)
    where TSettings : ChannelSettings
{
    public override Type SettingsType => typeof(TSettings);
}

public abstract record RouteTrace;

public readonly record struct MessageRoute(
    DeliveryTarget Target, ChannelId Channel, ChannelSettings Settings, string Language, string? Address,
    string? TemplateScope, RouteTrace? Trace);

public sealed record MessageTypeDescriptor(
    MessageType Type, Type EventType, IReadOnlyList<ChannelId> Channels,
    string? DisplayName = null, string? Description = null, bool Enabled = true,
    IReadOnlyList<ChannelId>? DisabledChannels = null);
```

Route rules (D95, D98):
- A route is one delivery target on one channel. A delivery target is a user, a company, or all users of the tenant of the message. Its `Id` is the text of the `UserId` or of the `CompanyId`, or empty for a tenant target (D315).
- The settings have the settings type of the channel (D77).
- The language is a culture name that `CultureInfo.GetCultureInfo` knows. The composers use it for the template and for the formats.
- The address is the channel address of the target, or null. The composer of a channel that needs an address, for example email, fails a route with no address (section 4.8).
- The template scope is the key of a template override, or null for the templates with no scope.
- The trace tells why the router gave the route. It holds no address and no payload value, so the logs can contain it (D37). A route with no trace is valid.
- `MessageTypeDescriptor` describes one registered message type, with its event type and its channels. `IMessageRegistry` gives them, and the `ChannelDescriptor` of each channel (section 4.6).
- The code gives the type, the event type, the channels, the features, and the settings type. The registry settings can change only the name of a channel, the display name of a message type, the descriptions, `Enabled`, and `DisabledChannels` (D118, section 4.20). The in-memory reader gives the values of the code, with `Enabled` true and no disabled channels.

Subscriptions (`MsgHatch.Subscriptions`, D97):

```csharp
public enum SubscriptionLevel { Default, Company, User }

public readonly record struct SubscriptionOwner
{
    public SubscriptionLevel Level { get; }
    public CompanyId? Company { get; }
    public UserId? User { get; }
    public static SubscriptionOwner Default { get; }
    public static SubscriptionOwner ForCompany(CompanyId company);
    public static SubscriptionOwner ForUser(UserId user);
}

public enum SubscriptionKind { Channel, OptOut }

public enum RecipientKind { ScopeUsers, Company, NamedUsers }

public sealed record SubscriptionRecipients(RecipientKind Kind, IReadOnlyList<UserId> Users)
{
    public static SubscriptionRecipients ScopeUsers { get; }
    public static SubscriptionRecipients Company { get; }
    public static SubscriptionRecipients ForUsers(IReadOnlyList<UserId> users);
}

public sealed record Subscription(
    Guid Id,
    int Revision,
    SubscriptionOwner Owner,
    MessageType Type,
    SubscriptionKind Kind,
    ChannelId? Channel,
    ChannelSettings? Settings,
    SubscriptionRecipients Recipients,
    bool Mandatory,
    DateTime CreatedAtUtc,
    DateTime LastModifiedAtUtc)
{
    public bool IsFixed { get; }
}

public sealed record SubscriptionDraft(
    SubscriptionOwner Owner, MessageType Type, SubscriptionKind Kind, ChannelId? Channel, ChannelSettings? Settings,
    SubscriptionRecipients? Recipients = null, bool Mandatory = false);

public readonly struct NotificationScope
{
    public ReadOnlyMemory<UserId> Users { get; }
    public CompanyId? Company { get; }
    public static NotificationScope ForUsers(ReadOnlyMemory<UserId> users);
    public static NotificationScope ForUsers(IReadOnlyList<string> users);
    public static NotificationScope ForCompany(CompanyId company);
}

public enum TargetSource { ScopeUser, CompanyGroup, Company, NamedUser }
public enum LanguageSource { ChannelSettings, UserUiLanguage, CompanyDefaultLanguage, LibraryDefault }
public enum AddressSource { None, UserProfile, CompanyProfile }
public enum SkipReason { Overridden, Locked, Merged }

public readonly record struct SubscriptionSource(
    Guid Id, SubscriptionOwner Owner, RecipientKind Recipients, bool Mandatory);

public readonly record struct SkippedSubscription(SubscriptionSource Subscription, SkipReason Reason);

public sealed record ResolutionTrace(
    SubscriptionSource Applied, TargetSource Target, LanguageSource Language, AddressSource Address,
    IReadOnlyList<SkippedSubscription> Skipped)
    : RouteTrace;

public readonly record struct OptOutDecision(
    DeliveryTarget Target, ChannelId Channel, SubscriptionSource OptOut, IReadOnlyList<SkippedSubscription> Skipped);

public sealed record SubscriptionResolution(
    IReadOnlyList<MessageRoute> Deliveries, IReadOnlyList<OptOutDecision> OptOuts);

public sealed record UserProfile(
    UserId Id, CompanyId? Company, string? UiLanguage, IReadOnlyDictionary<ChannelId, string> Addresses);

public sealed record CompanyProfile(
    CompanyId Id, string? DefaultLanguage, IReadOnlyDictionary<ChannelId, string> Addresses);

public sealed record SubscriptionQuery(
    MessageType? Type = null, CompanyId? Company = null, UserId? User = null, SubscriptionLevel? Level = null);

public sealed record MessageTypeQuery(
    MessageType? Type = null, CompanyId? Company = null, UserId? User = null);

public sealed record MessageTypeState(
    MessageType Type, IReadOnlyList<ChannelId> Channels, IReadOnlyList<Subscription> AppliedSubscriptions);
```

Subscription rules (D72, D88, D89):
- A subscription of the kind `Channel` has a channel and channel settings of the settings type of that channel. A subscription of the kind `OptOut` has no settings. Its channel is one channel, or null for all channels.
- For one owner, one message type, one channel, and one recipient kind, at most one subscription exists. An opt-out for all channels excludes the other subscriptions for the scope users of the same owner and message type.
- A default subscription has no company and no user. A company subscription has only a company. A user subscription has only a user.
- An owner has no tenant. The subscription store of a tenant holds only the subscriptions of that tenant (D116). So `SubscriptionOwner.Default` is the default level of the tenant of the store, and two owners with the same ids in two tenants are in two stores.
- `SubscriptionRecipients`:
  - `ScopeUsers` goes to the scope users of the owner (section 2). A user subscription and an opt-out always have these recipients.
  - `Company` goes to the company that the event concerns. Only the default and the company levels can have it.
  - `NamedUsers` goes to the users in `Users`: 1 to 100 different user ids. Only the default and the company levels can have it. A company subscription names only users of that company.
  - For `ScopeUsers` and `Company`, `Users` is empty. `Equals` compares `Kind` and the users in their order, so that a subscription read back from a store is equal to the stored one.
- `Mandatory` is true only for a channel subscription for the scope users at the default or the company level (D88).
- `IsFixed` is true for a mandatory subscription and for a subscription for the company or for named users. No lower level overrides a fixed subscription (D89).
- `Revision` starts at 1 and increases with each update (D79).
- `NotificationScope.ForUsers` with a duplicate user id gives that user one time. The resolution removes the duplicate.
- The subscription router sets the template scope of an effective subscription to the text of the company of the delivery target (D80).
- `UserProfile.Addresses` has one address for each channel that needs one, for example the email address under `EmailChannel.Id`. A SignalR user target uses the user id, so it needs no address.
- `CompanyProfile.Addresses` has the addresses of the company itself, for example the email address of the company administrator. A company target on a channel with the feature `GroupDelivery` needs no address. A company target on another channel, for example email, uses the address of the company for that channel.

Resolution trace (D92, D98). The subscription router gives each effective subscription a `ResolutionTrace` as its route trace. The trace tells where each decision comes from, so that a person can see why a target got a message on a channel, in a language, at an address:
- `Applied` is the subscription that gave the delivery: its id, its owner with the level, its recipient kind, and its mandatory flag. The channel and the channel settings come from it, whole. The settings of one channel never merge from several subscriptions.
- `Target` tells why the target gets the message: a user of the scope, the SignalR group of a company scope, the company of a subscription for the company, or a named user.
- `Language` tells which rule of the routing procedure (section 4.17) gave the language: the channel settings of `Applied`, the UI language of the user, the default language of the company, or `MsgHatchOptions.DefaultLanguage`.
- `Address` tells where the address comes from: the user profile, the company profile, or `None` when the profile has no address for the channel, for example for a SignalR target.
- `Skipped` lists each other subscription for the same target and channel, in level order from the default down, with the rule that removed it:
  - `Overridden`: a more specific level has a subscription or an opt-out for the channel (D72).
  - `Locked`: a mandatory subscription of a higher level applies to the channel (D88).
  - `Merged`: another subscription gave the same target and channel, and comes first in the merge order (D91).
- An opt-out that applies gives no delivery. The resolver then records an `OptOutDecision` with the target, the channel, the opt-out, and its own `Skipped` list. So the trace also tells why a target got no message.
- `SubscriptionResolver.Resolve(channels, scope, subscriptions, users, companies, defaultLanguage)` is static and does no I/O (D146). It returns a `SubscriptionResolution` with the deliveries and the opt-out decisions. An empty `Skipped` list is `Array.Empty<SkippedSubscription>()`, so a trace with nothing skipped allocates nothing more.
- The trace holds ids, levels, and sources only. It has no address and no payload value, so the logs can contain it (D37).

Delivery (`MsgHatch.Domain.Delivery`):

```csharp
public enum DeliveryStatus { Pending, Done, Failed, Cancelled }

public readonly record struct DeliveryState(
    Guid Id,
    DeliveryStatus Status,
    int TryCount,
    int MaxTryCount,
    DateTime CreatedAtUtc,
    DateTime? LastTryAtUtc,
    DateTime NextTryAtUtc,
    DateTime? LeaseUntilUtc,
    DateTime? ExpiresAtUtc,
    string? LastError);

[Flags]
public enum MessageFlags { None = 0, Test = 1 }

public readonly record struct MessageContext(Guid MessageId, TenantId? Tenant = null, MessageFlags Flags = MessageFlags.None);

public abstract record ReceivedMessage(
    int SchemaVersion, DeliveryState Delivery, TenantId? Tenant, MessageType Type, int ContentVersion,
    MessageFlags Flags, string? TraceParent);

public sealed record ReceivedMessage<TEvent>(
    int SchemaVersion, DeliveryState Delivery, TenantId? Tenant, MessageType Type, int ContentVersion,
    MessageFlags Flags, string? TraceParent, TEvent Event)
    : ReceivedMessage(SchemaVersion, Delivery, Tenant, Type, ContentVersion, Flags, TraceParent);

public abstract record OutgoingMessage(
    int SchemaVersion, DeliveryState Delivery, Guid SourceMessageId, TenantId? Tenant, MessageType Type,
    ChannelId Channel, DeliveryTarget Target, string ContentType, int ContentVersion, MessageFlags Flags,
    string? TraceParent, RouteTrace? Trace)
{
    public abstract object Content { get; }
}

public sealed record OutgoingMessage<TEnvelope>(
    int SchemaVersion, DeliveryState Delivery, Guid SourceMessageId, TenantId? Tenant, MessageType Type,
    ChannelId Channel, DeliveryTarget Target, string ContentType, int ContentVersion, MessageFlags Flags,
    string? TraceParent, RouteTrace? Trace, TEnvelope Envelope)
    : OutgoingMessage(
        SchemaVersion, Delivery, SourceMessageId, Tenant, Type, Channel, Target, ContentType, ContentVersion,
        Flags, TraceParent, Trace)
    where TEnvelope : class
{
    public override object Content => Envelope;
}

public enum PublishOutcome { Succeeded, TransientError, PermanentError, Expired, Suppressed, Cancelled }

public sealed record PublishTry(
    Guid OutgoingMessageId, int TryNumber, DateTime StartedAtUtc, DateTime FinishedAtUtc,
    PublishOutcome Outcome, string? ProviderMessageId, int? ErrorCode, string? ErrorName, string? ErrorMessage);

public readonly record struct PublishReceipt(string? ProviderMessageId);
```

The current `SchemaVersion` of all library types is 1.

`MessageContext` is the input of the caller for one event: the message id, the tenant, and the flags (D115). The received message and each outgoing message keep the tenant. The pipeline and the inbox processor resolve the tenant services with it, and the dispatch stores and the transports read it from the message (D126, D127).

`OutgoingMessage.Content` is the channel envelope as an object, so that a transport and a store need no generic type (D129).

`TraceParent` is the trace context of the span that made the message, or null (D132, section 4.21).

`OutgoingMessage.Type` and `OutgoingMessage.Target` come from the event and the route, so that a store can filter and count the messages by message type and by target (D113). `Cancelled` is the last member of `DeliveryStatus` and of `PublishOutcome`, as the versioning rules require (D110, section 4.13).

`OutgoingMessage.Trace` is the trace of the route that gave the message (D98). The outbox keeps it, so that a stored message tells later why it went to its target. For the subscription router, it is the `ResolutionTrace`, which also tells why the message has its language and its address (D92).

Pipeline results (`MsgHatch.Domain.Pipeline` and `MsgHatch.Domain.Dispatch`):

```csharp
public sealed record NotificationResult(
    int Stored, int Failed, IReadOnlyList<Error> Errors, int Skipped = 0);

public readonly record struct DispatchSummary(int Claimed, int Succeeded, int Retried, int Failed);

public readonly record struct RouteResult(MessageRoute Route, OutgoingMessage? Message, Error? Error);

public sealed record ClaimResult<T>(IReadOnlyList<T> Messages, DateTime? NextDueAtUtc, ChannelDown? Down = null);

public enum ChannelDownReason { Errors, Operator }

public sealed record ChannelDown(
    ChannelId Channel, ChannelDownReason Reason, DateTime SinceUtc, DateTime? NextCheckAtUtc,
    int? ErrorCode, string? ErrorName, string? Note);

public readonly record struct DispatchTopic(string Name)
{
    public static DispatchTopic Inbox { get; }
    public static DispatchTopic ForChannel(ChannelId channel);
}

[Flags]
public enum MsgHatchRoles { None = 0, Consumer = 1, Publisher = 2 }
```

`DispatchTopic.Inbox` has the name `inbox`, and `ForChannel` gives the name `outbox:{Channel}`, for example `outbox:Email` (D102). `MsgHatchRoles` is in `MsgHatch.Domain.DependencyInjection` (D105).

`ChannelDown` is the down state of one channel in the outbox dispatch store, for all publisher instances of the channel (D106):
- `Reason` is `Errors` when a dispatch loop found transient errors in a row, and `Operator` when a person paused the channel.
- `SinceUtc` is the start of the down state. `NextCheckAtUtc` is the time of the next health check, only for the reason `Errors`.
- `ErrorCode` and `ErrorName` belong to the last transient error, only for the reason `Errors`. `Note` is the text of the person, at most 500 characters, only for the reason `Operator`.
- A channel with no down state is up. `ClaimResult.Down` is the down state of the claimed channel, or null.

`NotificationResult.Errors` is an empty array when no message failed. `Skipped` counts the routes on a disabled channel (D118). For a disabled message type, the pipeline calls no router, and all counts are 0. `RouteResult` is the result of one route of a preview (D84, D100): the outgoing message, or the error of that route.

Test messages (D82 to D84):
- `MessageFlags.Test` marks a test message. The flag goes from the received message to each outgoing message. The stores persist it, so that `IDeliveryCommands.DeleteTestDataAsync` can remove all test data after a test, and analytics can leave test data out.
- `INotificationService.PreviewAsync` runs steps 3 to 5 of the pipeline, and stores and sends nothing. It returns one `RouteResult` for each route (D84).
- A test message goes to its channel as `TestDeliveryOptions` of that channel say (section 4.8).

Routing verification (D85, `MsgHatch.Subscriptions.Verification`):

```csharp
public sealed record RoutingVerificationRequest(
    IReadOnlyList<UserId> TestUsers, IReadOnlyList<string> Languages, MessageType? Type = null);

public enum RoutingCheckOutcome { Passed, Failed, Skipped }

public sealed record RoutingCheck(
    MessageType Type, string? TestCase, SubscriptionOwner? Owner, DeliveryTarget? Target, ChannelId? Channel,
    string? Language, RoutingCheckOutcome Outcome, int? ErrorCode, string? ErrorName, ResolutionTrace? Trace);

public sealed record RoutingVerificationReport(IReadOnlyList<RoutingCheck> Checks)
{
    public int Passed { get; }
    public int Failed { get; }
    public int Skipped { get; }
}
```

Email (`MsgHatch.Channels.Email`):

```csharp
public static class EmailChannel
{
    public static readonly ChannelId Id = new("Email");
    public static readonly ChannelDescriptor<EmailChannelSettings> Descriptor =
        new(Id, "Email", ChannelFeatures.Languages | ChannelFeatures.RichText);
}

public enum EmailFormat { Html, PlainText }

public sealed record EmailChannelSettings(string? Language = null, EmailFormat Format = EmailFormat.Html)
    : ChannelSettings, ILanguageSettings;

public sealed record EmailMessage(
    string To, string Subject, string? HtmlBody, string? TextBody = null,
    string? From = null, IReadOnlyList<string>? Cc = null, IReadOnlyList<string>? Bcc = null);

```

An `EmailMessage` has at least one body. The format `PlainText` sends only the text body. The `EmailMessage` is the channel envelope of each email, with no copy of the payload (D17).

SignalR (`MsgHatch.Channels.SignalR`) and the broker (`MsgHatch.Channels.Broker`):

```csharp
public static class SignalRChannel
{
    public static readonly ChannelId Id = new("SignalR");
    public static readonly ChannelDescriptor<SignalRChannelSettings> Descriptor = new(
        Id, "UI notification",
        ChannelFeatures.Languages | ChannelFeatures.RawData | ChannelFeatures.GroupDelivery);
}

public sealed record SignalRChannelSettings(string? Language = null) : ChannelSettings, ILanguageSettings;

public interface ISignalREnvelope
{
    object Payload { get; }
}

public sealed record SignalREnvelope<TPayload>(TPayload Payload) : ISignalREnvelope
    where TPayload : class
{
    object ISignalREnvelope.Payload => Payload;
}

public static class BrokerChannel
{
    public static readonly ChannelId Id = new("Broker");
    public static readonly ChannelDescriptor<BrokerChannelSettings> Descriptor = new(
        Id, "Broker message", ChannelFeatures.RawData | ChannelFeatures.GroupDelivery);
}

public sealed record BrokerChannelSettings(IReadOnlyDictionary<string, string>? Headers = null) : ChannelSettings;

public interface IBrokerEnvelope
{
    object Payload { get; }
    IReadOnlyDictionary<string, string>? Headers { get; }
}

public sealed record BrokerEnvelope<TPayload>(TPayload Payload, IReadOnlyDictionary<string, string>? Headers = null)
    : IBrokerEnvelope
    where TPayload : class
{
    object IBrokerEnvelope.Payload => Payload;
}
```

The SignalR transport sends `ISignalREnvelope.Payload` with the message type name as the method name, to the target of `OutgoingMessage.Target`. A user target goes to the SignalR user of `SignalRUsers.ForUser`, a company target to the group of `SignalRGroups.ForCompany`, and a tenant target to the group of `SignalRGroups.ForTenant` (D315). The broker transport gives `IBrokerEnvelope.Payload` and the headers to `IBrokerClient`, which publishes the payload with its run-time type (D134). So no envelope copies the target or the method (D17).

Channel features (D77):

| Channel | Languages | Rich text | Raw data | Group delivery |
|---|---|---|---|---|
| Email | yes | yes, `EmailFormat.Html` | no | no |
| UI notification (SignalR) | yes | no | yes, the payload as JSON | yes, one message to the SignalR group of a company or of a tenant (D315) |
| Broker message | no | no | yes, the payload as it is | yes, one message with the company target |

A null language in channel settings means the UI language of the user, or the default language of the company for a company target. The language of channel settings is `ILanguageSettings.Language`. Settings that do not implement `ILanguageSettings`, for example `BrokerChannelSettings`, have no language (D145).

Options (D49, D63, D64, D65). Each options class is a `sealed class` with a public parameterless constructor, a `get; set;` property for each setting, the default value in the property initializer, and a `public const string SectionKey`, the key of its section under the root of the configuration sections (D329). `MsgHatchOptions` has `DefaultSectionKey` in its place, the default root `MsgHatch`, which the parameter `configSectionPath` of `AddMsgHatch` replaces (D328). A `[OptionsValidator]` partial class next to it, for example `DispatchOptionsValidator`, validates the attributes. A rule across properties is in `IValidatableObject.Validate`.

The column Section gives the path of the section, with `{root}` for the root of `AddMsgHatch`, by default `MsgHatch`:

| Class | Section | Property | Default | Validation |
|---|---|---|---|---|
| `MsgHatchOptions` | `{root}` | `DefaultLanguage` | `"en"` | `[Required]`. The culture must be known to `CultureInfo.GetCultureInfo`. |
| | | `EnableLogging` | `true` | none |
| | | `Roles` | `Consumer, Publisher` | `None` only with no message type (D105, D230) |
| `RetryOptions` (named: `Inbox`, the channel name, or the content type of a channel registration) | `{root}:Retry:{Name}` | `MaxTryCount` | 8 (SignalR: 3) | `[Range(1, 100)]` |
| | | `BaseDelay` | 5 seconds | `[Range(typeof(TimeSpan), "00:00:00.001", "01:00:00")]` |
| | | `MaxDelay` | 15 minutes | `[Range(typeof(TimeSpan), "00:00:01", "7.00:00:00")]`, and not less than `BaseDelay` |
| | | `Expiry` | none (SignalR: 5 minutes) | `[Range(typeof(TimeSpan), "00:00:01", "30.00:00:00")]` when it has a value. Null means no expiry (D94). The name `Inbox` allows no value (D142). |
| `DispatchOptions` (named: `Inbox` or `Outbox`) | `{root}:Dispatch:{Name}` | `MaxDegreeOfParallelism` | 8 | `[Range(1, 256)]` |
| | | `QueueCapacity` | 16 | `[Range(1, 4096)]`, and at least `MaxDegreeOfParallelism` |
| | | `Lease` | 60 seconds | `[Range(typeof(TimeSpan), "00:00:05", "01:00:00")]` |
| | | `LeaseSafetyMargin` | 10 seconds | `[Range(typeof(TimeSpan), "00:00:00", "00:30:00")]`, and less than `Lease` |
| | | `PollInterval` | 1 second | `[Range(typeof(TimeSpan), "00:00:00.010", "00:10:00")]` |
| | | `MaxPollInterval` | 30 seconds | `[Range(typeof(TimeSpan), "00:00:00.010", "01:00:00")]`, and not less than `PollInterval` |
| | | `EnablePolling` | `true` | none. False only where a signal reaches each loop, for example in one process (D102, D103). With `UseNamedPipeDispatchSignal`, `UseMongoDbDispatchSignal`, or `UseRabbitMqDispatchSignal`, false fails the startup validation (D190). |
| `ChannelDownOptions` (named: the channel name) | `{root}:ChannelDown:{Name}` | `ErrorThreshold` | 5 | `[Range(1, 1000)]` |
| | | `CheckInterval` | 30 seconds | `[Range(typeof(TimeSpan), "00:00:01", "01:00:00")]` |
| `TestDeliveryOptions` (named: the channel name) | `{root}:TestDelivery:{Name}` | `Mode` | `Suppress` | `TestDeliveryMode.Suppress`, `Deliver`, or `Redirect` |
| | | `RedirectTo` | none | `[MaxLength(256)]`. Required when `Mode` is `Redirect`. |
| `SubscriptionCacheOptions` | `{root}:SubscriptionCache` | `Expiration` | 1 minute | `[Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]` |
| | | `KeyPrefix` | `"msghatch:sub"` | `[Required]`, `[RegularExpression("^[A-Za-z0-9:_-]+$")]` |
| `FileStoreOptions` | `{root}:FileStores` | `RootPath` | none | `[Required]` |
| `SqliteStoreOptions` | `{root}:Sqlite` | `ConnectionString` | none | `[Required]`. It must not contain `{tenant}`, because one database for each tenant has its own setup method (D125). |
| `SqliteTenantStoreOptions` | `{root}:SqliteTenants` | `TenantConnectionString` | none | `[Required]`. It must contain `{tenant}` (D125). |
| | | `SystemConnectionString` | none | `[Required]`. It must not contain `{tenant}`. It names the database of the system storage, for the key `TenantKey.None`. |
| `EntityFrameworkStoreOptions` | `{root}:EntityFramework` | `Schema` | `"msghatch"` | `[Required]`, `[RegularExpression("^[A-Za-z_][A-Za-z0-9_]*$")]` |
| `MongoDbStoreOptions` | `{root}:MongoDb` | `ConnectionString` | none | `[Required]` |
| | | `DatabaseName` | `"msghatch"` | `[Required]`, `[RegularExpression("^[A-Za-z0-9_-]+$")]`, `[MaxLength(63)]`, because MongoDB 8.3.7 rejects a longer name (checked fact of phase 19 in the roadmap) |
| `NamedPipeSignalOptions` | `{root}:NamedPipeSignal` | `PipeNamePrefix` | none | `[Required]`, `[RegularExpression("^[A-Za-z0-9_-]{1,40}$")]` (D192) |
| `RabbitMqSignalOptions` | `{root}:RabbitMqSignal` | `Uri` | none | `[Required]`, and an absolute URI with the scheme `amqp` or `amqps` (D305) |
| | | `ExchangeName` | `"msghatch.dispatch"` | `[Required]`, `[RegularExpression("^(?!amq\.)[A-Za-z0-9_.:-]{1,255}$")]`, because the broker keeps the prefix `amq.` for its own exchanges. All hosts of one system use the same name. |
| `RegistryOptions` | `{root}:Registry` | `RefreshInterval` | 30 seconds | `[Range(typeof(TimeSpan), "00:00:01", "01:00:00")]` (D118) |
| `FileTemplateOptions` | `{root}:FileTemplates` | `RootPath` | none | `[Required]` |
| `AmazonSesOptions` | `{root}:AmazonSes` | `SenderEmail` | none | `[Required]`, `[EmailAddress]`, and `[RegularExpression("^[ -~]+$")]`, because SES accepts only 7-bit ASCII in an address. A domain with other letters is in Punycode. |
| | | `SenderName` | none | `[MaxLength(100)]` |
| | | `Region` | none, so that the AWS SDK selects the region from its configuration | `[RegularExpression("^[a-z]{2}(-[a-z]+)+-[0-9]$")]` when it has a value |
| `AmazonSnsOptions` | `{root}:AmazonSns` | `Region` | none, so that the AWS SDK selects the region from its configuration | `[RegularExpression("^[a-z]{2}(-[a-z]+)+-[0-9]$")]` when it has a value |
| | | `DefaultTopicArn` | none | The ARN of a standard SNS topic when it has a value. An ARN that ends with `.fifo` is a FIFO topic, and is not valid (D301). |
| | | `Topics` | empty | The ARN of a standard SNS topic for each key. The key is the name of a broker registration, for example `DEvent` (D300). `DefaultTopicArn` or `Topics` has at least one topic. |
| `ResendOptions` | `{root}:Resend` | `ApiKey` | none | `[Required]`. A key with the permission "sending access" is enough. |
| | | `SenderEmail` | none | `[Required]`, `[EmailAddress]`. Its domain must be verified in Resend. The test sender `onboarding@resend.dev` needs no verified domain, and reaches only the test addresses of Resend and the address of the Resend account. |
| | | `SenderName` | none | `[MaxLength(100)]` |

Each setup method registers its options in this order:
1. `services.AddOptions<DispatchOptions>(name).BindConfiguration(...)` with the path of the section under the root, `builder.SectionPathOf(SectionKey)` of the options class, so that a value from the configuration applies first. Each setup method keeps its own `BindConfiguration` call with a concrete type, because the configuration binding source generator binds only at such a call (D329).
2. `.Configure(configure)` with the `Action<TOptions>` of the setup method, so that a value from code wins.
3. `.ValidateOnStart()`, and the generated validator as `IValidateOptions<TOptions>` with `TryAddEnumerable`.

The code reads a setting only through `IOptions<TOptions>`, `IOptionsSnapshot<TOptions>`, or `IOptionsMonitor<TOptions>`.

Retry options of a channel registration (D87). Each channel registration of a message type registers a named `RetryOptions` with its content type as the name, for example `Email:OrderShippedEmail`. It repeats the steps of the channel options, and then adds its own steps:
1. `.Configure(...)` sets the default values of the channel, from the `retry` parameter of `AsChannel` (section 4.7).
2. `.BindConfiguration("{root}:Retry:{Channel}")` applies the section of the channel.
3. `.BindConfiguration("{root}:Retry:{ContentType}")` changes only the values that the section of the registration has. This section is inside the section of the channel, and the binding of the channel options ignores it.
4. `.Configure(...)` applies the action of `ChannelBuilder.WithRetry`, so that a value from code wins.
5. `.ValidateOnStart()`, with the same generated validator.

The steps do not read the channel options through `IOptionsMonitor<RetryOptions>`, because a configure step of `RetryOptions` that needs `IOptionsMonitor<RetryOptions>` is a circular dependency in the DI container.

The pipeline and the dispatcher read the retry options of a message by its content type, never by its channel. For example:

```csharp
.WithPayload<PasswordResetEmail, PasswordResetEmailLoader>(p => p.AsEmail(
    "PasswordResetEmail",
    c => c.WithRetry(o => { o.MaxTryCount = 3; o.Expiry = TimeSpan.FromMinutes(15); })))
```

```json
{ "MsgHatch": { "Retry": { "Email": { "MaxTryCount": 8, "WeeklyReportEmail": { "Expiry": "1.00:00:00" } } } } }
```

The example has the default root. A host that calls `AddMsgHatch(configSectionPath: "App:Messaging")` puts the same section under `App:Messaging`, for example `App:Messaging:Retry:Email`. Each section of the core and of the packages follows the root (D328). No setup method of a package has a path parameter. A host that needs one section in another place adds its own `services.Configure<TOptions>(configuration.GetSection(...))` after the setup call, because a later configure step wins.

Each subscription store package uses the options class of its core data package, for example `SqliteStoreOptions`, so one setting serves the outbox and the subscriptions (D59).

`AmazonSesOptions` gives the sender address `"{SenderName} <{SenderEmail}>"`. It gives only `SenderEmail` when `SenderName` is empty, or when `SenderEmail` already contains `<` and `>`. SES requires the MIME encoded-word syntax of RFC 2047 for a sender name with letters outside 7-bit ASCII. So such a `SenderName` becomes words of the form `=?UTF-8?B?…?=`, each of at most 75 characters, and a word ends only between letters. SES accepted such a sender name in a real send (checked in phase 20, 2026-11-14).

`ResendOptions` gives the same sender address with no encoding, because Resend encodes a sender name with letters outside 7-bit ASCII itself (checked with a real send, 2026-11-02).

Types that a consumer writes, for example:

```csharp
public sealed record OrderShipped(Guid OrderId, IReadOnlyList<string> CustomerIds);

[PlaceholderPayload]
public sealed partial record OrderShippedEmail(string CustomerName, string OrderNumber, DateTime ShippedAt);
```

### 4.5 Persistence

The stores receive the typed envelopes and serialize them with the serializer of their storage (D22). A stored record stays readable and editable in the tools of that storage. These are the four storage kinds:

| Storage | Outgoing message | Publish tries | Claim |
|---|---|---|---|
| JSON files in a folder | One file for each message, written with `MsgHatchJson`. `MsgHatch.Data.FileSystem` ships `FileSystemOutboxStore` (D26). | An array in the same file. | An atomic rename from the `pending` folder to the `claimed` folder. |
| NoSQL database, for example MongoDB | One BSON document for each message, converted from the JSON document of `MsgHatchJson` (D184), with each time as `{ d, t }` (D185), and a partial index on `channel`, the due time, and `_id` with the filter `status: "Pending"` (D104, D187). `MsgHatch.Data.MongoDb` ships `MongoDbOutboxStore` (D121). | An embedded array. The store adds a try with `$push` in the same update as the delivery state. | An atomic conditional update of each document, for example `findOneAndUpdate`. |
| SQLite | Columns for the delivery state, the content type, and the versions. A JSON text column for the channel envelope, written with `MsgHatchJson`. `MsgHatch.Data.Sqlite` ships `SqliteOutboxStore` (D55). | A JSON array column `Tries` of the message row. One `UPDATE` adds the try and changes the delivery state (D27). | An `UPDATE ... RETURNING` of the first due rows. SQLite allows one writer at a time, so the claim is atomic. |
| EF Core | Columns for the delivery state, the content type, and the versions. A JSON column for the channel envelope, written with `MsgHatchJson`. `MsgHatch.Data.EFCore` ships `EntityFrameworkOutboxStore` on each relational provider (D120, D318). | A JSON array column `Tries` of the message row, which the store writes in C#. One `ExecuteUpdate` with the condition of the stored `TryCount` adds the try and changes the delivery state (D27). | A LINQ read of the due candidates and one `ExecuteUpdate` with a claim token. On SQL Server, the dialect of `MsgHatch.Data.EFCore.SqlServer`: an `UPDATE` with an `OUTPUT` clause of the rows of a subquery that selects the first due rows with `TOP`, `ORDER BY NextTryAtUtc, Sequence` (D144), and the hints `ROWLOCK, READPAST, UPDLOCK` (D120). On PostgreSQL, the dialect of `MsgHatch.Data.EFCore.PostgreSql`: an `UPDATE ... FROM` a subquery that selects the first due rows with `ORDER BY NextTryAtUtc, Sequence`, `LIMIT`, and `FOR UPDATE SKIP LOCKED`, with `RETURNING` of the claimed rows. |

The inbox uses the same patterns, with no tries (D29).

The outbox dispatch store also keeps the state of each channel as insert-only rows (D106, D277). A SQL store has one row for each state, with the primary key `(Channel, Version)`, a NoSQL store one document with a unique index on the channel and the version, and the file store one file `{channel}/states/{version:D10}.json`, which it creates with a hard link on Linux and macOS (D312). A change is one insert of the next version, so of two parallel changes only the first adds its row, and no store needs a transaction. The latest row is the current state, and each claim of a channel reads it. Only `NextCheckAtUtc` of the latest row changes after its insert.

In-memory stores (D119). `InMemoryInboxStore` and `InMemoryOutboxStore` in `MsgHatch.Domain` keep the messages, the tries, and the state rows of the channels in dictionaries, with one lock for each store. They follow each rule of section 4.6 and pass the contract base classes, so they are also the reference for the other stores. `UseInMemoryStores()` selects them. They have the comment `ponytail: not durable and for one process, use a storage package for production`. `InMemoryInboxStore.GetMessages()` and `InMemoryOutboxStore.GetMessages()` give a copy of the stored messages of all tenants, in the order of their insert, for example for a test. `InMemoryOutboxStore.GetTries(Guid)` gives a copy of the publish tries of one outgoing message, in the order of their record, so that the test data abstraction of section 5.5 can read them.

Store parts (D127, D130). Each library store class has the two parts of one storage:
- The class implements the dispatch store, `IInboxDispatchStore` or `IOutboxDispatchStore`, for all tenants. Its setup method registers it as a thread-safe singleton.
- `For(TenantKey key)` of the class gives the tenant store of one key, `IInboxStore` or `IOutboxStore`. It is a small view that keeps the key, and reads and writes only the messages of its tenant.
- The setup method registers the tenant store with `KeyedService.AnyKey` through `For(key)`, and the unkeyed tenant store for `TenantKey.None` (D126).
- So all parts share one storage, one lock or connection pool, and one schema setup. A consumer store can use the same form, or two classes on one storage.

Tenants (D114). Each store keeps the tenant of each message and each subscription in its own field: a column, a document field, or a JSON property. A SQL store writes an empty text for no tenant, so that a unique index treats "no tenant" as one value. The tenant store of a key reads and writes only the data of its tenant. The dispatch store claims the messages of all tenants, and each claimed message keeps its tenant. The key of a received message is its tenant and its id, because each caller gives the message id, and two tenants can give the same id. The id of an outgoing message is unique, because `DeterministicId` uses the tenant (section 4.8).

A store for each tenant (D125). A storage package can keep one storage for each tenant through its own setup method. The setup methods of one shared storage do not change. For SQLite:
- `UseSqliteTenantStores<TTenantSource>` makes the file of each tenant from `TenantConnectionString`, for example `Data Source=data/msghatch-{tenant}.db`. It replaces `{tenant}` with the tenant id, escaped as in the file stores: with `Uri.EscapeDataString`, and then each capital letter A to Z as `^` and the small letter (D172). The key `TenantKey.None` uses `SystemConnectionString`, for example `Data Source=data/msghatch.db`.
- It registers the store class as a keyed singleton with `KeyedService.AnyKey`, one instance for each key, on the file of that key. The tenant store of a key is `For(key)` of that instance, so the tenant stores need no composite.
- It calls the core method `UseStorageForEachTenant<TTenantSource>` (section 4.7) with the four parts of the instance of each key. This method registers the tenant stores, and the library composites `TenantInboxDispatchStore` and `TenantOutboxDispatchStore` as the dispatch stores.
- `UseSqliteTenantSubscriptionStore` of `MsgHatch.Subscriptions.Data.Sqlite` registers the keyed subscription store of each key on the same file, with the same options. The subscription store has no claim, so it needs no composite.

Each dispatch composite gets a function that gives the dispatch store of one key, and keeps one instance for each key:
- The down states of the channels are only in the store of `TenantKey.None`, because a channel is global (D106). So an outbox claim first claims from that store. If that claim returns a down state, the composite claims nothing more, and returns that state.
- Then a claim asks the dispatch stores of the tenants of `ITenantSource` in turn, from the store after the last one that gave messages, until it has `maxCount` messages. `NextDueAtUtc` is the earliest value of all the stores.
- So the claim order of D144 holds only inside the storage of one key. A claim returns the messages of the store of `TenantKey.None` first, and then the messages of each tenant in turn. A due message of one tenant can come after a message of another storage that is due later.
- The composite keeps the next due time of each tenant and channel from each claim. A claim skips a tenant whose next due time is later than now. A tenant store of the same process clears the next due times of its tenant after a successful `AddAsync` or `RetryAsync`, through the internal wrapper that `UseStorageForEachTenant` puts around it. A successful `RecordTryAsync` or `FailAsync` with a next try time clears them too, so the next claim asks the tenant for its new next due time. A claim that runs at the same time as a clear does not keep its next due time. Each `MaxPollInterval`, one claim asks all tenants, so a message that another process adds waits at most that interval, as with polling.
- `RecordTryAsync`, `CompleteAsync`, and `FailAsync` go to the store of the tenant of the message.
- A claim skips the store of a tenant that returns an error (D173). The composite logs the error with the tenant (event 1502), and returns the messages of the other stores. An error of the tenant source or of the store of `TenantKey.None`, and `Errors.OperationCancelled`, fail the claim.
- `CountAsync` sums the counts of all stores. `DeleteTestDataAsync` and `PurgeAsync` call each store, and `PurgeAsync` stops at `maxCount` messages in total. Each of them returns the first error.
- The composite has the comment `ponytail: a sweep asks each tenant store, a shared claim index or a dispatcher for each group of tenants if there are many tenants`.

Test data (D82). Each inbox and outbox record keeps its `MessageFlags`:
- SQLite: the column `Flags` (text, for example `Test`) in `MsgHatchInbox` and `MsgHatchOutbox`, with an index. `DeleteTestDataAsync` deletes the rows with the test flag, with their publish tries in the same rows.
- JSON files: a test message is stored under `{RootPath}/test/inbox` or `{RootPath}/test/outbox`, with the same folder layout. `DeleteTestDataAsync` deletes the `test` folder of its store.
- A consumer store for another storage keeps the flags in a queryable field, so that analytics can leave test data out.

The subscription stores of `MsgHatch.Subscriptions.Data.Sqlite` and `MsgHatch.Subscriptions.Data.FileSystem` (D78, D97) keep each subscription with its fields as columns, and its channel settings as JSON:
- SQLite: the table `MsgHatchSubscription` with `Id TEXT PRIMARY KEY`, `Revision`, `TenantId`, `Level`, `CompanyId`, `UserId`, `MessageType`, `Kind`, `Channel`, `SettingsType`, `SettingsVersion`, `Settings TEXT` (the channel settings as JSON), `Recipients` (the recipient kind as text), `NamedUsers TEXT` (a JSON array of user ids, or null), `Mandatory INTEGER`, `CreatedAtUtc`, and `LastModifiedAtUtc`. The subscription store of a key reads and writes only the rows of its tenant. A unique index on `(TenantId, MessageType, Level, CompanyId, UserId, Channel, Recipients)` stops a duplicate. The store writes an empty text for a missing tenant, company, user, or channel, because SQLite treats each NULL as a different value in a unique index. The unique index also serves `FindForDeliveryAsync`, because its first columns are `(TenantId, MessageType, Level, CompanyId, UserId)`, so the table has no second index (checked fact of phase 10 in the roadmap). `UpdateAsync` uses `UPDATE ... WHERE Id = @id AND Revision = @expected`, and a changed row count of 0 gives `Errors.Conflict`. The store can share a database file with the core SQLite stores, so it keeps its schema version in its own table `MsgHatchSubscriptionSchema(Version INTEGER)`.
- JSON files: one file `{RootPath}/subscriptions/{tenantFolder}/{MessageType}/{level}/{ownerId}/{id}.json` for each subscription. `{tenantFolder}` is `no-tenant` for the key `TenantKey.None`, and `tenants/{tenantId}` for a tenant, so the two forms never collide. `default` is the owner folder of the default level. Each id in a folder name is escaped with `Uri.EscapeDataString`, because an id such as `auth0|abc` has characters that a file name cannot have. Then each capital letter A to Z becomes `^` and the small letter, because Windows and macOS ignore the case of a name, but the ids are case-sensitive. The escape turns `^` into `%5E`, so two ids never give one name. The level folder is `default`, `company`, or `user`. The file holds the subscription as one JSON document with camelCase names: `schemaVersion` 1, `tenant`, the fields of `Subscription`, and the channel settings as the JSON object `settings` with `settingsType` and `settingsVersion` (D149). `FindForDeliveryAsync` reads only the folders of the requested owners. A file lock on the owner folder serializes the changes of one owner. A change while another caller holds that lock gives `Errors.Unavailable`, as a state file of a channel that another caller holds does (D26). The store has the comment `ponytail: one file for each subscription and a folder lock suit small subscription sets, use a database store for many owners`.
- Each channel package registers its settings type with `MsgHatchBuilder.AddChannelType`, with the content type `Settings:{Channel}` and a version, so that the stores can read old settings versions (sections 4.7 and 4.13).
- The EF Core and MongoDB subscription stores follow the same rules (D120, D121). A consumer store for another storage also follows them, and passes `SubscriptionStoreTestsBase`.

Route traces (D98). Each router package registers its trace type with `MsgHatchBuilder.AddTraceType`, with the content type `Trace:{Name}` and a version, in the same way as the channel settings. The subscription router registers `ResolutionTrace` as `Trace:Subscription`. A router with no trace type registers nothing.

SQLite schema (D55, D56, D57). The table and column names are the C# property names:
- `MsgHatchInbox`: the counter `Sequence INTEGER PRIMARY KEY` (D144), `TenantId` (an empty text for no tenant) and `Id`, with `UNIQUE (TenantId, Id)`, the columns `Status`, `TryCount`, `MaxTryCount`, `CreatedAtUtc`, `LastTryAtUtc`, `NextTryAtUtc`, `LeaseUntilUtc`, `ExpiresAtUtc`, and `LastError` of `DeliveryState`, then `SchemaVersion`, `ContentType`, `ContentVersion`, `TraceParent` (the trace context, or null), and `Content TEXT` (the event as JSON).
- `MsgHatchOutbox`: `Sequence INTEGER PRIMARY KEY`, `Id TEXT UNIQUE`, and the same other columns, plus `SourceMessageId`, `MessageType`, `Channel`, `TargetKind`, `TargetId`, `TraceType` (the discriminator of the route trace, for example `Trace:Subscription@1`, or null), `Trace TEXT` (the route trace as JSON, or null), and `Tries TEXT` (the publish tries as a JSON array, D27). `Content` holds the channel envelope as JSON.
- `MsgHatchChannelState`: `Channel`, `Version`, `Kind`, `SinceUtc`, `PreviousKind`, `PreviousDurationMs`, `Reason`, `NextCheckAtUtc`, `ErrorCode`, `ErrorName`, and `Note`, with `PRIMARY KEY (Channel, Version)`, one row for each state of a channel (D106, D277). From phase 32, it replaces the table `MsgHatchChannelDown`, which the store no longer creates or reads.
- The partial index `(NextTryAtUtc) WHERE Status = 'Pending'` in `MsgHatchInbox`, and `(Channel, NextTryAtUtc) WHERE Status = 'Pending'` in `MsgHatchOutbox` (D104). So a claim index holds only the backlog and the claimed messages.
- From phase 16: `MsgHatchChannelEntry` and `MsgHatchMessageTypeEntry`, the registry settings (section 4.20).
- For the delivery management (section 4.19): an index on `(TenantId, CreatedAtUtc, Id)` in each message table, and on `(TenantId, TargetKind, TargetId, CreatedAtUtc)` and `(SourceMessageId)` in `MsgHatchOutbox`.
- `MsgHatchSchema(Version INTEGER)` holds the schema version 1.
- Enum values are text. Times are ISO 8601 text in UTC, for example `2026-10-31T08:00:00.0000000Z` (D57).

SQLite store rules:
- Each operation opens its own connection. `Microsoft.Data.Sqlite` pools the connections.
- The store is a singleton (D130). Its first operation sets `PRAGMA journal_mode=WAL`, and creates the tables with `CREATE TABLE IF NOT EXISTS`, one time for each connection string in the process.
- A busy database waits through the command timeout: `Microsoft.Data.Sqlite` tries a locked database again until the command timeout ends. Phase 8 checked this for version 10.0.14 (checked facts of phase 8 in the roadmap). The command timeout is the `Default Timeout` of the connection string, 30 seconds by default. A `PRAGMA busy_timeout` is not used, because it applies to one connection only.
- `AddAsync` uses `INSERT OR IGNORE` in one transaction for the batch.
- `ClaimDueAsync` first reads, with no write lock, `MIN(NextTryAtUtc)` of the pending rows, of its channel for the outbox. The outbox claim also reads the latest row of its channel in `MsgHatchChannelState`. With a down row, it claims nothing, and returns its down state as `ClaimResult.Down` (D106). With no due row, it returns the minimum as `NextDueAtUtc`, and takes no write lock.
- Else, in a `BEGIN IMMEDIATE` transaction, a first statement reads the first due rows in the claim order (D144): `SELECT Sequence ... WHERE Status = 'Pending' AND NextTryAtUtc <= @now ORDER BY NextTryAtUtc, Sequence LIMIT @max`. The outbox claim also has `AND Channel = @channel` (D104). A second statement leases these rows: `UPDATE ... SET NextTryAtUtc = @leaseUntil, LeaseUntilUtc = @leaseUntil WHERE Sequence IN (SELECT value FROM json_each(@sequences)) RETURNING Sequence, ...`. The transaction holds the write lock from its start, so no other claim changes the rows between the two statements. The claim uses `Sequence`, because two tenants can store one inbox id. So the end of the lease is the next due time of a claimed row (D103).
- `RETURNING` gives each row as one `json_object` document, which `MsgHatchJson` reads (D143). SQLite gives the rows of `RETURNING` in an arbitrary order, and `RETURNING` has no `ORDER BY`. So the store returns the documents in the order of the first statement. The document puts a column with invalid JSON in as a JSON string, so one edited row cannot fail the whole claim.
- A claimed row that the code cannot read stays out of the result. A transient error, for example a newer version, keeps the row pending with the end of the lease as its next try time, so a newer instance reads it after the lease (section 4.13). A permanent error, for example invalid JSON, sets its status to `Failed`, ends its lease, and writes `{ErrorName}: {message}` into `LastError`.
- When the first statement reads fewer than `maxCount` rows, a third statement in the same transaction reads `MIN(NextTryAtUtc)` of the pending rows as `NextDueAtUtc`. When it reads `maxCount` rows, `NextDueAtUtc` is now, because more rows can be due.
- `SetChannelDownAsync` and `SetChannelUpAsync` read the latest row of the channel, and add the next row of `ChannelState.AfterDown` or `AfterUp` with `INSERT OR IGNORE`. A changed row count of 0 means that another caller added the version first, so the store reads the latest row again (D277). `TryStartChannelCheckAsync` uses `UPDATE ... SET NextCheckAtUtc = @next WHERE Channel = @channel AND Kind = 'Down' AND Reason = 'Errors' AND NextCheckAtUtc <= @now` on the latest version, and a changed row count of 1 means that this caller runs the check.
- `RecordTryAsync` is one statement: `UPDATE MsgHatchOutbox SET Tries = json_insert(Tries, '$[#]', json_object('tryNumber', @tryNumber, ...)), TryCount = @tryNumber, ... WHERE Id = @id AND TryCount = @tryNumber - 1`. A changed row count of 0 means that the try is already recorded (D27), or that the message is not stored. Only then a second statement reads whether the id is stored, and an id that is not stored gives `Errors.NotFound`.
- The tables have no `NOT NULL` constraint, because `INSERT OR IGNORE` also skips a row that breaks a `NOT NULL` constraint, with no error.
- A SQLite failure, for example a busy database after the command timeout, gives `Errors.Unavailable`. A down row with an invalid value gives `Errors.SerializationFailed`.
- The database file must be on a local disk. SQLite does not support network file systems. The stores do not create the folder of the database file.

File store rules (D26):
- A message file is `{nextTryAtUtc:yyyyMMddHHmmssfff}_{sequence:D10}_{key}.json` in the status folder `pending`, `claimed`, `done`, `failed`, or `cancelled` (D110), under `{RootPath}/inbox` or `{RootPath}/outbox/{channel}` (D104). The key of a received message is its id and the name of its tenant, and the key of an outgoing message is its id. The name of a tenant is its id escaped with `Uri.EscapeDataString`, with each capital letter A to Z as `^` and the small letter, because Windows and macOS ignore the case of a name, but a tenant id is case-sensitive.
- The sequence counts the files that one store instance adds with the same time prefix, so a sort by name keeps the insert order of the messages with the same due time.
- A claim renames each due file into `claimed`, with the lease end as its time prefix (D103). The claims of one store instance take turns. On Windows, a rename goes through a handle of the source, so two processes on one folder can rarely claim one file in the same lease.
- A claimed file that the code cannot read stays out of the claim: a newer version stays in `claimed` for a newer instance (section 4.13), and a permanent error moves the file to `failed`.
- A query and a count of the delivery management (section 4.19) skip a file that the code cannot read (D167).

EF Core store rules (D120, D175 to D178, D318 to D323, D325 to D327). `docs/stores.md` section 4 has the details:
- `MsgHatch.Data.EFCore` has no SQL text and no provider name, and runs on each relational EF Core provider. `MsgHatchDbContext` maps the tables of the SQLite schema with no prefix `MsgHatch`, in the schema of `EntityFrameworkStoreOptions.Schema`: `Inbox`, `Outbox`, `ChannelState`, `ChannelEntry`, and `MessageTypeEntry` (D321). `MsgHatchSubscriptionDbContext` of `MsgHatch.Subscriptions.Data.EFCore` maps `Subscription`. The indexes drop the prefix too, for example `IX_Outbox_Due`. On a database with no schemas, for example MySQL, the dialect gives `HasSchemas` false, and each table and each history table keeps the prefix `MsgHatch` in the database of the connection, for example `MsgHatchOutbox` and `__MsgHatchStoreMigrations` (D326).
- The due indexes have no filter: `(Status, NextTryAtUtc, Sequence)` of the inbox, and `(Channel, Status, NextTryAtUtc, Sequence)` of the outbox (D104, D144). Each message table has the columns `ClaimToken` and `ClaimedDueAtUtc` of the last claim.
- An id is `nvarchar(256)`, a message id is `char(36)` in the format `D`, so a page sorts as `Guid.CompareTo` (D112), and a name is `varchar(256)` (D178). A JSON column has the type of the option `JsonColumnType`, or else the JSON type of the dialect, `json` on PostgreSQL, or else the text type of the provider (D323). Each text column has the collation of `TextCollation`, or else of the dialect, or else of the database, and a JSON column has no collation, because PostgreSQL refuses a collation on `json`. An enum value is text. A time is a UTC `DateTime` in the time type of the provider, and the store gives it the kind UTC on a write and on a read (D319). On PostgreSQL, a time is a `timestamptz`, and Npgsql cuts it down to the microsecond. On MySQL, a time is a `datetime(6)`, and the provider cuts it down to the microsecond too. On MySQL, a message id and a claim token are `varchar(36)`, because MySqlConnector reads a `char(36)` column as a `Guid`. A host on MySQL with no MySQL package sets `GuidFormat=None` in its connection string.
- Each store is a singleton, and makes one context for each operation through a pooled `IDbContextFactory` (D130).
- The unique key of a subscription is the column `OwnerKey`, a binary column of 32 bytes: the SHA-256 of the 7 key values, each as its UTF-8 byte count and its UTF-8 bytes. The store calculates it in C# before each insert and each update (D327).
- An add is an EF Core insert on the unique key. A stored key gives no error: the store reads it and treats the add as done. An outbox batch with a stored id adds its other messages in a new transaction, in the order of the batch. The store splits a message into its columns in C#, and writes the document from the columns in C# (D176).
- A claim with no claim statement is a LINQ read of the due candidates in the claim order, and one `ExecuteUpdate` that sets the claim token, the due time before the claim, the due time, and the lease end of the candidates that are still pending and due. When another dispatcher took a candidate first, the store reads the next candidates. The store reads the claimed rows by their `Sequence` and the token, and sorts them by `ClaimedDueAtUtc` and `Sequence` (D144).
- `RecordTryAsync` and the cancel of an outgoing message write the new JSON array `Tries` with an `ExecuteUpdate` on the condition of the `TryCount` that the store read, and read the row again when another writer changed it first (D27). A channel state row is an insert on the key of the channel and the version (D277).
- A provider package implements `IEntityFrameworkStoreDialect` of `MsgHatch.Data.EFCore.Providers`: the provider name, the default collation, the JSON type, the error type of a database exception, and an optional claim statement, which the base package runs with `FromSqlRaw` and the quoted names of `ClaimStatementNames`. The dialect of `MsgHatch.Data.EFCore.SqlServer` has the claim of D120 with `READPAST` and the collation `Latin1_General_100_BIN2`. The dialect of `MsgHatch.Data.EFCore.PostgreSql` has the claim with `FOR UPDATE SKIP LOCKED` and the collation `C`. The dialect of `MsgHatch.Data.EFCore.MySql` has no claim statement, so the claim of the base package runs, the collation `utf8mb4_bin`, and `HasSchemas` false (D325, D326).
- An `ExecuteUpdate` of the base package sets a column only after each assignment that reads that column, because MySQL applies the assignments from left to right. It has no subquery on its own table, because MySQL refuses it with the error 1093. So the channel check reads the latest version first, as the MongoDB store does.
- Only `MigrateMsgHatchAsync` changes the schema (D320). Each context has a history table of its own in the schema of the options: `__StoreMigrations` and `__SubscriptionMigrations`. The migrations of a provider package derive from `EntityFrameworkStoreMigration` and `EntityFrameworkStoreModelSnapshot` of `MsgHatch.Data.EFCore.Migrations`, and the base package gives them the schema, the collation, and the JSON type of the options through its own `IMigrationsAssembly`. A migration of a host runs unchanged.
- A provider that is not relational fails the startup validation, and each store call on it gives `Errors.NotFound` (D175).
- A store classifies an exception in the order of D141: a cancelled token, the `IExceptionClassifier<TContext>` registrations of the host, `ErrorTypeOf` of the dialect, and then the built-in mapping (D322). The SQL Server, PostgreSQL, and MySQL dialects give `Errors.Rejected` for a missing permission or a failed login, and `Errors.ValidationFailed` for a missing table or column and for a value that is too long.
- The base package has the feature folders `DependencyInjection`, `Messages`, `Registry`, `Model`, `Providers`, and `Migrations`, and its root keeps the types that a host uses (D50).

MongoDB store rules (D121, D184 to D187). `docs/stores.md` section 5 has the details:
- The collections have the names of the SQLite tables: `MsgHatchInbox`, `MsgHatchOutbox`, `MsgHatchChannelState`, `MsgHatchChannelEntry`, and `MsgHatchMessageTypeEntry`, and `MsgHatchSubscription` of `MsgHatch.Subscriptions.Data.MongoDb`, in the database of `MongoDbStoreOptions.DatabaseName`.
- A message document is the JSON document of `MsgHatchJson` converted into BSON (D184), with an ObjectId `_id` from `CreatedAtUtc` and the counter of the driver (D187), each time as `{ d, t }` (D185), and for the outbox the array `tries`.
- The claim index is a partial index on `(channel, delivery.nextTryAtUtc.t, _id)` of the outbox, and `(delivery.nextTryAtUtc.t, _id)` of the inbox, with the filter `delivery.status: "Pending"` (D104). A store creates the indexes of its collection one time for each database in the process.
- A claim is a loop of `findOneAndUpdate` calls in the claim order. `RecordTryAsync` is one `updateOne` with `$push` and the filter `delivery.tryCount: tryNumber - 1` (D27). The cancel of an outgoing message is one `updateMany` with an aggregation pipeline, so each document gets its next try number.
- An add is one unordered `insertMany` with no transaction, which skips a stored key (D186). The stores use no transaction, so they work on a server with no replica set.
- The stores register no BSON class map. A server failure gives `Errors.Unavailable`, and a stored value that a person changed into an invalid value gives `Errors.SerializationFailed`.

The library gives each store the type information that it needs (`MsgHatch.Domain.Persistence`):

```csharp
public interface IContentTypeRegistry
{
    IReadOnlyList<ContentTypeEntry> Entries { get; }
    bool TryFind(string contentType, int version, out ContentTypeEntry entry);
}

public sealed record ContentTypeEntry(
    string ContentType, int Version, string Discriminator, Type ClrType, JsonTypeInfo ContentJsonTypeInfo)
{
    public static ContentTypeEntry ForReceivedMessage<TEvent>(string contentType, int version, JsonTypeInfo<TEvent> json);
    public static ContentTypeEntry ForOutgoingMessage<TEnvelope>(string contentType, int version, JsonTypeInfo<TEnvelope> json)
        where TEnvelope : class;
}
```

- The registry is read-only for the stores. Its entries come from the registrations of the message types and the channels, and from `MsgHatchBuilder.AddChannelType` and `AddTraceType` (section 4.7).
- Each registration adds one entry for each version: the current version, and each version that has an upgrade. `ClrType` is the closed type, for example `ReceivedMessage<OrderShippedV1>` for version 1. The registration makes the entry of a message with `ForReceivedMessage` or `ForOutgoingMessage`, which also give the entry an internal shape: the typed code that reads the content of a typed message and makes a typed message. So `MsgHatchJson` closes no generic type at run time (D237, D245). An entry of the plain constructor has no shape, and `MsgHatchJson` gives `Errors.SerializationFailed` for a message of it.
- `Discriminator` is `{ContentType}@{Version}` (D30). `MsgHatchJson` writes it as the `$type` of a route trace. A MongoDB store keeps the document of `MsgHatchJson` with the same discriminators, and registers no class map (D184).
- An event content type is the registered message type name, for example `OrderShipped`. An envelope content type is `{Channel}:{Name}`, for example `Email:OrderShippedEmail`. The channel registration methods add the channel prefix. A trace content type is `Trace:{Name}`, for example `Trace:Subscription` (D98).
- `MsgHatchJson` writes a whole `ReceivedMessage` or `OutgoingMessage` as one JSON document, and reads it back as the correct closed type. It uses the registered `JsonTypeInfo`, camelCase names, enum values as strings (D23), and it writes null values.
- A SQL store keeps the event or the envelope, and the route trace, of that document in its JSON columns. To read a message, it puts its columns back into one document (D143).
- `MsgHatchJson` writes a route trace with the discriminator property `$type`, for example `"$type": "Trace:Subscription@1"`, and reads it back as the registered trace type. An unknown discriminator gives `Errors.SerializationFailed`, as for an unknown envelope type.
- A store reads an old version as the old closed type. The library upgrades it after the claim (section 4.13).

An outgoing message with one failed try, as a JSON file:

```json
{
  "schemaVersion": 1,
  "delivery": {
    "id": "7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31",
    "status": "Pending",
    "tryCount": 1,
    "maxTryCount": 8,
    "createdAtUtc": "2026-10-31T08:00:00Z",
    "lastTryAtUtc": "2026-10-31T08:00:01Z",
    "nextTryAtUtc": "2026-10-31T08:00:06Z",
    "leaseUntilUtc": null,
    "expiresAtUtc": null,
    "lastError": "RateLimited: Maximum sending rate exceeded."
  },
  "sourceMessageId": "0b6c1f7e-2a44-4d8e-9a3b-61c0f5e2d7a9",
  "tenant": null,
  "type": "OrderShipped",
  "channel": "Email",
  "target": { "kind": "User", "id": "1" },
  "contentType": "Email:OrderShippedEmail",
  "contentVersion": 1,
  "flags": "None",
  "traceParent": "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01",
  "trace": {
    "$type": "Trace:Subscription@1",
    "applied": { "id": "0193a4c2-7b1e-7c3d-9f20-5e8a1b2c3d4e", "owner": { "level": "User", "company": null, "user": "1" }, "recipients": "ScopeUsers", "mandatory": false },
    "target": "ScopeUser",
    "language": "ChannelSettings",
    "address": "UserProfile",
    "skipped": [
      { "subscription": { "id": "0193a4c2-7b1e-7c3d-9f20-5e8a1b2c3d01", "owner": { "level": "Default", "company": null, "user": null }, "recipients": "ScopeUsers", "mandatory": false }, "reason": "Overridden" }
    ]
  },
  "envelope": { "to": "client1@example.com", "subject": "Order 1001 is shipped", "htmlBody": "...", "textBody": null, "from": null, "cc": null, "bcc": null },
  "tries": [
    {
      "tryNumber": 1,
      "startedAtUtc": "2026-10-31T08:00:00Z",
      "finishedAtUtc": "2026-10-31T08:00:01Z",
      "outcome": "TransientError",
      "providerMessageId": null,
      "errorCode": 429,
      "errorName": "RateLimited",
      "errorMessage": "Maximum sending rate exceeded."
    }
  ]
}
```

The `tries` array is not part of `OutgoingMessage`. The file store adds it, and `MsgHatchJson` ignores it when it reads a message. A SQL store keeps the same array in the column `Tries`. `MsgHatchJson` does not write `OutgoingMessage.Content`, because `envelope` already holds it.

### 4.6 Contracts

Core contracts (`MsgHatch.Domain`):

```csharp
// MsgHatch.Domain.Routing
public interface IMessageRouter<TEvent>
{
    ValueTask<ErrorOr<IReadOnlyList<MessageRoute>>> RouteAsync(ReceivedMessage<TEvent> message, CancellationToken ct);
}

public interface IMessageRegistry
{
    IReadOnlyList<ChannelDescriptor> Channels { get; }
    IReadOnlyList<MessageTypeDescriptor> MessageTypes { get; }
    ChannelDescriptor? FindChannel(ChannelId channel);
    MessageTypeDescriptor? FindMessageType(MessageType type);
}

public interface IRegistryWriter
{
    ValueTask<ErrorOr<RegistryEntries>> GetEntriesAsync(CancellationToken ct);
    ValueTask<ErrorOr<ChannelEntry>> SaveChannelAsync(ChannelEntry entry, CancellationToken ct);
    ValueTask<ErrorOr<MessageTypeEntry>> SaveMessageTypeAsync(MessageTypeEntry entry, CancellationToken ct);
    ValueTask<Error?> ResetChannelAsync(ChannelId channel, CancellationToken ct);
    ValueTask<Error?> ResetMessageTypeAsync(MessageType type, CancellationToken ct);
}

public interface IRegistrySettingsStore
{
    ValueTask<ErrorOr<RegistryEntries>> GetEntriesAsync(CancellationToken ct);
    ValueTask<Error?> SaveChannelEntryAsync(ChannelEntry entry, CancellationToken ct);
    ValueTask<Error?> SaveMessageTypeEntryAsync(MessageTypeEntry entry, CancellationToken ct);
    ValueTask<Error?> DeleteChannelEntryAsync(ChannelId channel, CancellationToken ct);
    ValueTask<Error?> DeleteMessageTypeEntryAsync(MessageType type, CancellationToken ct);
}

public interface IChannelSettingsValidator
{
    ChannelId Channel { get; }
    Error? Validate(ChannelSettings settings, ChannelFeatures features);
}

// MsgHatch.Domain.Pipeline
public interface IEnvelopeComposer<TEvent, TEnvelope>
{
    ValueTask<ErrorOr<IReadOnlyList<ErrorOr<TEnvelope>>>> ComposeAsync(
        ReceivedMessage<TEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct);
}

public interface IPayloadLoader<TEvent, TPayload>
{
    ValueTask<ErrorOr<IReadOnlyList<TPayload>>> LoadAsync(
        ReceivedMessage<TEvent> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct);
}

public interface INotificationService
{
    ValueTask<ErrorOr<NotificationResult>> ProcessAsync<TEvent>(
        TEvent domainEvent, MessageContext context, CancellationToken ct);
    ValueTask<ErrorOr<Guid>> AcceptAsync<TEvent>(TEvent domainEvent, MessageContext context, CancellationToken ct);
    ValueTask<ErrorOr<IReadOnlyList<RouteResult>>> PreviewAsync<TEvent>(
        TEvent domainEvent, MessageContext context, IReadOnlyList<MessageRoute>? routes, CancellationToken ct);
}

// MsgHatch.Domain.Persistence
public interface ITenantSource
{
    ValueTask<ErrorOr<IReadOnlyList<TenantId>>> GetTenantsAsync(CancellationToken ct);
}

// MsgHatch.Domain.Delivery
public interface ITransport
{
    ChannelId Channel { get; }
    ValueTask<ErrorOr<PublishReceipt>> SendAsync(OutgoingMessage message, string? redirectTo, CancellationToken ct);
    ValueTask<Error?> CheckHealthAsync(CancellationToken ct);
}

// MsgHatch.Domain.Inbox
public interface IInboxStore
{
    ValueTask<Error?> AddAsync(ReceivedMessage message, CancellationToken ct);

    // Delivery management (section 4.19)
    ValueTask<ErrorOr<MessagePage<ReceivedMessageSummary>>> QueryAsync(InboxQuery query, CancellationToken ct);
    ValueTask<ErrorOr<ReceivedMessage?>> GetAsync(Guid id, CancellationToken ct);
    ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct);
    ValueTask<ErrorOr<int>> RetryAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, int addedTries, CancellationToken ct);
    ValueTask<ErrorOr<int>> CancelAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, CancellationToken ct);
}

public interface IInboxDispatchStore
{
    ValueTask<ErrorOr<ClaimResult<ReceivedMessage>>> ClaimDueAsync(
        int maxCount, DateTime nowUtc, TimeSpan lease, CancellationToken ct);
    ValueTask<Error?> CompleteAsync(ReceivedMessage message, DateTime triedAtUtc, CancellationToken ct);
    ValueTask<Error?> FailAsync(
        ReceivedMessage message, string error, DateTime triedAtUtc, DateTime? nextTryAtUtc, CancellationToken ct);
    ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct);
    ValueTask<ErrorOr<int>> PurgeAsync(DateTime finishedBeforeUtc, int maxCount, CancellationToken ct);
}

public interface IInboxProcessor
{
    ValueTask RunAsync(CancellationToken ct);
    ValueTask<ErrorOr<DispatchSummary>> ProcessPendingAsync(int batchSize, CancellationToken ct);
}

// MsgHatch.Domain.Outbox
public interface IOutboxStore
{
    ValueTask<Error?> AddAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken ct);

    // Delivery management (section 4.19)
    ValueTask<ErrorOr<MessagePage<OutgoingMessageSummary>>> QueryAsync(OutboxQuery query, CancellationToken ct);
    ValueTask<ErrorOr<OutgoingMessageHistory?>> GetAsync(Guid id, CancellationToken ct);
    ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct);
    ValueTask<ErrorOr<int>> RetryAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, int addedTries, CancellationToken ct);
    ValueTask<ErrorOr<int>> CancelAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, CancellationToken ct);
}

public interface IOutboxDispatchStore
{
    ValueTask<ErrorOr<ClaimResult<OutgoingMessage>>> ClaimDueAsync(
        ChannelId channel, int maxCount, DateTime nowUtc, TimeSpan lease, CancellationToken ct);
    ValueTask<Error?> RecordTryAsync(
        OutgoingMessage message, PublishTry publishTry, DateTime? nextTryAtUtc, CancellationToken ct);
    ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct);
    ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct);
    ValueTask<ErrorOr<int>> PurgeAsync(DateTime finishedBeforeUtc, int maxCount, CancellationToken ct);

    // State of a channel (D106, D277)
    ValueTask<ErrorOr<IReadOnlyList<ChannelDown>>> GetDownChannelsAsync(CancellationToken ct);
    ValueTask<ErrorOr<bool>> SetChannelDownAsync(ChannelDown down, CancellationToken ct);
    ValueTask<ErrorOr<bool>> SetChannelUpAsync(ChannelId channel, ChannelDownReason? reason, DateTime upAtUtc, CancellationToken ct);
    ValueTask<ErrorOr<IReadOnlyList<ChannelState>>> GetChannelStatesAsync(DateTime fromUtc, CancellationToken ct);
    ValueTask<ErrorOr<bool>> TryStartChannelCheckAsync(
        ChannelId channel, DateTime nowUtc, DateTime nextCheckAtUtc, CancellationToken ct);
}

public interface IOutboxDispatcher
{
    ValueTask RunAsync(CancellationToken ct);
    ValueTask<ErrorOr<DispatchSummary>> DispatchPendingAsync(int batchSize, CancellationToken ct);
}

// MsgHatch.Domain.Dispatch
public interface IDispatchSignal
{
    ValueTask<Error?> NotifyAsync(DispatchTopic topic, CancellationToken ct);
    ValueTask<Error?> WaitAsync(DispatchTopic topic, CancellationToken ct);
}

// MsgHatch.Domain.Management
public readonly record struct DeletedMessages(int InboxMessages, int OutboxMessages);

public sealed record MessageContent(Guid Id, string Json); // the JSON text of MsgHatchJson (D290)

public interface IDeliveryQueries // D289
{
    ValueTask<ErrorOr<IReadOnlyList<ChannelStatus>>> GetChannelsAsync(CancellationToken ct);
    ValueTask<ErrorOr<TrafficReport>> GetTrafficAsync(TrafficQuery query, CancellationToken ct);
    ValueTask<ErrorOr<MessagePage<ReceivedMessageSummary>>> GetReceivedMessagesAsync(InboxQuery query, CancellationToken ct);
    ValueTask<ErrorOr<MessagePage<OutgoingMessageSummary>>> GetOutgoingMessagesAsync(OutboxQuery query, CancellationToken ct);
    ValueTask<ErrorOr<OutgoingMessageDetail>> GetOutgoingMessageAsync(Guid id, CancellationToken ct);
    ValueTask<ErrorOr<MessageContent>> GetReceivedContentAsync(Guid id, CancellationToken ct);
    ValueTask<ErrorOr<MessageContent>> GetOutgoingContentAsync(Guid id, CancellationToken ct);
}

public interface IDeliveryCommands // D289
{
    ValueTask<Error?> PauseChannelAsync(ChannelId channel, string? note, CancellationToken ct);
    ValueTask<Error?> ResumeChannelAsync(ChannelId channel, CancellationToken ct);
    ValueTask<ErrorOr<DeliveryActionResult>> RetryReceivedAsync(InboxQuery query, CancellationToken ct);
    ValueTask<ErrorOr<DeliveryActionResult>> CancelReceivedAsync(InboxQuery query, CancellationToken ct);
    ValueTask<ErrorOr<DeliveryActionResult>> RetryOutgoingAsync(OutboxQuery query, CancellationToken ct);
    ValueTask<ErrorOr<DeliveryActionResult>> CancelOutgoingAsync(OutboxQuery query, CancellationToken ct);
    ValueTask<ErrorOr<DeletedMessages>> DeleteTestDataAsync(CancellationToken ct);
    ValueTask<ErrorOr<DeletedMessages>> PurgeAsync(DateTime finishedBeforeUtc, int maxCount, CancellationToken ct);
}

// MsgHatch.Channels.Email
public interface IEmailClient
{
    ValueTask<ErrorOr<PublishReceipt>> SendAsync(Guid messageId, EmailMessage message, CancellationToken ct);
    ValueTask<Error?> CheckHealthAsync(CancellationToken ct);
}

// MsgHatch.Templates
public readonly record struct TemplateRequest(
    MessageType Type, string Language, string? Scope, string Part, TemplatePartKind Kind, bool Optional = false);

public interface ITemplateEngine<TPayload>
{
    ValueTask<ErrorOr<ITemplate<TPayload>>> PrepareAsync(TemplateRequest request, CancellationToken ct);
    ValueTask<ErrorOr<IReadOnlyList<TemplateIssue>>> CheckAsync(TemplateLookup lookup, string text, CancellationToken ct); // D219, D241
}

public sealed record TemplateIssue(string Message, int? Line, int? Column); // the line and the column start at 1, or are null

public interface ITemplate<TPayload>
{
    TemplateLookup Lookup { get; } // the lookup that found the text, after the fallback (D214)
    ValueTask<ErrorOr<string>> RenderAsync(TPayload payload, CultureInfo culture, CancellationToken ct);
}

public readonly record struct TemplateMatch(TemplateSource Source, TemplateLookup Lookup); // a hit of TemplateSelector

public interface ITemplateEngineFactory
{
    string Name { get; } // for example liquid (D240)
    ITemplateEngine<TPayload> Create<TPayload>(IServiceProvider tenantServices, TenantKey key);
}

// MsgHatch.Channels.Broker
public readonly record struct BrokerPublish(
    Guid MessageId, TenantId? Tenant, string ContentType, object Payload, JsonTypeInfo PayloadJson,
    IReadOnlyDictionary<string, string> Headers);

public interface IBrokerClient
{
    ValueTask<ErrorOr<PublishReceipt>> PublishAsync(BrokerPublish publish, CancellationToken ct);
    ValueTask<Error?> CheckHealthAsync(CancellationToken ct);
}
```

Contracts of the subscription router (`MsgHatch.Subscriptions`, D97):

```csharp
// MsgHatch.Subscriptions
public interface ISubscriptionStore
{
    ValueTask<ErrorOr<IReadOnlyList<Subscription>>> FindForDeliveryAsync(
        MessageType type, IReadOnlyList<CompanyId> companies, IReadOnlyList<UserId> users, CancellationToken ct);
    ValueTask<ErrorOr<IReadOnlyList<Subscription>>> QueryAsync(SubscriptionQuery query, CancellationToken ct);
    ValueTask<ErrorOr<Subscription?>> GetAsync(Guid id, CancellationToken ct);
    ValueTask<Error?> AddAsync(Subscription subscription, CancellationToken ct);
    ValueTask<Error?> UpdateAsync(Subscription subscription, int expectedRevision, CancellationToken ct);
    ValueTask<Error?> DeleteAsync(Guid id, CancellationToken ct);
}

public interface IRecipientDirectory
{
    ValueTask<ErrorOr<IReadOnlyList<UserProfile>>> GetUsersAsync(IReadOnlyList<UserId> users, CancellationToken ct);
    ValueTask<ErrorOr<IReadOnlyList<UserProfile>>> GetCompanyUsersAsync(CompanyId company, CancellationToken ct);
    ValueTask<ErrorOr<CompanyProfile?>> GetCompanyAsync(CompanyId company, CancellationToken ct);
}

public interface ISubscriptionCache
{
    ValueTask<Error?> InvalidateAsync(SubscriptionOwner owner, CancellationToken ct);
    ValueTask<Error?> InvalidateAllAsync(CancellationToken ct);
}

public interface ISubscriptionCacheBackend
{
    ValueTask<ErrorOr<IReadOnlyList<Subscription>?>> TryGetAsync(string key, CancellationToken ct);
    ValueTask<Error?> SetAsync(
        string key, IReadOnlyList<Subscription> subscriptions, IReadOnlyList<string> tags, TimeSpan expiration,
        CancellationToken ct);
    ValueTask<Error?> RemoveByTagAsync(string tag, CancellationToken ct);
}

public interface ISubscriptionQueries
{
    ValueTask<ErrorOr<IReadOnlyList<ChannelDescriptor>>> GetChannelsAsync(CancellationToken ct);
    ValueTask<ErrorOr<IReadOnlyList<MessageTypeState>>> GetMessageTypesAsync(MessageTypeQuery query, CancellationToken ct);
    ValueTask<ErrorOr<IReadOnlyList<Subscription>>> GetSubscriptionsAsync(SubscriptionQuery query, CancellationToken ct);
    ValueTask<ErrorOr<Subscription>> GetSubscriptionAsync(Guid id, CancellationToken ct);
}

public interface ISubscriptionCommands
{
    ValueTask<ErrorOr<Subscription>> CreateAsync(SubscriptionDraft draft, CancellationToken ct);
    ValueTask<ErrorOr<Subscription>> UpdateAsync(Guid id, int expectedRevision, SubscriptionDraft draft, CancellationToken ct);
    ValueTask<Error?> DeleteAsync(Guid id, CancellationToken ct);
}

// MsgHatch.Subscriptions.Verification
public interface IRoutingVerifier
{
    ValueTask<ErrorOr<RoutingVerificationReport>> VerifyAsync(RoutingVerificationRequest request, CancellationToken ct);
}
```

A router can be simple (D95). This router of a consumer sends each password reset to the one user of the event:

```csharp
public sealed record PasswordReset(string UserId, string Email, string Language);

public sealed class PasswordResetRouter : IMessageRouter<PasswordReset>
{
    public ValueTask<ErrorOr<IReadOnlyList<MessageRoute>>> RouteAsync(
        ReceivedMessage<PasswordReset> message, CancellationToken ct)
    {
        var e = message.Event;
        var route = new MessageRoute(
            new DeliveryTarget(DeliveryTargetKind.User, e.UserId), EmailChannel.Id, new EmailChannelSettings(),
            e.Language, e.Email, TemplateScope: null, Trace: null);
        return ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageRoute>>.ForValue([route]));
    }
}
```

Tenant services (D126). These services are tenant services: each router, payload loader, composer, and template engine, the template store of a tenant, `IInboxStore`, `IOutboxStore`, `IDeliveryQueries`, `IDeliveryCommands`, and, in `MsgHatch.Subscriptions`, `ISubscriptionStore`, `IRecipientDirectory`, `ISubscriptionCache`, `ISubscriptionQueries`, `ISubscriptionCommands`, and `IRoutingVerifier`. The other services exist one time for the whole system, for example the dispatch stores, the transports, `IMessageRegistry`, and `IDispatchSignal`.

Contract rules of the core:
- Each store, router, loader, composer, engine, and transport method returns an `Error` for a failure, and does not throw (section 4.12).
- A store never reads the clock. The library gives it each time that it needs (section 4.14).
- A tenant service of a key reads and changes only the data of its tenant, and each tenant service that it uses has the same key (D126, section 4.7). So a message id or a subscription id of another tenant finds nothing. A dispatch store works for all tenants (D127).
- `IMessageRouter.RouteAsync` returns the routes of one event. An empty list sends nothing, and is not an error. An error fails the event, and the inbox or the broker retries it. A router stores nothing and sends nothing, because a preview calls it too.
- `IMessageRegistry` gives each registered channel and each registered message type with its channels, from a frozen snapshot (D117). A lookup of an unknown channel or message type gives null. A read completes at once, does not allocate, and has no error result.
- `IRegistryWriter` reads, saves, and resets the registry settings (D118, section 4.20). The `Revision` of a saved entry is the expected revision, and 0 means a new entry. Only an administration host needs it. A setup in code registers no writer.
- `IRegistrySettingsStore` keeps the registry settings. `Save…EntryAsync` writes the entry only when the stored revision is `entry.Revision - 1`, and a missing entry has the revision 0. Else it gives `Errors.Conflict`. A delete of an unknown entry succeeds. `GetEntriesAsync` gives each list in the ordinal order of the names, so that each store gives the same result.
- `ITenantSource.GetTenantsAsync` gives each tenant that has its own storage (D125). Only the dispatch composites call it.
- `IEnvelopeComposer.ComposeAsync` returns one inner result for each route, in the same order. An inner error fails only that message. An outer error, for example from a payload loader, fails the event, and the inbox or the broker retries it.
- `IPayloadLoader.LoadAsync` returns one payload for each route, in the same order. It can return the same instance for many routes when the data does not depend on the target.
- `INotificationService.PreviewAsync` runs steps 3 to 5 of section 4.8 as a test message, for the given routes, or for the routes of the router of the message type when `routes` is null. It stores nothing and sends nothing. It returns one `RouteResult` for each route, in the same order (D84, D100).
- `AddAsync` of a tenant store ignores a message whose id is already stored. A partial batch is safe, because a retry writes the same ids. A message with another tenant than the key of the store gives `Errors.ValidationFailed`, and the store adds nothing of the batch.
- `ClaimDueAsync` with a `maxCount` less than 1 returns `Errors.ValidationFailed`. `DispatchPendingAsync`, `ProcessPendingAsync`, and `PurgeAsync` do the same for a count less than 1.
- `ClaimDueAsync` claims only pending messages with `NextTryAtUtc` at or before `nowUtc`. It sets `NextTryAtUtc` and `LeaseUntilUtc` to `nowUtc + lease`, so no other claim takes the message before the end of the lease (D103). It is atomic between concurrent dispatchers. It returns each message as its registered closed type, with its tenant, and without its publish tries.
- `ClaimDueAsync` takes and returns the due messages in the order of their `NextTryAtUtc`. Messages with the same `NextTryAtUtc` come in the order of their add, as D144 gives for each store. A dispatch composite of D125 keeps this order only inside the storage of one key (section 4.5).
- `IOutboxDispatchStore.ClaimDueAsync` claims only messages of the given channel, and leaves the messages of the other channels unchanged (D104).
- `ClaimResult.NextDueAtUtc` is the earliest `NextTryAtUtc` of the pending messages after the claim, which includes the lease ends of the claimed messages: of the given channel for the outbox, or of the inbox. It is null when no pending message exists. A value at or before `nowUtc` means that more messages are due now. A claim that returns `maxCount` messages can return `nowUtc` with no read (D103).
- `IOutboxDispatchStore.ClaimDueAsync` claims nothing for a channel with a down state, and returns that state as `ClaimResult.Down` (D106).
- `SetChannelDownAsync` with the reason `Errors` adds a `Down` row only on an up channel, and returns true when it added one. So the first loop that finds the errors wins, and the others change nothing. With the reason `Operator`, it adds a row unless the channel is paused, so a pause replaces a down state with the reason `Errors`, and a second pause keeps the first note (D277).
- `SetChannelUpAsync` adds an `Up` row at `upAtUtc` on a down channel, and returns true when it added one. With a reason, it changes only a down state with that reason, so a health check never ends a pause of a person.
- Each row has the version of the latest row plus 1, `PreviousKind`, and `PreviousDuration` in whole milliseconds. A store keeps a unique key on the channel and the version. When another caller added the version first, the store reads the latest row again, at most three times, and then gives `Errors.Conflict` (`ChannelState.Change`).
- `TryStartChannelCheckAsync` sets `NextCheckAtUtc` of the latest row to `nextCheckAtUtc`, and returns true, only when the channel is down with the reason `Errors` and its `NextCheckAtUtc` is at or before `nowUtc`. It is atomic between concurrent publisher instances, so one publisher instance at a time runs the health check.
- `GetDownChannelsAsync` returns the down state of each channel whose latest row is `Down`. `GetChannelStatesAsync` returns the rows at or after `fromUtc` and the row of each channel that was current at `fromUtc`, newest first. A store keeps all rows.
- `IDispatchSignal.NotifyAsync` returns at once, and never waits for a reader. `WaitAsync` returns null when a notification for the topic arrived after the last wait for that topic, also before the first wait. It returns `Errors.OperationCancelled` when the token is cancelled, and another `Error` when the signal does not work, for example a broken pipe or a change stream that does not work. A signal between processes can give the error of an earlier notification from `NotifyAsync`, for example the MongoDB signal while its inserts fail, or the RabbitMQ signal while it has no connection. A loop then waits until the earliest of its times of section 4.9, which include a time that a worker gives during the wait, or for `PollInterval` when it has no such time (D102, D103).
- `ITransport.Channel` is the channel of the transport, and one transport exists for each channel (D129). `SendAsync` sends `message.Content`. With a `redirectTo` value, it sends to that test address or test group in place of the target of the message (section 4.8).
- `ITransport.CheckHealthAsync` returns null when the channel can send. A transport with nothing to check returns null.
- `IBrokerClient.PublishAsync` publishes `Payload` with its run-time type and all `Headers`. The broker transport builds the headers (phase 6), so a client adds no header of its own. It can also give `MessageId` and `Tenant` to the matching fields of its library (D134). A direct client of an SDK writes `Payload` with `PayloadJson`, and selects its destination with `ContentType` (D299). The broker transport finds the entry of the content type and the version of the message in `IContentTypeRegistry`, and takes `PayloadJson` from the JSON options of the envelope with the run-time type of the payload. A message with no entry, or a payload type that the options do not know, gives `Errors.ValidationFailed` with no publish. So a publisher host needs the registration of each broker message type that it sends.
- `IOutboxDispatchStore.RecordTryAsync` changes the stored message of `message` in one atomic operation:
  1. It adds the publish try.
  2. It sets `TryCount` to the try number, `LastTryAtUtc` to the finish time, and `LastError` to `"{ErrorName}: {ErrorMessage}"`, or to null for a success. `PublishTry.ErrorText()` gives this text, and only the error name for a try with no error message.
  3. It sets the status: `Done` for `Succeeded` and `Suppressed`, `Failed` for `PermanentError`, `Expired`, or a `TransientError` with a null `nextTryAtUtc`, and `Pending` with the new `NextTryAtUtc` for a `TransientError` with a next try time. `PublishTry.StatusAfter(nextTryAtUtc)` gives this status, so each store uses one rule. The outcome `Cancelled` comes only from `CancelAsync` (section 4.19), and gives the status `Cancelled` (D166).
  4. It ends the lease.
- `RecordTryAsync` changes nothing when the stored `TryCount` is not the try number minus 1. So a dispatcher can repeat the call after a store failure, and a try is never recorded two times.
- `IInboxDispatchStore.CompleteAsync` and `FailAsync` add 1 to `TryCount` of the stored message of `message`, set `LastTryAtUtc`, and end the lease. `FailAsync` with `nextTryAtUtc` null marks the message as failed permanently.
- `DeleteTestDataAsync` of a dispatch store deletes each stored message with `MessageFlags.Test` of all tenants, with its publish tries, and returns the number of deleted messages. It deletes no message without the flag.
- `PurgeAsync` of a dispatch store deletes up to `maxCount` finished messages of all tenants, with their publish tries, and returns the number of deleted messages (D128). A finished message has the status `Done`, `Failed`, or `Cancelled`, and a finish time before `finishedBeforeUtc`. The finish time is `LastTryAtUtc`, or `CreatedAtUtc` for a message with no try, for example a received message that a person cancelled. It never deletes a pending message.
- `CountAsync` of a tenant store counts the messages of its tenant. `IOutboxDispatchStore.CountAsync` counts the messages of all tenants, for the state of the channels (section 4.19).
- `RunAsync` runs until the token is cancelled, and then returns. It has no failure result, so it returns a plain `ValueTask`. It handles the error of each item inside the loop, and a store error only delays the next claim. It does not throw `OperationCanceledException`.
- A caller must not await one `ValueTask` two times. The XML documentation of each interface says this.

Contract rules of the subscription router:
- `ISubscriptionStore.FindForDeliveryAsync` returns, for one message type, the default subscriptions, the subscriptions of the given companies, and the subscriptions of the given users, in one call. It does no resolution. The subscription router resolves the levels (section 4.17).
- `ISubscriptionStore.QueryAsync` returns the subscriptions that match all given filters. A null filter matches each value.
- `ISubscriptionStore.AddAsync` returns `Errors.Conflict` for a second subscription with the same owner, message type, channel, and recipient kind. `UpdateAsync` returns `Errors.Conflict` when the stored revision is not `expectedRevision`, and `Errors.NotFound` for an unknown id. `DeleteAsync` succeeds also for an unknown id.
- `IRecipientDirectory` returns the profiles of known users of its tenant only, and leaves out an unknown user without an error. `GetCompanyAsync` returns null for an unknown company. A user id or a company id of another tenant is unknown (D114).
- `IRoutingVerifier.VerifyAsync` stores nothing and sends nothing (section 4.18).

### 4.7 Dependency injection

The consumer registers MsgHatch with one fluent call chain. Each technology needs exactly one call (D60). This host uses the subscription router for one message type, and its own router for another:

```csharp
services.AddMsgHatch(o => o.DefaultLanguage = "en")
    .UseSubscriptions()
    .UseRecipientDirectory<AppRecipientDirectory>()
    .UseSqliteSubscriptionStore()
    .UseFusionSubscriptionCache(o => o.Expiration = TimeSpan.FromMinutes(1))
    .UseEmbeddedTemplates(typeof(Program).Assembly, "ExampleApp.Templates")
    .UseSqliteStores("Data Source=msghatch.db")
    .UseAmazonSes(o => { o.SenderEmail = "noreply@example.com"; o.SenderName = "Example"; })
    .UseSignalR<NotificationHub>()
    .AddMessage<OrderShipped>("OrderShipped", 2, AppJson.Default.OrderShipped,
        m => m
            .WithScope(e => NotificationScope.ForUsers(e.CustomerIds))
            .WithUpgrade<OrderShippedV1>(1, AppJson.Default.OrderShippedV1,
                v1 => new OrderShipped(v1.OrderId, [v1.CustomerId]))
            .WithPayload<OrderShippedEmail, OrderShippedEmailLoader>(p => p
                .AsEmail("OrderShippedEmail"))
            .WithPayload<OrderShippedUiUpdate, OrderShippedUiLoader>(p => p
                .AsSignalR("OrderShippedUiUpdate", 1, AppJson.Default.OrderShippedUiUpdate)))
    .AddMessage<PasswordReset>("PasswordReset", 1, AppJson.Default.PasswordReset,
        m => m
            .WithRouter<PasswordResetRouter>()
            .WithPayload<PasswordResetEmail, PasswordResetEmailLoader>(p => p.AsEmail("PasswordResetEmail")));
```

`UseSqliteSubscriptionStore()` needs no connection string here, because it uses the `SqliteStoreOptions` that `UseSqliteStores` sets (D59). A host with no subscriptions leaves out the first four calls, and gives each message type its own router with `WithRouter`. A host with separate processes puts the registration of the message types into one shared method, and each process calls it (section 4.9).

**Confirmed rule (D60):** each implementation package has one setup extension method on `MsgHatchBuilder`. One call in the host registers all parts of that package:
- Its implementations of the domain interfaces.
- The logging wrappers of these implementations, and the caching wrapper where it applies.
- Its options.
- Each third-party service that it needs, with `TryAdd`, so that a service that the host already registered stays.

Each `Action<TOptions>` parameter is optional, because the options also bind from their configuration section (D65). An overload with a `string` parameter sets the one required option of the package in code.

A setup method calls the generic domain method of each interface that it implements, for example `UseOutboxStore<TStore>()`. So each package gets the same wrappers, the same wrapper order, and the same validation. Each generic domain method also has an overload with a factory, `Func<IServiceProvider, TInterface>`, or `Func<IServiceProvider, TenantKey, TInterface>` for a tenant service, for example for a test substitute. `MsgHatchBuilder.Services` and `MessageBuilder<TEvent>.Services` give the `IServiceCollection` to the extension methods of other packages, for example `WithScope` of `MsgHatch.Subscriptions`.

Tenant services (D126). The generic domain method of a tenant service registers it with `KeyedService.AnyKey`:
- For each key, it creates the implementation with `ActivatorUtilities.CreateInstance`, and puts the wrappers around it. It gives `ActivatorUtilities` a small internal service provider for that key: this provider resolves each tenant service with the same key, each keyed service with its own key, and each other service as it is. So a storage package can resolve its keyed store of the key, for example `UseSqliteTenantStores`. So a router gets the store and the directory of its own tenant, and a composer the loader and the engine of its own tenant.
- An implementation that needs its tenant takes a `TenantKey` parameter in its constructor, and gets the key.
- It adds an unkeyed registration that resolves the service of `TenantKey.None`. So a host with one tenant injects each service with no key. The template store of a tenant is the one exception: it has no unkeyed registration, because the unkeyed `ITemplateStore` is the shared template store (section 4.10).
- A singleton tenant service has one instance for each key. A scoped tenant service has one instance for each key in each DI scope.
- Phase 1 checks these premises in the pinned version of `Microsoft.Extensions.DependencyInjection`: an `AnyKey` singleton gives one instance for each requested key, and a keyed resolution with an unknown key uses the `AnyKey` registration.

Names (D61): a method that selects an implementation starts with `Use`. A method that registers a message type starts with `Add`. A method that registers a form of a message type on one channel starts with `As`, for example `AsEmail`, because it describes the form of the message, not a destination. A method that adds an option to a message type or a channel registration starts with `With`.

Domain methods (`MsgHatch.Domain.DependencyInjection`):

| Method | Effect |
|---|---|
| `AddMsgHatch(Action<MsgHatchOptions>?, string? configSectionPath)` | Registers the core services: the in-memory `IMessageRegistry` from the registrations (D117), `InProcessDispatchSignal` as the `IDispatchSignal`, the named `RetryOptions` `Inbox`, the named `DispatchOptions` `Inbox` and `Outbox`, `IInboxProcessor` and `IOutboxDispatcher` with their logging wrappers, the in-process `IRegistryQueries` and `IRegistryCommands` as the default tenant services with their logging wrappers (D268, D269, D283), the in-process `IDeliveryQueries` and `IDeliveryCommands` as the default scoped tenant services with their logging wrappers (D289), and `TimeProvider.System` with `TryAddSingleton`. Only the first call binds the options and adds their validators. A later call reuses the setup and applies only its action from code, so a value from code wins over the configuration in each order of the calls (D314). The options bind from the root `configSectionPath`, a full path, for example `App:Messaging`, or `MsgHatchOptions.DefaultSectionKey` for null. An empty root or a root of white space throws `ArgumentException`. The first call sets the root, a later call with null keeps it, and a later call with another root fails the startup validation (D328). `MsgHatchBuilder.ConfigSectionPath` gives the root, and `SectionPathOf(sectionKey)` gives `{ConfigSectionPath}:{sectionKey}`, the path of a section of a package. Returns `MsgHatchBuilder`. |
| `AddChannelType<TSettings>(ChannelDescriptor<TSettings>, int settingsVersion, JsonTypeInfo<TSettings> settingsJson, IChannelSettingsValidator)` | Registers a channel: its descriptor, the content type `Settings:{Channel}` of its settings type with the version, and the settings validator. A second call for the same channel id changes nothing. The first `As<Channel>` registration of a channel package calls it (D77). |
| `AddTenantService<TService>(ServiceLifetime, Func<IServiceProvider, TenantKey, TService> factory, wrapWithLogging?)` | Registers a tenant service of a package (D126): one instance for each `TenantKey`, and an unkeyed registration that gives the instance of `TenantKey.None`. The service provider of the factory gives the key and each tenant service of the same key. The logging wrapper is added only with `EnableLogging`. The channel and router packages call it, for example the email package for the engine of a payload type. |
| `AddDefaultTenantService<TService>(ServiceLifetime, factory, wrapWithLogging?)` | Registers the default implementation of a tenant service of a package, as `AddTenantService` does, only when the service collection has no registration of the service type (D231). So a selection wins in each order of the setup calls, and a second default adds nothing. |
| `UseTenantService<TService>(ServiceLifetime, factory, wrapWithLogging, setupMethod)` | Selects the implementation of a tenant service of a package (D231). It records the selection for the setup check, removes each registration of the service type, and registers the factory as `AddTenantService` does. Two selections of one service type give the setup error "… select an implementation of …" (D62). |
| `AddTraceType<TTrace>(string name, int version, JsonTypeInfo<TTrace>)` | Registers a route trace type with the content type `Trace:{name}` and the version. `TTrace` derives from `RouteTrace`. The router packages call it (D98). |
| `UseRouter(IOpenRouterFactory factory)` | Selects the factory of the router of each message type with no router of its own, for example the factory of `SubscriptionRouter<TEvent>` (D99, D245). The router packages call it. `IOpenRouterFactory.Create<TEvent>(tenantServices, key)` makes the router of one event type with a generic method, so no code closes an open generic type at run time (D237). The startup validation reports a message type whose router the factory cannot make. |
| `AddExceptionClassifier<TClient, TClassifier>()` | Domain | Adds an `IExceptionClassifier<TClient>` as a singleton, with `TryAddEnumerable` (D141). A client can have many classifiers, for example one for each Wolverine transport of the host. A second call with the same classifier type adds nothing. |
| `UseInMemoryStores()` | Selects `InMemoryInboxStore` and `InMemoryOutboxStore`, with their two parts and their logging wrappers, for a host with no database (D119). From phase 16, it also registers `InMemoryRegistrySettingsStore`. |
| `UseStorageForEachTenant<TTenantSource>(Func<IServiceProvider, TenantKey, IInboxStore> inbox, Func<IServiceProvider, TenantKey, IInboxDispatchStore> inboxDispatch, Func<IServiceProvider, TenantKey, IOutboxStore> outbox, Func<IServiceProvider, TenantKey, IOutboxDispatchStore> outboxDispatch, string? setupMethod)` | Registers the consumer's tenant source as a singleton with its logging wrapper. An overload takes a factory of the tenant source, `Func<IServiceProvider, ITenantSource>`, for example for a test substitute. Registers the tenant stores of each key from the functions, each with its logging wrapper and an internal wrapper that clears the next due time of its tenant in the dispatch composite after a successful `AddAsync` or `RetryAsync`. Registers `TenantInboxDispatchStore` and `TenantOutboxDispatchStore`, with their logging wrappers, as the dispatch stores (D125). The storage packages call it. |
| `UseStoredRegistry()` | Selects `StoredRegistry` as `IMessageRegistry` and `IRegistryWriter`, on the registered `IRegistrySettingsStore` (D118, section 4.20). |
| `UseRegistryQueries(factory, setupMethod?)` | Selects the implementation of `IRegistryQueries`, for example an HTTP client, with `UseTenantService`, as a scoped tenant service with its logging wrapper (D231, D269). It replaces the in-process default of `AddMsgHatch` in each order of the calls. |
| `UseRegistryApiClient(Action<RegistryApiClientOptions>?, Action<IHttpClientBuilder>?)`, `UseRegistryApiClient(Uri, Action<IHttpClientBuilder>?)` | In `MsgHatch.Api.Client`: `RegistryApiClientOptions`, a named HTTP client, and the HTTP client of `IRegistryQueries` and `IRegistryCommands` through `UseRegistryQueries` and `UseRegistryCommands` (D271, D283, section 4.23). |
| `UseDeliveryQueries(factory, setupMethod?)`, `UseDeliveryCommands(factory, setupMethod?)` | Selects the implementation of `IDeliveryQueries` or `IDeliveryCommands`, for example an HTTP client, with `UseTenantService`, as a scoped tenant service with its logging wrapper (D231, D289). It replaces the in-process default of `AddMsgHatch` in each order of the calls. |
| `UseDeliveryApiClient(Action<DeliveryApiClientOptions>?, Action<IHttpClientBuilder>?)`, `UseDeliveryApiClient(Uri, Action<IHttpClientBuilder>?)` | In `MsgHatch.Api.Client`: `DeliveryApiClientOptions`, a named HTTP client, and the HTTP client of `IDeliveryQueries` and `IDeliveryCommands` through `UseDeliveryQueries` and `UseDeliveryCommands` (D294, section 4.23). |
| `UseRegistrySettingsStore<TStore>()` | Registers a registry settings store with its logging wrapper. The storage packages call it. |
| `UseInboxStore<TStore>()`, `UseOutboxStore<TStore>()` | Register a tenant store as a tenant service, with its logging wrapper. |
| `UseInboxDispatchStore<TStore>()`, `UseOutboxDispatchStore<TStore>()` | Register a dispatch store as a singleton, with its logging wrapper. The storage packages call these methods and the tenant store methods, with the factory overloads. Each factory overload takes an optional `setupMethod`, and a storage package gives the name of its own setup method, so that the message of D62 names it, for example `UseFileStores`. A host calls them for its own store, or to use two different technologies for the inbox and the outbox. |
| `UseDispatchSignal<TSignal>()`, `UseDispatchSignal(Func<IServiceProvider, IDispatchSignal>, string? setupMethod)` | Selects another `IDispatchSignal`, for example one between processes (D102). A signal package calls the factory overload with the name of its own setup method, so that the message of D62 names it, for example `UseNamedPipeDispatchSignal`. |
| `UseTransport<TTransport>()` | Registers the `ITransport` of one channel as a singleton, with its logging wrapper (D129). The channel packages call it, with the factory overload. A second transport for the same channel fails the startup validation. |
| `ConfigureInboxDispatch(Action<DispatchOptions>)`, `ConfigureOutboxDispatch(Action<DispatchOptions>)` | Change the dispatch options. |
| `AddMessage<TEvent>(name, version, JsonTypeInfo<TEvent>, configure)` | Registers one message type. `configure` receives a `MessageBuilder<TEvent>`. |
| `MessageBuilder<TEvent>.WithRouter<TRouter>()` | Selects the router of this message type. It wins over the router of the factory of `UseRouter` (D99). |
| `MessageBuilder<TEvent>.UsePayloadLoader<TPayload, TLoader>()` | Registers the payload loader of one payload type of this message type as a tenant service, with its logging wrapper. `WithPayload` calls it. A second call with the same loader type adds nothing. A call with another loader type for the same payload type fails the startup validation (D135). |
| `MessageBuilder<TEvent>.WithPayload<TPayload, TLoader>(Action<PayloadBuilder<TEvent, TPayload>> channels)` | Registers the payload loader of one payload type with `UsePayloadLoader`, apart from the channels, and then the channel registrations of `channels` that use that payload type (D136). A factory overload `WithPayload<TPayload>(Func<IServiceProvider, TenantKey, IPayloadLoader<TEvent, TPayload>>, channels)` takes a loader factory, for example for a test substitute. |
| `MessageBuilder<TEvent>.WithEventPayload(Action<PayloadBuilder<TEvent, TEvent>> channels)` | Registers the domain event as the payload of the channel registrations of `channels`, with the internal loader `EventPayloadLoader<TEvent>` and `UsePayloadLoader` (D139). The loader returns the event of the received message for each route. The channel registrations are the same as with `WithPayload`, for example `p => p.AsBroker("OrderShipped", 1, AppJson.Default.BrokerEnvelopeOrderShipped)`. |
| `PayloadBuilder<TEvent, TPayload>` | The builder of one payload registration. `Services` and `Parent` give the `IServiceCollection` and the `MsgHatchBuilder` to the channel packages. `AsChannel<TEnvelope, TComposer>(channel, name, version, JsonTypeInfo<TEnvelope>, retry, configure)` calls `MessageBuilder<TEvent>.AsChannel`, with a composer that gets the loader of the payload type through DI. |
| `MessageBuilder<TEvent>.WithUpgrade<TOld>(version, JsonTypeInfo<TOld>, Func<TOld, TEvent>)` | Registers an old event version (section 4.13). |
| `ConfigureTestDelivery(ChannelId, Action<TestDeliveryOptions>)` | Changes the test delivery of one channel (D83). The options also bind from `{root}:TestDelivery:{Channel}`, by default `MsgHatch:TestDelivery:{Channel}`, and a value from code wins. The order of the setup calls does not change the result. |
| `ConfigureSharedOptions<TOptions>(string setupMethod, Action<TOptions> configure)` | Applies a value in code to a shared options class with `PostConfigure`, and records the setup method that gives it (D150). A second setup method with a value in code for the same options class fails the startup validation, and the message names both methods. The storage packages call it. |
| `ConfigureChannelDown(ChannelId, Action<ChannelDownOptions>)` | Changes the error threshold and the check interval of one channel (D86). The options also bind from `{root}:ChannelDown:{Channel}`, by default `MsgHatch:ChannelDown:{Channel}`, and a value from code wins. The order of the setup calls does not change the result. |
| `MessageBuilder<TEvent>.AsChannel<TEnvelope, TComposer>(channel, name, version, JsonTypeInfo<TEnvelope>, retry, configure)` | The generic channel registration. The channel packages call it through `PayloadBuilder<TEvent, TPayload>.AsChannel`, after `AddChannelType`. `TComposer` is the `IEnvelopeComposer<TEvent, TEnvelope>` of the channel, a tenant service (D96). `retry` gives the default `RetryOptions` values of the channel, for example 3 tries and an expiry of 5 minutes for SignalR. `configure` receives a `ChannelBuilder<TEnvelope>` with `WithUpgrade<TOldEnvelope>` and `WithRetry(Action<RetryOptions>)` (D87). |

`AddMessage` registers `IMessageRouter<TEvent>` of its message type as a tenant service, with the logging wrapper `MessageRouterLoggingWrapper<TEvent>`. For each key, the registration creates the router of `WithRouter`. A message type with no `WithRouter` gets the router of `IOpenRouterFactory.Create<TEvent>` of `UseRouter`. So `WithRouter` wins over `UseRouter`, and the DI container has no open generic router registration. Phase 1 tests this for a key and for no key.

Setup methods of `MsgHatch.Subscriptions` and its packages (D97):

| Method | Package | One call registers |
|---|---|---|
| `UseSubscriptions()` | Subscriptions | The factory of `SubscriptionRouter<TEvent>` with `UseRouter`, `AddTraceType` for `ResolutionTrace`, and `ISubscriptionQueries`, `ISubscriptionCommands`, and `IRoutingVerifier` as tenant services, each with its logging wrapper (D231). These three are the defaults of `AddDefaultTenantService`. |
| `UseSubscriptionQueries(factory, setupMethod?)`, `UseSubscriptionCommands(factory, setupMethod?)`, `UseRoutingVerifier(factory, setupMethod?)` | Subscriptions | Selects the implementation of one interface, for example an HTTP client, with `UseTenantService`, as a scoped tenant service with the logging wrapper of the package (D231). Each one replaces the default of `UseSubscriptions` of its interface only, in each order of the calls. A package that calls it gives its own name as `setupMethod`, so a second selection names it. |
| `UseSubscriptionApiClient(Action<SubscriptionApiClientOptions>?, Action<IHttpClientBuilder>?)`, `UseSubscriptionApiClient(Uri, Action<IHttpClientBuilder>?)`, and the same forms of `UseSubscriptionQueriesApiClient` and `UseSubscriptionCommandsApiClient` | Subscriptions.Api.Client | `SubscriptionApiClientOptions`, a named HTTP client for the queries and the verifier and one for the commands, and the HTTP client of the queries with the verifier, the commands, or both, through the selection methods of D231 (D227, D232, section 4.23). |
| `UseSubscriptionStore<TStore>()` | Subscriptions | A subscription store as a tenant service, with its logging wrapper. The store packages call it with the factory overload. A host calls it only for its own store. |
| `UseInMemorySubscriptionStore()` | Subscriptions | `InMemorySubscriptionStore`, with its logging wrapper, for a host with no database. Each key has its own instance. |
| `UseRecipientDirectory<TDirectory>()` | Subscriptions | The consumer's recipient directory as a tenant service, with its logging wrapper (D76). |
| `UseSubscriptionCache<TBackend>(Action<SubscriptionCacheOptions>?)` | Subscriptions | The cache backend, `ISubscriptionCache` as a tenant service, and the caching wrapper between the logging wrapper and the store (D40). The cache packages call it. A host calls it only for its own backend. |
| `MessageBuilder<TEvent>.WithScope(Func<TEvent, NotificationScope>)` | Subscriptions | The scope function of the message type (D16). The subscription router needs it. |
| `MessageBuilder<TEvent>.WithTestEvent(testCase, Func<UserId, TEvent>)` | Subscriptions | One test case of the message type for the routing verification (section 4.18). |
| `UseFileSubscriptionStore(Action<FileStoreOptions>?)`, `UseFileSubscriptionStore(string rootPath)` | Subscriptions.Data.FileSystem | `FileStoreOptions` of the core package, and `FileSystemSubscriptionStore` under `{RootPath}/subscriptions` with its logging wrapper. A value in code follows the rule of the shared options below. |
| `UseSqliteSubscriptionStore(Action<SqliteStoreOptions>?)`, `UseSqliteSubscriptionStore(string connectionString)` | Subscriptions.Data.Sqlite | `SqliteStoreOptions` of the core package, and `SqliteSubscriptionStore` with its logging wrapper. The store creates its tables one time for each connection string. |
| `UseSqliteTenantSubscriptionStore(Action<SqliteTenantStoreOptions>?)` | Subscriptions.Data.Sqlite | `SqliteTenantStoreOptions` of the core package, and one `SqliteSubscriptionStore` for each key, on the file of that key: `TenantConnectionString` for a tenant, and `SystemConnectionString` for `TenantKey.None` (D125). |
| `UseEntityFrameworkSubscriptionStore(Action<DbContextOptionsBuilder> configure)` | Subscriptions.Data.EFCore | The EF Core subscription store on a pooled `IDbContextFactory` of its own `DbContext`, with `EntityFrameworkStoreOptions` of the core package and its logging wrapper, and the migrations of its context with the history table `__SubscriptionMigrations` (D320, D321). The store asks the `IExceptionClassifier<MsgHatchSubscriptionDbContext>` registrations (D322). |
| `UseEntityFrameworkSqlServerSubscriptionStore(string connectionString, Action<SqlServerDbContextOptionsBuilder>?)` | Subscriptions.Data.EFCore.SqlServer | `UseEntityFrameworkSubscriptionStore` with `UseSqlServer`, the dialect of `MsgHatch.Data.EFCore.SqlServer`, and the migrations of `MsgHatchSubscriptionDbContext` in the package (D318, D320). |
| `UseEntityFrameworkPostgreSqlSubscriptionStore(string connectionString, Action<NpgsqlDbContextOptionsBuilder>?)` | Subscriptions.Data.EFCore.PostgreSql | `UseEntityFrameworkSubscriptionStore` with `UseNpgsql`, the dialect of `MsgHatch.Data.EFCore.PostgreSql`, and the migrations of `MsgHatchSubscriptionDbContext` in the package (D318, D320). |
| `UseEntityFrameworkMySqlSubscriptionStore(string connectionString)` | Subscriptions.Data.EFCore.MySql | `UseEntityFrameworkSubscriptionStore` with `UseMySql` on MySQL 8.4, the dialect of `MsgHatch.Data.EFCore.MySql`, and the migrations of `MsgHatchSubscriptionDbContext` in the package (D320, D325). |
| `UseMongoDbSubscriptionStore(Action<MongoDbStoreOptions>?)` | Subscriptions.Data.MongoDb | The MongoDB subscription store, with its logging wrapper. It shares `MongoDbStoreOptions` and the `IMongoClient` with the core MongoDB stores. A value in code follows the rule of the shared options below. |
| `UseHybridSubscriptionCache(Action<SubscriptionCacheOptions>?, Action<HybridCacheOptions>?)` | Subscriptions.Caching.Hybrid | `HybridCacheSubscriptionBackend`, the caching wrapper, and HybridCache with `AddHybridCache`. A HybridCache that the host already registered stays, with the host's settings. |
| `UseFusionSubscriptionCache(Action<SubscriptionCacheOptions>?, Action<IFusionCacheBuilder>?)` | Subscriptions.Caching.FusionCache | `FusionCacheSubscriptionBackend`, the caching wrapper, and the named FusionCache `MsgHatch.Subscriptions`. The second callback configures that cache, for example a distributed level or a backplane. The host's own FusionCache instances do not change. |

Shared options (D59). A subscription store package uses the options class of its core package, for example `SqliteStoreOptions`. Its setup method with a value in code sets the same options as the core setup method. If both methods give a value in code, the startup validation fails, and its message names both methods (D62). So the outbox and the subscriptions of one technology always use one storage, and a value never changes the other one with no warning. Each of the two methods binds the configuration section, and gives its value in code through `ConfigureSharedOptions` (D150).

`MsgHatch.Subscriptions.AspNetCore` needs no setup method, because its endpoints need only `ISubscriptionQueries`, `ISubscriptionCommands`, `IRoutingVerifier`, and `IContentTypeRegistry`, which `AddMsgHatch` registers. The registry gives the JSON type information of the `settings` object of a body (section 4.17). `IEndpointRouteBuilder.MapMsgHatchSubscriptions(prefix, configure?)` maps the HTTP endpoints of section 4.17 on the web application.

`MsgHatch.AspNetCore` needs no setup method either, because its endpoints need only the delivery queries and commands, `IMessageRegistry`, and the registry queries and commands of `AddMsgHatch` (sections 4.19 and 4.20). `MapMsgHatchDelivery(prefix)` and `MapMsgHatchDeliveryContent(prefix)` map the HTTP endpoints of section 4.19, and `MapMsgHatchRegistry(prefix)` the endpoints of section 4.20.

`MsgHatch.Templates.AspNetCore` needs no setup method either, because its endpoints need only `ITemplateQueries` and `ITemplateCommands`, which each template setup method registers (D231). `MapMsgHatchTemplates(prefix)` maps the HTTP endpoints of section 4.22.

Setup methods of the other implementation packages (D60):

| Method | Package | One call registers |
|---|---|---|
| `UseFileStores(Action<FileStoreOptions>?)`, `UseFileStores(string rootPath)` | Data.FileSystem | `FileStoreOptions`, and `FileSystemInboxStore` under `{RootPath}/inbox` and `FileSystemOutboxStore` under `{RootPath}/outbox`, with their two parts and their logging wrappers. From phase 16, also the file registry settings store under `{RootPath}/registry`. |
| `UseSqliteStores(Action<SqliteStoreOptions>?)`, `UseSqliteStores(string connectionString)` | Data.Sqlite | `SqliteStoreOptions`, and `SqliteInboxStore` and `SqliteOutboxStore` on one database, with their two parts and their logging wrappers. From phase 16, also the SQLite registry settings store on the same database. The stores create their tables one time for each connection string. |
| `UseSqliteTenantStores<TTenantSource>(Action<SqliteTenantStoreOptions>?)` | Data.Sqlite | `SqliteTenantStoreOptions`, one `SqliteInboxStore` and `SqliteOutboxStore` for each key on the file of that key, as the tenant stores, and the dispatch composites, through `UseStorageForEachTenant<TTenantSource>`. The key `TenantKey.None` and, from phase 16, the registry settings store use `SystemConnectionString` (D125). |
| `UseEntityFrameworkStores(Action<DbContextOptionsBuilder> configure, Action<EntityFrameworkStoreOptions>?)` | Data.EFCore | `EntityFrameworkStoreOptions`, a pooled `IDbContextFactory<MsgHatchDbContext>` with the options of `configure` on a relational provider, for example `o => o.UseSqlite(connectionString)`, the EF Core inbox, outbox, and registry settings stores, with their logging wrappers, and the migrations of the context with the history table `__StoreMigrations` (D120, D130, D318, D320, D321). The stores ask the `IExceptionClassifier<MsgHatchDbContext>` registrations (D322). |
| `UseEntityFrameworkSqlServerStores(string connectionString, Action<EntityFrameworkStoreOptions>?, Action<SqlServerDbContextOptionsBuilder>?)` | Data.EFCore.SqlServer | `UseEntityFrameworkStores` with `UseSqlServer`, the dialect `SqlServerStoreDialect`, and the migrations of `MsgHatchDbContext` in the package (D318, D320). `MigrateMsgHatchAsync` and `ScriptMsgHatchMigrationsAsync` of `MsgHatch.Data.EFCore` apply and script the migrations of each registered MsgHatch context. |
| `UseEntityFrameworkPostgreSqlStores(string connectionString, Action<EntityFrameworkStoreOptions>?, Action<NpgsqlDbContextOptionsBuilder>?)` | Data.EFCore.PostgreSql | `UseEntityFrameworkStores` with `UseNpgsql`, the dialect `PostgreSqlStoreDialect`, and the migrations of `MsgHatchDbContext` in the package (D318, D320). |
| `UseEntityFrameworkMySqlStores(string connectionString, Action<EntityFrameworkStoreOptions>?)` | Data.EFCore.MySql | `UseEntityFrameworkStores` with `UseMySql` on MySQL 8.4, the dialect `MySqlStoreDialect`, and the migrations of `MsgHatchDbContext` in the package (D318, D320, D325). The public API has no type of the provider, so the setup has no callback of the provider. |
| `UseMongoDbStores(Action<MongoDbStoreOptions>?)` | Data.MongoDb | `MongoDbStoreOptions`, an `IMongoClient` if the host has not registered one, and the MongoDB inbox, outbox, and registry settings stores, with their logging wrappers (D121). |
| `UseEmbeddedTemplates(Assembly, string prefix)`, `UseFileTemplates(Action<FileTemplateOptions>?)`, `UseFileTemplates(string rootPath)`, `UseTemplateStore<TStore>()` | Templates | The shared template store of each template engine, with its logging wrapper, and the template selector. From phase 25, also the template queries `ITemplateQueries` and the template commands `ITemplateCommands` as tenant services, one time for all template setup methods (D222, D231). |
| `UseTenantTemplateStore<TStore>()` | Templates | The template store of each tenant, as a keyed `ITemplateStore` with no unkeyed registration, with its logging wrapper. The shared template store is the unkeyed `ITemplateStore`, so the two never collide (D62). The template selector tries the store of the tenant first (D123). From phase 25, also the template queries and commands. |
| `payload.AddTemplateEngine<TEvent, TPayload>(registration, engineName, IReadOnlyList<TemplatePart> parts, Func<IServiceProvider, TenantKey, ITemplateEngine<TPayload>> engine)` | Templates | The template engine of one payload type as a tenant service, with its logging wrapper, for one registration of a channel (D212), with the name of the engine, the message type of the payload builder, and the parts that the channel reads (D220, D240). The name is `ITemplateEngineFactory.Name`, for example `liquid`, or `PlaceholderTemplates.EngineName`, which is `placeholder`. An empty part list, or a part name that is not a name of ASCII letters, digits, and `_` that starts with a letter, gives an `ArgumentException`. `AsEmail` calls it with `subject` (`PlainText`), `body` (`Html`), and `body` (`PlainText`). The registrations of one payload type must give the same engine factory (D62). It also registers the template queries and commands. |
| `UseTemplateQueries(factory, setupMethod?)`, `UseTemplateCommands(factory, setupMethod?)` | Templates | Selects the implementation of one interface, for example an HTTP client, with `UseTenantService`, as a scoped tenant service with its logging wrapper (D231, D244). Each one replaces the in-process default of the template setup methods of its interface only, in each order of the calls. |
| `UseTemplateApiClient(Action<TemplateApiClientOptions>?, Action<IHttpClientBuilder>?)`, `UseTemplateApiClient(Uri, Action<IHttpClientBuilder>?)`, and the same forms of `UseTemplateQueriesApiClient` and `UseTemplateCommandsApiClient` | Templates.Api.Client | `TemplateApiClientOptions`, a named HTTP client for each interface, and the HTTP client of the template queries, the commands, or both, through `UseTemplateQueries` and `UseTemplateCommands` (D227, section 4.23). |
| `UseEmailClient<TClient>()` | Channels.Email | A custom `IEmailClient` and the email transport, with their logging wrappers, and the email channel with `AddChannelType`. So a publisher host with no message type also binds `{root}:ChannelDown:Email` and `{root}:TestDelivery:Email`. |
| `UseAmazonSes(Action<AmazonSesOptions>?)`, `UseAmazonSes(string senderEmail)` | Channels.Email.AmazonSes | The options, `AmazonSimpleEmailServiceClient` as the `IEmailClient` with its logging wrapper, the email transport, and an `IAmazonSimpleEmailServiceV2` client for `AmazonSesOptions.Region`, if the host has not registered one. The AWS SDK finds the credentials. With no `Region` and no region in the AWS SDK configuration, the startup validation fails with an `OptionsValidationException` for `Region`. |
| `UseResend(Action<ResendOptions>?)` | Channels.Email.Resend | The options, `ResendEmailClient` as the `IEmailClient` with its logging wrapper, the email transport, and the named HTTP client `MsgHatch.Resend`, which the host can configure further (D138). It has no `string` overload, because the package has two required options, and the API key does not belong in code. |
| `UseSignalR<THub>()` | Channels.SignalR | `SignalRTransport<THub>` as the one `ITransport` of the SignalR channel, with its logging wrapper, the SignalR server services with `AddSignalR` (D129), and the SignalR channel with `AddChannelType`. So a publisher host with no message type also binds `{root}:ChannelDown:SignalR` and `{root}:TestDelivery:SignalR`. |
| `UseBrokerClient<TClient>()` | Channels.Broker | A custom `IBrokerClient` and the broker transport, with their logging wrappers. One transport serves each payload type (D129). It also registers the broker channel with `AddChannelType`, so a publisher host with no message type also binds `{root}:ChannelDown:Broker` and `{root}:TestDelivery:Broker`. |
| `UseWolverineBroker()` | Channels.Broker.Wolverine | `WolverineBrokerClient` as the `IBrokerClient` with its logging wrapper, and the broker transport. It uses the Wolverine of the host, because the host selects the transport of Wolverine, for example RabbitMQ or Amazon SQS (D134). Wolverine registers `IMessageBus` as a scoped service, so the client gets a new `MessageBus` on the `IWolverineRuntime` of the host for each publish. The client sends with `IMessageBus.SendAsync`, so a payload type with no Wolverine route gives the permanent `Errors.NotFound`. `PublishAsync` drops such a message with no error. The host configures Wolverine as D140 requires: `SendInline()` on the route of each broker payload type, `Durability.UseSyncRetryBlock = true`, and the publisher confirms of the transport. |
| `UseAmazonSnsBroker(Action<AmazonSnsOptions>?)` | Channels.Broker.AmazonSns | The options, `AmazonSnsBrokerClient` as the `IBrokerClient` with its logging wrapper, the broker transport, and an `IAmazonSimpleNotificationService` client for `AmazonSnsOptions.Region`, if the host has not registered one (D300). The AWS SDK finds the credentials. With no `Region` and no region in the AWS SDK configuration, the startup validation fails with an `OptionsValidationException` for `Region`. Each publish is one `Publish` call to the topic of the content type, or to the default topic: the payload JSON is the message, and each header is a `String` message attribute. The message id of SNS is the provider message id of the receipt. A content type with no topic gives `Errors.NotFound`. More than 10 headers, and a header name or value that SNS does not accept, give `Errors.ValidationFailed` with no call (D301). |
| `UseNamedPipeDispatchSignal(Action<NamedPipeSignalOptions>?)` | Dispatch.NamedPipes | `NamedPipeSignalOptions`, and the internal `NamedPipeDispatchSignal` as the `IDispatchSignal` through `UseDispatchSignal` (D189). Its startup checks: `EnablePolling` false in the dispatch options `Inbox` or `Outbox` fails (D190). On Linux and macOS, the pipe path of a topic of the host that is too long for the platform fails, and the message names the topic. The topics of a host are the inbox and each channel with a registered transport. |
| `UseMongoDbDispatchSignal()` | Data.MongoDb | `MongoDbDispatchSignal` as the `IDispatchSignal` through `UseDispatchSignal`, on the `IMongoClient` and the database of `UseMongoDbStores` (D303). Its startup checks: a host with no `UseMongoDbStores` fails, and `EnablePolling` false in the dispatch options `Inbox` or `Outbox` fails (D190). |
| `UseRabbitMqDispatchSignal(Action<RabbitMqSignalOptions>?)` | Dispatch.RabbitMq | `RabbitMqSignalOptions`, and the internal `RabbitMqDispatchSignal` as the `IDispatchSignal` through `UseDispatchSignal` (D305). Each host of the system calls it with the same broker and the same `ExchangeName`. Its startup checks: a missing or invalid `Uri` fails with an `OptionsValidationException` for `RabbitMqSignalOptions`, and `EnablePolling` false in the dispatch options `Inbox` or `Outbox` fails (D190). |

A transport registration makes the host a publisher of its channel. The outbox dispatcher of a host sends exactly the channels whose transport the host registered (D101).

Message channel registrations of the channel packages. Each one is an extension method of `PayloadBuilder<TEvent, TPayload>`, so a call gives no type argument, and C# infers the event type and the payload type (D136). Each one calls `AddChannelType` for its channel, and then `PayloadBuilder.AsChannel` with the library composer of its channel. `AsChannel` registers the composer as a tenant service. The composer gets the payload loader of the payload registration through DI. No channel registration names a loader type:

| Method | Package | Effect |
|---|---|---|
| `PayloadBuilder<TEvent, TPayload>.AsEmail(name, configure?)` | Channels.Email | Registers email with `EmailComposer<TEvent, TPayload>` and the placeholder engine. Requires `TPayload : IPlaceholderPayload<TPayload>`. The channel envelope is the library type `EmailMessage`, so the registration needs no version and no JSON type information (D17). |
| `PayloadBuilder<TEvent, TPayload>.AsEmail(name, ITemplateEngineFactory engine, configure?)` | Channels.Email | Registers email with another engine (D216), for example `LiquidTemplates.Engine` or a test substitute. The email registrations of one payload type share one engine. Two registrations of one payload type with two engine factories fail the startup validation, and the error names both registrations (D62). |
| `PayloadBuilder<TEvent, TPayload>.AsSignalR(name, version, JsonTypeInfo<SignalREnvelope<TPayload>>, configure?)` | Channels.SignalR | Registers a SignalR message. The channel envelope is `SignalREnvelope<TPayload>`. |
| `PayloadBuilder<TEvent, TPayload>.AsBroker(name, version, JsonTypeInfo<BrokerEnvelope<TPayload>>, configure?)` | Channels.Broker | Registers a broker message. The channel envelope is `BrokerEnvelope<TPayload>`. |

`MsgHatchBuilder` records each selection of an implementation. If two setup methods select an implementation for the same interface, for example `UseFileStores` and `UseSqliteStores`, the startup validation fails, and its message names both methods (D62).

Lifetimes (D130):
- Singleton: the options, the content type registry, `IMessageRegistry`, the library stores, the transports, the template stores with their caches, the cache backends, the dispatch signal, the dispatchers, and `TimeProvider`. Each one is thread-safe.
- Scoped: the routers, the payload loaders, the composers, the template engines, the recipient directory, `INotificationService`, `IDeliveryQueries`, `IDeliveryCommands`, `ISubscriptionQueries`, `ISubscriptionCommands`, `IRoutingVerifier`, and the pipeline.
- Only a received message gets a DI scope, because the routers and the payload loaders of the consumer run in it. The inbox processor makes one DI scope for each claimed message. A caller of `ProcessAsync`, `AcceptAsync`, or `PreviewAsync` resolves `INotificationService` in its own scope, for example the scope of an HTTP request or of a broker message. The outbox dispatcher makes no DI scope. It uses the singleton dispatch store and the singleton transport of its channel.
- A store of the consumer must be thread-safe too, for example an EF Core store on an `IDbContextFactory` that makes one `DbContext` for each operation.

Roles (D105). `MsgHatchOptions.Roles` tells the startup validation what the host must have. The DI registrations do not change with the role, so all processes can call the same registration code:
- `Consumer`: the host routes and composes. It runs the inbox processor, or calls `ProcessAsync`.
- `Publisher`: the host sends. It runs the outbox dispatcher for the channels of its transports.
- The default is both, for a host with one process.
- `None`: the host only calls the API clients, for example an admin UI (D230). It needs no router, no transport, and no store, and the startup validation gives an error for each message type that it registers.

Startup validation, with `ValidateOnStart`:
- An outbox tenant store and an outbox dispatch store are registered. A host with the role `None` needs none.
- The calls of `AddMsgHatch` give one root of the configuration sections. The message names the roots (D328).
- No interface has two selected implementations (D62). Each channel has at most one transport (D129).
- With `UseStoredRegistry`, a registry settings store is registered.
- At most one setup method gives a value in code to one shared options class (section 4.7, shared options).
- With the role `Consumer`, each registered message type resolves an `IMessageRouter<TEvent>` for `TenantKey.None` in a DI scope (D95). The subscription router resolves only for a message type with a scope function from `WithScope`.
- With the role `Consumer` and `UseSubscriptions`, a subscription store and a recipient directory are registered.
- With the role `Consumer`, each registration of a template engine (`AddTemplateEngine`, so each `AsEmail`) has a template store.
- With the role `Publisher`, the host registered at least one transport.
- With `UseAmazonSnsBroker`, the options have at least one topic, and each topic is the ARN of a standard topic (D301).
- With `UseWolverineBroker`, the host registered Wolverine, so that `IWolverineRuntime` resolves. The check does not resolve the scoped `IMessageBus`. `Durability.UseSyncRetryBlock` of Wolverine is true (D140).
- With `UseEntityFrameworkStores`, the EF Core provider of `MsgHatchDbContext` is relational, and a dialect of a provider package serves that provider. The message names the setup method and the provider (D175, D318).
- With `UseEntityFrameworkSubscriptionStore`, the same check for `MsgHatchSubscriptionDbContext` (D182).
- With `EntityFrameworkStoreOptions.RequireCurrentSchema`, each migration of each registered MsgHatch context is applied. The message names the context and the first migration that is not applied. The check reads only the history tables (D320).
- Each content type and version is unique.
- Each message type name is unique. Two registrations with one name and two versions have two different content types, so the check above does not find them.
- Each message type has at most one registration for each channel, and its channel registrations have different envelope types (D135).
- Each payload type of a message type has one loader type (D135).
- Each registered options class passes its attributes and its `IValidatableObject` rules (D64). For example, a missing `AmazonSesOptions.SenderEmail` or a `LeaseSafetyMargin` that is not less than `Lease` stops the start.

A host runs this validation when it starts. Without a host, the consumer calls `IStartupValidator.Validate()`.

### 4.8 Processing

Processing of one domain event:

1. `AcceptAsync` builds a `ReceivedMessage<TEvent>` with the message id, the tenant, and the flags of the `MessageContext` (D115), calls `AddAsync` of the inbox tenant store of `TenantKey.For(context.Tenant)`, and notifies the inbox topic of the dispatch signal. Later, the inbox processor claims it and runs steps 2 to 6. `ProcessAsync` builds the `ReceivedMessage<TEvent>` in memory and runs steps 2 to 6 directly. An unregistered event type gives `Errors.NotFound`. `AcceptAsync` in a host with no inbox store also gives `Errors.NotFound`.
2. **Route.** With `StoredRegistry`, the pipeline first waits for the end of the first read of the registry (D168). Only `ProcessAsync` and the inbox processor apply the registry settings of steps 2 and 3, and `PreviewAsync` ignores them (D169). The pipeline reads the descriptor of the message type from `IMessageRegistry` one time. If the message type is disabled, the pipeline stores nothing, logs it at the Information level, and returns a result with no stored message (D118). Otherwise it resolves `IMessageRouter<TEvent>` with the tenant key of the message, and calls `RouteAsync` one time (D95, D126). Each later step resolves its tenant services with the same key. Section 4.17 describes the routing procedure of the subscription router.
3. **Check.** The pipeline checks each route, and skips or fails only that route:
   - A route for a channel that `IMessageRegistry` gives as disabled, or that the message type has in `DisabledChannels`, is skipped. It gives no outgoing message and no error. `NotificationResult.Skipped` counts it, and the pipeline logs it at the Debug level (D118).
   - A route for a channel that the message type has not registered gives `Errors.NotFound`.
   - A route with settings of another type than the settings type of the channel gives `Errors.ValidationFailed`.
   - A second route for the same target and channel gives `Errors.Conflict`, because its outgoing message id is the id of the first route (step 5).
4. **Compose.** The pipeline splits the routes by channel, in the order of the router (D280). For each channel registration of `TEvent`, the pipeline calls `IEnvelopeComposer<TEvent, TEnvelope>.ComposeAsync` one time, with all routes of that channel (D96). When all routes are on one channel, no route failed, and the router gave an array, the composer gets that array with no copy. The library composers do these steps:
   - Each library composer first calls `IPayloadLoader<TEvent, TPayload>.LoadAsync` one time, with all routes of its channel. A loader error is the outer error of the composer.
   - The email composer groups the routes by language and template scope with `RouteGrouping.ByLanguageAndScope`. It calls `ITemplateEngine.PrepareAsync` one time for each group and part (`subject`, the html `body`, and the text `body`), and `ITemplate.RenderAsync` for each part of each route, with the culture of the language (D215). It takes `To` from the address, and fails a route with no address with `Errors.ValidationFailed`. The format `PlainText` sends only the text part.
   - The SignalR composer puts the payload into a `SignalREnvelope<TPayload>`. The transport takes the target from the outgoing message, and the method name is the message type name.
   - The broker composer puts the payload as it is, and the headers of the channel settings, into a `BrokerEnvelope<TPayload>`.
5. **Outgoing messages.** The pipeline builds an `OutgoingMessage<TEnvelope>` for each envelope:
   - `DeterministicId` derives the id from the message id, the tenant, the delivery target, and the channel id.
   - `Tenant` is the tenant of the context (D115). `Type` is the message type of the event, and `Target` is the target of the route (D113).
   - `MaxTryCount` and `ExpiresAtUtc` come from the retry options of the channel registration, which have the content type as their name (D87). `ExpiresAtUtc` is the `CreatedAtUtc` of the received message plus `Expiry`, or null when `Expiry` is null (D94, D142).
   - `ContentType` and `ContentVersion` come from the registration. `SchemaVersion` is 1.
   - `Trace` is the trace of the route (D98).
6. **Store.** The pipeline calls `AddAsync` of the outbox tenant store of the tenant key one time for the event. Then it notifies the topic of each channel of the stored messages through the dispatch signal (section 4.9), one time for each channel, in the order of the first stored message of the channel (D280). Each outgoing message has the flags of the received message.

A failure in step 3, an inner error in step 4, and a failure in step 5 fail only the affected messages. A failure in step 2 or 6, and an outer error in step 4, fail the event, and the inbox or the broker retries it.

`PreviewAsync` builds the `ReceivedMessage<TEvent>` in memory from the context, and adds `MessageFlags.Test` to its flags. With routes from the caller, it runs steps 3 to 5 with these routes. With null routes, it runs steps 2 to 5 with the routes of the router, and a disabled message type gives an empty list. It stores nothing, notifies no signal, and returns one `RouteResult` for each route (D84, D100).

Publishing of one claimed outgoing message:

1. If the channel of the message is down, the dispatcher does not send, and records no try. The lease ends by itself, and no loop claims the messages of the channel until the channel is up again (section 4.9).
2. If the message is expired, the dispatcher records a try with the outcome `Expired`, and does not send.
3. If the message has `MessageFlags.Test`, the dispatcher reads the `TestDeliveryOptions` of the channel (D83):
   - `Suppress`, the default: it records a try with the outcome `Suppressed`, and does not send.
   - `Redirect`: it sends with `RedirectTo` as the `redirectTo` argument. Each transport applies it to its own envelope: the email transport replaces `To` and removes `Cc` and `Bcc`, the SignalR transport sends to the SignalR group `RedirectTo`, and the broker transport adds the header `msghatch-redirect`.
   - `Deliver`: it sends as for a normal message. A broker message gets the header `msghatch-test: true`, so that a receiver can see the test flag.
4. The dispatcher finds the channel registration of the content type of the message, and upgrades an old envelope version with its registered function (section 4.13). It checks the version before the CLR type (D25). A version with no upgrade, for example a version that is newer than the code, gives a try with the transient `Errors.UnsupportedVersion`, and no send. A content type with no registration on the host is sent as it is stored. Then the dispatcher reads the start time from `TimeProvider` and calls `SendAsync` of the `ITransport` of the channel (D129).
5. It reads the finish time from `TimeProvider` and builds a `PublishTry` with the next try number:
   - A receipt gives `Succeeded` with the provider message id.
   - A transient error gives `TransientError`. If the try number is less than `MaxTryCount`, the dispatcher also calculates the next try time with the retry options of the channel registration of the message (section 4.14, D87).
   - A permanent error gives `PermanentError`.
6. It calls `IOutboxDispatchStore.RecordTryAsync`. If this call fails, it repeats the call one time. If the call fails again, the lease ends, and a later claim sends the message again. After a recorded try with a next try time, the worker gives that time to the claim loop of its channel (D103).
7. It updates the error count of the channel with the outcome of the send (section 4.9). A try with no send, for example `Expired`, `Suppressed`, or `Errors.UnsupportedVersion`, does not change the count, because D86 counts only the errors of the transport. The try of a transport or an upgrade function that throws does not change the count either, because a throw is a fault of the code, not an outage (D137).

A cancelled send records no try. The lease of that message ends by itself. A transport or an upgrade function that throws gives `Errors.Exception` at the boundary of the dispatcher (section 4.12). The dispatcher logs the exception, and records a `TransientError` try with `Errors.Exception` and the next try time of step 5. So a message that always throws reaches `Failed` after `MaxTryCount` tries (D137). A store that throws gets no try, because the dispatcher cannot record it. The dispatcher logs the exception, and a later claim gets the message again when its lease ends.

Processing of one claimed received message: the inbox processor makes one DI scope for the message, upgrades an old version (section 4.13), and checks the version before the CLR type (D25). A version with no upgrade, for example a newer version of the current event type, gives the transient `Errors.UnsupportedVersion`, and the pipeline does not run. Then it runs steps 2 to 6 with the tenant key of the message, and then calls `CompleteAsync` of the inbox dispatch store. A failure calls `FailAsync` with the next try time, or with no next try time for a permanent error or the last try. After a `FailAsync` with a next try time, the worker gives that time to the claim loop (D103). The inbox has no expiry of its own, so the inbox processor does not read `ExpiresAtUtc`. The time in the inbox counts against the expiry of each outgoing message (D142).

`DeterministicId.Create(Guid messageId, TenantId? tenant, DeliveryTarget target, ChannelId channel)` is a UUID version 5:
1. The namespace is the fixed id `77b46874-bae1-4fa7-a7d9-baec996f4fc6`. This value never changes.
2. The name is the 16 bytes of `messageId` in big-endian order. With a tenant, the byte `t`, the UTF-8 bytes of the tenant id, and one `0x00` byte follow. With no tenant, nothing follows, so a setup with no tenant keeps the same ids. Then come one byte for the target kind (`u` for a user, `c` for a company, `a` for a tenant, D315), then the UTF-8 bytes of the target id, then one `0x00` byte, then the UTF-8 bytes of the channel name. A `stackalloc` buffer holds it, or a pooled array for a long input.
3. The hash is `SHA1.HashData` of the namespace bytes in big-endian order, followed by the name.
4. The id is the first 16 bytes of the hash. Byte 6 is `(b & 0x0F) | 0x50`, and byte 8 is `(b & 0x3F) | 0x80`. `new Guid(bytes, bigEndian: true)` creates it.

### 4.9 Dispatch loop

**Confirmed rule (D8, D41, D101):** the library owns the dispatch loops. The consumer starts `RunAsync` one time for each dispatcher, for example from its own `BackgroundService`:
- The inbox processor runs one dispatch loop for the inbox.
- The outbox dispatcher runs one dispatch loop for each published channel: each channel whose transport the host registered.
- Each loop has its own claim loop, work queue, and send workers, so a slow or down channel does not delay another channel.
- Each loop uses the `DispatchOptions` of its dispatcher, `Inbox` or `Outbox`. The dispatcher has the comment `ponytail: one DispatchOptions for all channel loops, add options for each channel if one process hosts channels with different needs`.

A dispatch loop has three parts, and an outbox loop has a fourth part, the down state of its channel:

1. **The claim loop.** It writes claimed messages into the work queue, a bounded `Channel<T>`. In each round, it does these steps:
   1. It calculates the free capacity: `QueueCapacity` minus `Reader.Count`. If the free capacity is 0, it waits with `Writer.WaitToWriteAsync` until a worker takes a message.
   2. It calls `ClaimDueAsync` with the free capacity as `maxCount` and `Lease` as the lease. An outbox loop also gives its channel. So it never claims more messages than the workers can take.
   3. It writes each claimed message into the work queue.
   4. If the claim filled the free capacity, it starts the next round at once.
   5. If the claim returned fewer messages, it waits until the first of these events (D103):
      - A signal for the topic of the loop (D102).
      - The next due time of the claim result, or an earlier time that a worker gave to it: the next try time of a recorded try or of a failed received message, or the lease end of a skipped message.
      - For a down channel with the reason `Errors`, the `NextCheckAtUtc` of its down state.
      - The poll delay, only when `EnablePolling` is true. The poll delay starts at `PollInterval` and doubles after each empty claim, to a maximum of `MaxPollInterval`. A signal or a claim with messages sets it back to `PollInterval`.
   6. If the claim returned an `Error`, it waits for the poll delay, also when `EnablePolling` is false, and then continues.
2. **The send workers.** `MaxDegreeOfParallelism` workers read with `await foreach (var message in channel.Reader.ReadAllAsync(ct))`. A worker waits asynchronously for the next message, with no poll.
   - Before a send, a worker skips a message whose lease ends within `LeaseSafetyMargin`. That message becomes due again when its lease ends. The worker gives that lease end to the claim loop, which also waits for that time. Without this check, another instance can claim the same message while it waits in the work queue, and both instances send it.
   - For each other message, the worker runs the same function as `DispatchPendingAsync` (section 4.8).
   - After a try with a next try time, the worker gives that time to the claim loop too. So a retry needs no poll and no signal, also with `EnablePolling` false (D103).
3. **The dispatch signal (D102).** `IDispatchSignal` wakes a waiting claim loop:
   - The topic of the inbox loop is `DispatchTopic.Inbox`. The topic of an outbox loop is `DispatchTopic.ForChannel(channel)`.
   - The pipeline notifies the topic of each channel of the stored messages after each successful `AddAsync`. `AcceptAsync` notifies the inbox topic.
   - A signal is a hint. A lost signal only delays a claim until the next due time or the next poll. Many signals before one wait cause only one wake-up.
   - A notify error does not fail the event, because the messages are already stored. The pipeline logs it at the Warning level.
   - A `WaitAsync` error other than `Errors.OperationCancelled` means that the signal does not work. The loop then waits until the earliest of its times of step 5 of part 1, or for `PollInterval` when it has no such time. A time that a worker gives during this wait ends the wait, as during a wait for the signal (D103).
   - The default `InProcessDispatchSignal` holds one `Channel<bool>` with capacity 1 and `BoundedChannelFullMode.DropWrite` for each topic. It reaches only the loops of the same process.
   - `UseDispatchSignal<TSignal>()` selects another implementation. `UseNamedPipeDispatchSignal` selects the named pipe signal between the processes of one machine (D188). `UseMongoDbDispatchSignal` selects the change stream signal of the hosts that use one database of the MongoDB stores, also on different machines (D303). `UseRabbitMqDispatchSignal` selects the signal through a fanout exchange of RabbitMQ, for the hosts of one system on different machines with each store (D305).
   - The named pipe signal of `MsgHatch.Dispatch.NamedPipes` (D188, D191, D192):
     - Each topic has one pipe: `{PipeNamePrefix}.inbox`, and `{PipeNamePrefix}.outbox.{Channel}` for a channel. Both ends use `PipeOptions.CurrentUserOnly`.
     - `NotifyAsync` notifies the loops of its own process at once through an `InProcessDispatchSignal`, and returns at once. When the process does not serve the pipe of the topic, a writer of that pipe also writes one byte into it in the background. Its queue has capacity 1, so many notifications before one write become one byte.
     - The waiting process starts to serve the pipe of a topic at its first `WaitAsync` of that topic, with no `PipeOptions.FirstPipeInstance`. Each byte that arrives notifies the loops of the topic in the process.
     - A writer keeps one connection, which starts with the id of its signal instance. A server closes a connection of its own signal instance, so a process never wakes itself through its own pipe. A notification that finds no server is lost. A write that fails connects one more time and writes again, so the first notification after a restart of the waiting process reaches it. After 3 failed connects in a row, the writer logs a Warning one time with the topic, and at its next connect an Information. So a waiting process of another OS user (D191), or with another prefix, shows in the log of the notifier.
     - When the pipe of a topic cannot be served, `WaitAsync` returns `Errors.Unavailable` at once, and the loop uses its fallback wait. The signal logs a Warning one time for each outage. The next `WaitAsync` tries to serve the pipe again.
   - The change stream signal `MongoDbDispatchSignal` of `MsgHatch.Data.MongoDb` (D303, D304):
     - One change stream on the database of the MongoDB stores sees the inserts into `MsgHatchInbox`, `MsgHatchOutbox`, and `msghatch_signals`. An insert into the inbox notifies `DispatchTopic.Inbox`, an insert into the outbox notifies the topic of its field `channel`, and a document of `msghatch_signals` notifies the topic of its field `topic`. Each notification goes to a local `InProcessDispatchSignal`, and `WaitAsync` waits on it. The watch opens at the first `WaitAsync`.
     - `NotifyAsync` notifies the loops of its own process at once, and returns at once. A writer in the background then inserts `{ topic, sender, createdAtUtc }` into `msghatch_signals`, which has a TTL index of one minute on `createdAtUtc`. Many notifications before one insert become one document for each topic. The watch skips the documents of its own signal instance by `sender`, so a process does not wake itself a second time. While the last insert failed, `NotifyAsync` returns `Errors.Unavailable`, and the pipeline logs it at the Warning level.
     - An insert of a message needs no notify, because the insert itself is the signal. The pipeline still calls `NotifyAsync`, and the extra document costs one small write.
     - When the watch does not open or ends, each waiting `WaitAsync` and each next one return `Errors.Unavailable`, and the loops use their fallback wait. The signal logs one Warning for each outage, event 2800, and opens the watch again after a pause of 5 seconds. At the next open, it logs an Information, event 2801.
     - The signal keeps no resume token. A change between the end of a watch and the next open reaches a loop only through its poll, so `UseMongoDbDispatchSignal` requires `EnablePolling` true (D190).
     - A change stream needs a replica set. On a server with no replica set, the open of the watch gives the server error 40573, so each `WaitAsync` returns `Errors.Unavailable`, and the loops poll.
   - The RabbitMQ signal `RabbitMqDispatchSignal` of `MsgHatch.Dispatch.RabbitMq` (D305):
     - The signal opens one connection at its first `WaitAsync` or `NotifyAsync`, with no automatic recovery of the client. It declares the fanout exchange `ExchangeName`, not durable and auto-delete, and one exclusive, auto-delete queue with a name from the server, bound to the exchange. The broker deletes the queue when the connection closes or is lost.
     - `NotifyAsync` notifies the loops of its own process at once through an `InProcessDispatchSignal`, and returns at once. The connection then publishes the topic name in the background, as a transient message with the id of the signal instance as `AppId`. Many notifications before one publish become one message for each topic. The topics that the process notifies before a connect are published after the connect.
     - The consumer of the queue acknowledges each message automatically, and skips the messages of its own signal instance by `AppId`. A body `inbox`, or `outbox:{Channel}` with a valid channel name, notifies that topic on the local signal. Each other body changes nothing.
     - When a connect fails or the connection closes, each waiting `WaitAsync` returns `Errors.Unavailable`, and the loops use their fallback wait. The signal connects again after a pause of 5 seconds. In the pause, `WaitAsync` and `NotifyAsync` return `Errors.Unavailable` at once. The signal logs one Warning for each outage, event 3200, and at the next connect an Information, event 3201.
     - A message that the broker cannot route is discarded, and a process gets no message while it has no queue, for example in its pause. So `UseRabbitMqDispatchSignal` requires `EnablePolling` true (D190).
4. **The down state of a channel (D86, D106).** The outbox dispatch store keeps one down state for each channel, for all publisher instances of the channel. Each outbox loop keeps only the error count of its channel in its process:
   - A `TransientError` try adds 1 to the count. A `Succeeded` or `PermanentError` try sets the count to 0, because the provider answered. An `Expired` or `Suppressed` try and a `TransientError` try with `Errors.UnsupportedVersion` send nothing, so they do not change the count. The `TransientError` try of a transport or an upgrade function that throws does not change the count either, because a throw is a fault of the code, not an outage (D137). A cancelled send records no try and does not change it.
   - When the count reaches `ErrorThreshold` of the `ChannelDownOptions` of the channel, the loop calls `SetChannelDownAsync` with the reason `Errors`, the error of the last try, and a next check time of `CheckInterval` later. The loop whose call wrote the state logs it at the Error level, with the channel and the error code. The other loops change nothing, because the first loop wins.
   - A claim of a down channel claims nothing, and returns the down state. A worker does not send a message that it claimed before the channel went down, and records no try for it. Its lease ends by itself.
   - While the reason is `Errors`, the loop waits until `NextCheckAtUtc`, and then calls `TryStartChannelCheckAsync` with a next check time of `CheckInterval` later. Only the loop that gets true calls `CheckHealthAsync` of its transport, so one publisher instance at a time checks the channel.
   - A check with no error calls `SetChannelUpAsync` with the reason `Errors`. The loop logs the end at the Information level, with the down time. A check with an error changes nothing more. The next check comes at the moved `NextCheckAtUtc`.
   - While the reason is `Operator`, no loop checks the channel. Only a person ends this state, through `IDeliveryCommands.ResumeChannelAsync` (section 4.19). A resume also notifies the topic of the channel, so that a waiting loop claims again at once.
   - When a loop finds its channel up again after a down state, its count starts at `ErrorThreshold` minus 1, so the first transient error marks the channel down again.
   - A message of a down channel keeps its try count, because no loop records a try for it. The expiry still applies: after the channel is up, an expired message gets an `Expired` try, and is not sent.
   - A transport with nothing to check, for example SignalR or the Wolverine broker client, returns null from `CheckHealthAsync`. Its down state then ends after one `CheckInterval`, so a long outage of such a channel uses at most one try for each `CheckInterval`.
   - The down state is in the store, so each process and each run sees it, also a host that makes a new dispatcher for each run, for example a Lambda function. The dispatcher has the comment `ponytail: fixed check interval, a growing interval if long outages need it`.

Hosting (D101, D102). The same registration code serves each of these layouts:
- **One process.** One host runs the event consumer and all publishers, for example with one `BackgroundService` that starts `IInboxProcessor.RunAsync` and `IOutboxDispatcher.RunAsync`. `MsgHatchOptions.Roles` keeps its default, both roles. The in-process signal wakes each loop. With `EnablePolling` false, no loop polls a store: each loop waits only for a signal and for the next due time, which includes the next try times that its workers give to it.
- **Separate processes.** The event consumer app has the role `Consumer`, registers no transport, and runs the inbox processor, or calls `ProcessAsync` from a broker. Each publisher app has the role `Publisher`, registers the transport of its channel, for example with `UseAmazonSes`, and runs `IOutboxDispatcher.RunAsync`. All apps call the same registration code of the message types, and use the same storage. A publisher then finds new messages by polling and by the next due time, or by an `IDispatchSignal` between processes.
- **The choice of the signal (D102, D188).** One process uses the default `InProcessDispatchSignal`, and `EnablePolling` can be false. With separate processes of one OS user on one machine, each process calls `UseNamedPipeDispatchSignal` with the same prefix. `EnablePolling` then stays true (D190), and `MaxPollInterval` can be longer, because the signal wakes the loops. With separate machines or separate OS users and the MongoDB stores on a replica set, each host calls `UseMongoDbDispatchSignal` (D303). With other stores and a RabbitMQ broker, each host calls `UseRabbitMqDispatchSignal` with the same broker and the same exchange (D305). With no such broker, a host there calls no signal method, and the loops find new messages by the poll and by the next due time.
- A stopped publisher stops only its channel. The messages of the channel wait in the outbox. They are sent after the restart, or they get an `Expired` try.
- Each process must reach the same stores. SQLite works only for processes on one machine, and the file stores need a folder with atomic rename (section 6).

`DispatchPendingAsync` claims one batch of at most `batchSize` messages for each published channel, and `ProcessPendingAsync` claims one batch for the inbox. Each one processes its batches with `Parallel.ForAsync` over the positions of the batch and `MaxDegreeOfParallelism` (D281), and returns one `DispatchSummary`. They use the same function for each message as `RunAsync`. Before each claim, `DispatchPendingAsync` reads the down states of the channels from the store, and runs a due check of a down channel first.

Shutdown: when the token is cancelled, each claim loop stops and completes the writer of its work queue. Each worker finishes or cancels its current send, and then stops. A cancelled send records no try. The messages that are still in a work queue are not sent. They become due again when their lease ends.

The validation of `DispatchOptions` at startup (section 4.4) requires `LeaseSafetyMargin` to be less than `Lease`, and `QueueCapacity` to be at least `MaxDegreeOfParallelism`.

### 4.10 Email templates and engines

**Confirmed rule (D44, D213):** an email registration renders its emails through `ITemplateEngine<TPayload>`, which another implementation can replace. A template is the text of one part (D214). The engine has two steps, so that the email composer can find and prepare each part one time for each group of routes:
1. `PrepareAsync` finds and prepares the template of one part for one message type, language, and scope. It returns `Errors.NotFound` when no template exists. An engine caches the parsed objects that it needs.
2. `ITemplate.RenderAsync` renders the prepared part for one payload and culture, and gives the output string. A placeholder template completes synchronously, so this call does not allocate a task.

A third method checks a text before a write: `CheckAsync(TemplateLookup, string, CancellationToken)` checks the text of one part as a prepare after a store read checks it, with no store read and no cache entry. It gives one `TemplateIssue(Message, Line, Column)` for each problem that it finds, an empty list for a valid text, or an error when the check did not run (D219, D241). The placeholder engine gives the error of its parser as one issue with no position.

A fourth method renders a text that is not stored, for example the draft of an editor: `RenderAsync(TemplateLookup, string, TPayload, CancellationToken)` gives `ErrorOr<string>` (D255). It parses the text as a check does, with no store read and no cache entry, and renders it with the code of `ITemplate.RenderAsync`: in the culture of the language of the lookup, an html part with the HTML encoder, and a text part with no encoder (D210). A text that does not parse gives `Errors.TemplateInvalid` with the message of the first issue of the check. A language that is not the name of a culture gives a `CultureNotFoundException`, because the template queries check the language before they call an engine (section 4.22). The logging wrapper of an engine logs each render with the events 2031 and 2032, and never logs the text, the payload, or the output.

Sample payloads (D254). `WithTemplateSample(sample)` on the payload builder stores one sample payload for the template registrations of a payload type and a message type, for example `p.AsEmail("FEmail", LiquidTemplates.Engine).WithTemplateSample(new PasswordResetEmail("https://example.com/reset"))`. A second sample of one payload type and one message type gives a setup error (D62). The template queries render a preview with the sample (D256), so a sample holds no real data.

The email layer (D215) asks for the parts `subject` (`PlainText`, required), `body` (`Html`, required), and `body` (`PlainText`, optional). A missing text part gives no text body. Each part falls back on its own (D214), so a company can change only its subject, and a part with no translation takes the default language. `AsEmail(name)` selects the placeholder engine, and `AsEmail(name, ITemplateEngineFactory)` another engine (D216).

The first engine is `PlaceholderTemplateEngine<TPayload> where TPayload : IPlaceholderPayload<TPayload>` (D45):
- **Template store.** `ITemplateStore.FindAsync(TemplateLookup lookup, CancellationToken ct)` returns `ValueTask<ErrorOr<TemplateSource?>>`. A null value means that no template matches. This is not an error.
- **Fallback (D123).** The template selector `TemplateSelector`, which is public so that each engine uses it (D205), tries these lookups in this order, and the first hit wins:
  1. The template store of the tenant, with the language and the scope.
  2. The template store of the tenant, with the language and no scope.
  3. The shared template store, with the language and the scope.
  4. The shared template store, with the language and no scope.
  5. to 8. The same four lookups with the default language.
  The selector gets the template store of the tenant with its own key. It skips the lookups in that store for the key `TenantKey.None`, or when no tenant template store is registered. It skips a lookup with a scope when the request has no scope. So a setup with no tenant tries the shared store with the scope and then with no scope, and the company templates of D80 apply.
- **Parser.** A placeholder is `{{PropertyName}}`. The name contains only letters, digits, and `_`. A `}}` outside a placeholder is literal text. An unclosed `{{`, or a name with other characters, gives `Errors.TemplateInvalid`. The parser makes an array of literal and placeholder segments.
- **Binding.** `ParsedTemplateCache<TPayload>` parses the text of a part one time for each `TemplateKey` and part kind, and binds each placeholder to a property index with `IPlaceholderWriter.IndexOf`. An unknown name gives `Errors.TemplateInvalid` (D5). The cache is a static `ConcurrentDictionary<(TemplateKey, TemplatePartKind), PlaceholderTemplate<TPayload>>` for each payload type. It is marked with `ponytail: unbounded cache, add a size limit if a store produces many versions`. `CheckAsync` parses and binds a text with the same parser and no cache entry, so a syntax error and an unknown name give `Errors.TemplateInvalid` before a write (D219).
- **Renderer.** `PlaceholderRenderer` writes each segment into an internal `PooledCharBufferWriter` on `ArrayPool<char>.Shared`. In an `Html` part, it encodes each value with `HtmlEncoder.Create(UnicodeRanges.All)` through the `OperationStatus` API. This encoder encodes the HTML characters, for example `<`, `>`, and `&`, but keeps accented letters, for example `Ö` and `Ű`. `HtmlEncoder.Default` writes them as character references, for example `&#xD6;`. In a `PlainText` part, it does not encode.

Template types (`MsgHatch.Templates`):

```csharp
public readonly record struct TemplateKey(string Id, long Version);
public readonly record struct TemplateLookup(MessageType Type, string Language, string? Scope, string Part, TemplatePartKind Kind);
public enum TemplatePartKind { PlainText, Html }
public sealed record TemplateSource(TemplateKey Key, string Text);
public sealed record TemplateEntry(TemplateLookup Lookup, long Version);
public sealed record TemplateQuery(MessageType? Type = null, string? Scope = null);
```

A lookup and a source are one part (D214). The kind gives the file extension: `.txt` for `PlainText` and `.html` for `Html`.

Template stores:
- `FileSystemTemplateStore(string rootPath)` reads the file `{MessageType}.{Language}.{Part}.txt` or `.html` of a lookup (D214), for example `OrderShipped.en.subject.txt`, `OrderShipped.en.body.html`, and `OrderShipped.en.body.txt`. The template of a scope is in the folder of the scope, for example `acme/OrderShipped.en.subject.txt` (D199). The language and the part are escaped with `Uri.EscapeDataString` in the file names. The template id is the full path of the file, so two folders never share an id. The version is the last write time of the file, in ticks.
- `ScopeFolders.FolderOf(scope)` gives the folder of a scope in the file store: the scope escaped with `Uri.EscapeDataString`, each `.` as `%2E`, and each capital letter as `^` and the small letter (D201). For example, the folder of `acme-1` is `acme-1`, and the folder of `ACME` is `^a^c^m^e`. An empty scope has no folder. So two scopes never share a folder, and a scope cannot lead out of the template folder. `ScopeFolders.SegmentOf(scope)` gives the folder as a part of a resource name: only a scope of ASCII letters, digits, and `_`, which does not start with `_`, has one, with `_` before a name that starts with a digit, for example `_42`, as MSBuild makes it. Each other scope has no segment, and its lookup is a miss, so the selector uses the template with no scope. A folder of embedded templates whose name breaks this rule is an error of the host: MSBuild changes its name, for example `acme-1` to `acme_1`, so the embedded store reads it for the scope `acme_1`.
- A host can use `FileSystemTemplateStore` as the template store of each tenant, with the factory overload of `UseTenantTemplateStore`, for example on the folder `tenants/{tenantId}`, with the tenant id escaped by `Uri.EscapeDataString`.
- `ITemplateStore.RenderVersion(long version)` gives the text of a version for a user (D267). Each other part treats a version as an opaque number. The default text is the number, the file store gives the last write time in UTC, for example `2030-01-02 15:04:05 UTC`, and the embedded store gives `Built in`.
- `EmbeddedResourceTemplateStore(Assembly assembly, string prefix)` reads the resources `{prefix}.{file name}`, with the same file names, and the template of a scope from `{prefix}.{segment}.{file name}`, for example `{prefix}._42.OrderShipped.en.subject.txt`. These are the names that MSBuild gives to the files of a folder and its scope folders, with no `LogicalName`. The template id is `{assembly name}:{resource name}`. The version is 0, because an embedded resource does not change while the process runs. It is a shared template store only, because the resources are the same for all tenants.
- Each library template store caches its sources. The embedded store keeps its sources and its misses for the life of the process. On each lookup, the file store reads only the last write time of the file, and reads the file again only when the time changed. The file store does not cache a miss, because a missing file has no read to skip. Thus its cache does not grow with the lookups of scopes that have no folder.

Template writer (D218, D223). `ITemplateWriter` lists, saves, and deletes the templates of one store: `ListAsync(TemplateQuery)`, `SaveAsync(TemplateLookup, text, expectedVersion)`, and `DeleteAsync(TemplateLookup)`. A store is writable when it implements `ITemplateStore` and `ITemplateWriter`. For a writable store, the logging wrapper is a writer too (section 4.15), so a cast of the registered store finds the writer, with and without logging. `FileSystemTemplateStore` is writable, as the shared store and as the store of each tenant. `EmbeddedResourceTemplateStore` is not, because the build fixes its resources. The file store writes in this way:
- A save writes the file of the lookup, in the folder of `ScopeFolders.FolderOf` (D201), so no request leaves the template folder. An empty scope has no folder, and gives `Errors.ValidationFailed`. The store creates a missing company folder.
- It writes the text into a temporary file `.{guid}.tmp` in the same folder, and moves that file over the file of the lookup, so a reader never reads half a text. It deletes the temporary file also after an error.
- The version of a save is the last write time of the file in ticks. When that time is not later than the version before the save, the store sets it one tick after that version. So each save gives a new `TemplateKey`, and the parse caches of the engines parse the new text.
- `expectedVersion` is the version that the caller read, or 0 for a new file. Another stored version gives `Errors.Conflict` (D79). One lock for each store makes the check of the version and the move atomic in one process. It has the comment `ponytail: one write lock for each store, and two processes on one folder can race between the check and the move; a lock file if two operators write one part at the same time`.
- On Windows, a move over a file that a reader holds open gives `UnauthorizedAccessException`, so the save gives `Errors.Unavailable` and keeps the old text (checked facts of phase 25). The store reads a file only when its time changed, so a reader holds it open only for a short time.
- A delete removes the file. A missing file is not an error.
- A list reads the root folder and each company folder, or only the folder of `query.Scope`, and gives the entries in the order of scope, language, part, and kind. `ScopeFolders.ScopeOf(folder)` gives the scope of a folder name, the reverse of `FolderOf`. The store skips each folder and each file whose name the library does not make from the parts of a lookup, for example the temporary file of a save, a folder `Acme` with a capital letter, or a name with an empty language.

Placeholder payloads (`MsgHatch.Templates.Placeholders`):

```csharp
[AttributeUsage(AttributeTargets.Class)]
public sealed class PlaceholderPayloadAttribute : Attribute;

public interface IPlaceholderPayload<TSelf> where TSelf : IPlaceholderPayload<TSelf>
{
    static abstract IPlaceholderWriter<TSelf> Writer { get; }
}

public interface IPlaceholderWriter<TPayload>
{
    int IndexOf(ReadOnlySpan<char> propertyName);
    bool TryWrite(in TPayload payload, int propertyIndex, IBufferWriter<char> output, IFormatProvider culture);
}
```

`IndexOf` returns -1 for an unknown property name.

The source generator (`MsgHatch.SourceGeneration`, D18) is an incremental generator. It finds each type with `[PlaceholderPayload]` through `ForAttributeWithMetadataName`. For a `partial` class or record, it adds `IPlaceholderPayload<TSelf>` and a nested private writer class:
- `IndexOf` is a `switch` on the `ReadOnlySpan<char>` name with ordinal comparison.
- A `string` property is copied into the output.
- An `ISpanFormattable` property, for example `int`, `decimal`, `DateTime`, `Guid`, or an enum, is written with `TryFormat` and the given culture, with no boxing. If the span is too short, the writer asks for a span of double the size.
- A null value of a nullable property writes nothing.
- Any other property type uses `ToString()`. This is marked with `ponytail: ToString allocates, add a writer interface for custom types if a benchmark shows the need`.
- A payload type that is not `partial` gets the diagnostic `MSGHATCH001` with the error severity.

**The Liquid engine (D45, D203 to D211, D216, D226).** A host must add an email template at run time, with no build and no deployment: an operator copies a file into the template store. `src/MsgHatch.Templates.Liquid` does this with `Fluid.Core` 2.41.1, and `p.AsEmail(name, LiquidTemplates.Engine)` selects it for one payload type (section 4.7). It knows no channel. Liquid gives loops, conditions, and filters.
- **Templates.** The engine reads the one part that a `TemplateRequest` names, with its kind, from `ITemplateStore` through `TemplateSelector`, with the fallback of D123 and the company folders of D199 (D214). The email composer asks for `subject` (`PlainText`), `body` (`Html`), and the optional `body` (`PlainText`) in the language and scope of the html body (D215). The engine parses each `TemplateKey` one time for the life of the process. A parse error gives `Errors.TemplateInvalid` at prepare, and a missing part gives `Errors.NotFound`.
- **Model (D209).** The payload is the model, so `{{ Name }}` reads the property `Name`. Member names are case-sensitive. The engine registers, one time for each payload type, each class, record, or struct that the public properties of the payload reach, also as the element type of a collection. It does not register a type of the .NET runtime, so a template cannot read the members of, for example, a `TimeZoneInfo`.
- **Render (D210).** A part of the kind `Html` encodes each value with `HtmlEncoder.Create(UnicodeRanges.All)`, and the filter `raw` turns this off. A part of the kind `PlainText` encodes nothing. A render has the culture of the route, the time zone UTC, the `now` of the `TimeProvider`, and a limit of 100,000 steps for each part, because each part is a template of its own (D214).
- **Errors.** An unknown name gives `Errors.TemplateInvalid` at render (D207). This applies also to a member of an object in a loop. A property with a null value is not unknown. The step limit and a `render` or `include` tag also give `Errors.TemplateInvalid` (D208, D210). An exception of the payload code reaches the caller, and the boundary of section 4.12 changes it into `Errors.Exception`. An unknown filter, for example `{{ Name | upcse }}`, also gives `Errors.TemplateInvalid` at render, through the option `StrictFilters` of Fluid (D211). Filter names are case-sensitive. As for an unknown name, a filter in a branch that does not run gives no error.
- **Check before a write (D219).** `CheckAsync` parses the text with no cache entry, and walks the parsed tree with `Fluid.Ast.AstVisitor`. An unknown filter and a `render` or `include` tag give an issue, also in a branch that does not run. An unknown name, and the step limit, give the error only at render, because a check has no payload. A `{% liquid %}` tag gives an issue at the check and `Errors.TemplateInvalid` at prepare, because Fluid 2.41.1 drops the text after it with no parse error (D226). A parse error gives an issue with the line and the column of Fluid, and an unknown filter, a `render` tag, an `include` tag, and a `{% liquid %}` tag give an issue with no position (D241, D243). The check gives one issue for the first problem that it finds.
- **Razor does not fit.** The Razor SDK compiles a component into the assembly at build time, and no supported library compiles a component at run time. A component is also C# code. Phase 22 built a Razor engine, and the owner removed it (D204).

### 4.11 Allocation budget

The target is zero allocation in the MsgHatch hot path. Some allocations stay, because they are outside MsgHatch or because they are the result itself:

| Scope | Allocates | Does not allocate |
|---|---|---|
| Each event | The received message, the route list, one result array, the payload lists, the composer results, and the outgoing message list. At most one copy of the routes with their positions, when the routes are on more than one channel, a route failed, or the router gave no array. A set of the targets, when the event has more than one route. A list of the channels after the first, when the routes are on more than one channel (D280). | Registry lookups, ids, `ErrorOr` results, closures, the boxes of the tenant keys, and the dispatch topics |
| Each message | The payload (from the consumer's loader), the rendered email strings, the channel envelope, the outgoing message | Template parsing on a cache hit, writing a value-type property, HTML encoding, `DeterministicId` |
| Each send | The publish try, the objects that the store deserializes, and the request objects of the AWS SDK or the broker library | The dispatcher loop, the tags of the outcome (D281), and the next position of a batch of `DispatchPendingAsync` (D281) |
| Each dispatch loop | One enumerator for each worker, one timeout for each wait of the claim loop, and one read operation of about 80 B for each wait of a worker on an empty work queue, because the stop token can be cancelled (D281) | A message that a worker reads from a work queue that is not empty |
| Each wrapper call | A log entry, only when its level is enabled | The call itself. The wrappers use `PoolingAsyncValueTaskMethodBuilder`, and the source-generated log methods do not box. |
| Each cached subscription lookup | The string cache key, the result list, and the work of the cache backend. The list of the missing owners, only on a miss. | The store call that a cache hit replaces, an owner part string, and an enumerator of the owners (D282) |
| Each resolution of subscriptions | For each target and channel: the route with its trace or the opt-out decision, the skipped subscriptions, and the scratch of the pair and its index entry. For each run: the indexes of the channels, the users, and the companies, and the subscriptions in level order (D282). | An enumerator, a list of the matching subscriptions, and a list for a pair with one candidate |
| Each span and measurement | The activity and its tags, only when a listener samples the span | A span with no listener, and a metric measurement with up to 8 tags in a `TagList` (section 4.21) |

The library does not serialize in the pipeline. Each store serializes one time, with its own serializer.

Unit tests prove the "does not allocate" claims for code with no dependencies with `GC.GetAllocatedBytesForCurrentThread()`. This covers `ErrorOr`, the generated writers, `DeterministicId`, and the bytes of each further user of a resolution of subscriptions (D282). NSubstitute allocates when it records a call, so code with a substitute in its path cannot get an allocation test. In the Debug configuration of the unit tests, the compiler makes each async state machine a class, so an async method cannot get an allocation test either. The benchmarks measure the template cache, rendering, the dispatcher loop, and the full budget with BenchmarkDotNet.

Measured budget (phase 34). The three projects `tests/*.Tests.Performance` ran at the last commit of phase 33 (`adf32d1`, column "Phase 33") and after the changes of phase 34 (`e43e7ec`, column "Phase 34"), on 2026-11-18 and 2026-11-19:
- BenchmarkDotNet 0.15.8 with `MemoryDiagnoser`, the default job, and for the email engine a fixed job of 1,024 invocations, 5 warmups, and 20 iterations. The fixed job bounds the template versions of the run with no cache hit, because the cache keeps each version. Its times have a larger error.
- .NET 10.0.15 (SDK 10.0.404), Release, X64 RyuJIT x86-64-v4, on an AMD Ryzen AI 7 350 with 8 physical and 16 logical cores, Windows 11 build 26300.
- `Allocated` counts the bytes of all threads, because BenchmarkDotNet 0.15.8 reads `GC.GetTotalAllocatedBytes`. So the result of `RunAsync` includes its send workers. The bytes come from the total bytes and the operations of each run, so they are exact to 1 B.
- Each router, composer, loader, store, transport, and cache backend of the benchmarks gives a result that it made before the measurement, so it allocates nothing.
- The bytes "for each further route" and "for each further user" are the difference between the runs with 100 and with 1 route or user, divided by 99.
- The email benchmarks use the contract of D214: a template is one part, so `RenderAsync` renders one part, and the composer prepares three parts for each group of routes. Their numbers replace those of phase 13, which are from before D214.

| Benchmark | Phase 33 | Phase 34 | Mean, phase 34 (phase 33) | Budget line | Result |
|---|---|---|---|---|---|
| `ProcessAsync`, 1 route, in-memory stores | 3,472 B | 688 B | 1.08 µs (1.53 µs) | Each event and each message | Holds. Target: at most 1,000 B |
| `ProcessAsync`, 100 routes | 80,234 B | 30,793 B | 27.7 µs (35.1 µs) | Each event and each message | Holds: about 304 B for each further route (775 B). Target: at most 330 B |
| `ProcessAsync`, 10,000 routes | 7,779,007 B | 3,004,730 B | 4.84 ms (8.01 ms) | Each event and each message | Holds: about 300 B for each further route (778 B) |
| `DispatchPendingAsync`, batch of 100 messages | 254 B for each message | 108 B for each message | 165 ns (613 ns) for each message | Each send: the publish try | Holds. Target: at most 110 B |
| `RunAsync`, batch of 100 messages that a signal wakes | 261 B for each message | 212 B for each message | 840 ns (867 ns) for each message | Each send, and each wait of a worker (D281) | Does not hold. Target: at most 190 B (see the open items) |
| A call through a logging wrapper with Debug off | 0 B | 0 B | 48 ns (48 ns), 0.03 ns with no wrapper | The call itself does not allocate | Holds |
| `DeterministicId.Create` | 0 B | 0 B | 259 ns (257 ns) | `DeterministicId` does not allocate | Holds |
| Placeholder engine, `PrepareAsync` with a hit of the template cache | 0 B | 0 B | 623 ns (618 ns) | Template parsing on a cache hit | Holds |
| Placeholder engine, `PrepareAsync` with no cache hit (one parse) | 648 B | 648 B | 1.52 µs (1.53 µs) | None: a parse allocates its template | Measured |
| Placeholder engine, `RenderAsync` of one part | 144 B | 144 B | 280 ns (286 ns) | The rendered email strings | Holds |
| `EmailComposer.ComposeAsync` of one email | 1,544 B | 1,360 B | 5.32 µs (5.54 µs) | Each event and each message | Holds. `RouteGrouping` makes no closure and no iterator now (D280) |
| `EmbeddedResourceTemplateStore.FindAsync` with a hit of its source cache | 0 B | 0 B | 34.7 ns (22.5 ns) | A template store lookup with a cache hit | Holds |
| `SubscriptionResolver.Resolve`, 1 user | 3,152 B | 2,040 B | 292 ns (415 ns) | Each resolution of subscriptions | Measured |
| `SubscriptionResolver.Resolve`, 100 users | 175,280 B | 41,728 B | 9.49 µs (19.2 µs) | Each resolution of subscriptions | Holds: about 401 B for each further user (1,739 B). Target: at most 500 B |
| `SubscriptionResolver.Resolve`, 10,000 users | 18,028,122 B | 3,971,168 B | 4.71 ms (18.9 ms) | Each resolution of subscriptions | Holds: about 397 B for each further user (1,803 B) |
| Subscription lookup with a cache hit for the defaults, one company, and one user | 448 B | 288 B | 179 ns (204 ns), 1.2 ns with no cache | The string cache keys, the result list, and the work of the backend | Holds. Target: at most 300 B |

The open items of phase 13 after phase 34 (D159, D280 to D282):
- The pipeline: closed. An event with one route allocates 688 B, and each further route about 300 B: the outgoing message (about 230 B), its place in the result array and in the array of the stored messages, and its entry in the set of the targets. The closure of the registry lookup and the topic string were the 150 B for each route that phase 13 did not attribute.
- `DispatchPendingAsync`: closed. A send allocates 108 B: the publish try and the share of the batch. The `SemaphoreSlim` waiter of `Parallel.ForEachAsync` and the two boxed outcome tags were the 150 B for each send that phase 13 did not attribute.
- `RunAsync`: open with its measured cause. The target of at most 190 B for each message does not hold: the benchmark measured 212 B (261 B before). The two boxed outcome tags are gone. A probe in the process, with the event `AllocationSampled`, measured 184 B for each message: the publish try 89 B, the read operation of the work queue 81 B, because each worker waits one time for each message with an instant transport (D281), and about 15 B for the state machines and the waits of each batch, which the 100 messages of a batch share. The benchmark counts about 28 B more for each message than the probe, as at the start of the phase (261 B against the 237 B of the probe of the plan). Both count the bytes of all threads. The cause of the difference is not confirmed.
- The cache hit: closed. A hit for 3 owners allocates 288 B: the 3 keys and the result list. The owner part strings, the two boxed enumerators, and the empty list of the missing owners are gone (D282).
- The resolution: closed with its own budget line (D282). Each further user of an opt-out allocates about 400 B: the opt-out decision with its skipped array, which is the result, and the scratch of the pair: the pair, its index entry, and the entries of the lists of the users. A unit test keeps it at most 500 B.

Measured cost of the Liquid engine (phase 23, D206). A Liquid render is outside the zero allocation target, because Fluid allocates a context and the output of each part. A probe outside the repository measured about 700 B and about 3 µs for the render of a short html template with `Fluid.Core` 2.41.1, with `GC.GetTotalAllocatedBytes` (roadmap, checked facts of phase 23). A host that needs the budget of this section uses the placeholder engine.

### 4.12 Error handling

**Confirmed rule:** MsgHatch defines no custom exception types. Each expected failure is a value, because `ErrorOr<T>` and `Error?` cost less than exceptions and make each failure case explicit:
- An operation with a result returns `ErrorOr<T>`.
- An operation with no result returns `Error?`.
- `ErrorType.IsTransient` tells the dispatcher to retry or to stop.
- The code of an `ErrorType` is its identity (D238). `ErrorType` compares and hashes by `Code` only, and the name and the transient flag are data about the code. `Errors.Find(code)` gives the type of `Errors` with a code, or null. A client of an HTTP API gives the type of `Errors.Find` for a known code, so `error.Type == Errors.NotFound` is true, and only an unknown code gives a new `ErrorType` from the wire. No code compares the type of an exception or the text of a message.

Exceptions stay at the edges:
- Each adapter that calls external code catches the exceptions of that code and returns an `Error`. External code is the AWS SDK, the broker library, SignalR, `System.Text.Json`, the file system, and embedded resources.
- The SES client, the Resend client, the Wolverine client, the SNS client, the SignalR transport, and the EF Core stores ask the `IExceptionClassifier<TClient>` registrations of the host before their built-in mapping (D141, D302, D322). The EF Core stores then ask the dialect of their provider package. A cancelled operation gives `Errors.OperationCancelled` before each classifier.
- The pipeline and the dispatchers catch an unexpected exception from consumer code one time, at their outer boundary. They convert it into `Errors.Exception`, so a defective store cannot stop the dispatcher loop.
- Cancellation becomes `Errors.OperationCancelled`.
- The HTTP APIs of the management interfaces write each `Error` as problem details with the extension member `errorType` (D233). The internal class `ErrorProblems` in `src/Shared/ErrorProblems.cs` holds the status of each type, and the error of a failed response, so no public type has an HTTP status. The server never sends `Error.Exception`.
- Only standard .NET exceptions remain. They are for programmer errors at a public entry point, for example `ArgumentNullException`, and for invalid configuration at startup (`OptionsValidationException`).

### 4.13 Contract versioning

**Confirmed rule (D24):** each persisted contract is backward compatible and has a version number.

| Persisted type | Owner | Version field |
|---|---|---|
| `ReceivedMessage`, `OutgoingMessage`, `DeliveryState`, `PublishTry`, `EmailMessage`, `SignalREnvelope<TPayload>`, and `BrokerEnvelope<TPayload>` | Library | `SchemaVersion` of the received or outgoing message |
| `ChannelDown`, `ChannelEntry`, and `MessageTypeEntry` | Library | The schema version of the store, for example `MsgHatchSchema` in SQLite |
| `Subscription` | `MsgHatch.Subscriptions` | The schema version of the subscription store, for example `MsgHatchSubscriptionSchema` in SQLite |
| Domain events | Consumer | `ContentVersion` of the received message |
| Template payloads in a SignalR or broker envelope | Consumer | `ContentVersion` of the outgoing message |
| Channel settings of a subscription | The channel package | The version of the content type `Settings:{Channel}`, in the field `SettingsVersion` (D77) |
| Route traces, for example `ResolutionTrace` | The router package | The version of the content type `Trace:{Name}`, in the discriminator of the trace (D98) |

A backward-compatible change obeys all of these rules. They apply to the library and to the consumer:
- Add a property only as nullable or with a default value.
- Do not remove a property. Do not change its type or its meaning.
- Fix the stored name of each property with the attribute of each serializer that the consumer uses, for example `[JsonPropertyName]` and `[BsonElement]`. Then a C# rename does not change a stored record.
- Do not put `required` or `[JsonRequired]` on a property that a later version adds.
- Add an enum member only at the end. Enum values are stored as strings (D23).
- The reader ignores unknown properties. So an older reader can read a newer record with the same version.

A change that breaks one of these rules is a breaking change (D25):
1. Increase the version in the registration.
2. Keep the old type, and register an upgrade function from it to the current type with `WithUpgrade` (section 4.7).
3. The store reads an old record as the old closed type from the registry. The library then upgrades it, and does not write the upgraded record back.
4. Delete the old type and its upgrade only when no stored record has the old version.

A record with a version that is newer than the code knows gives `Errors.UnsupportedVersion`. This error is transient, so a newer instance processes the record during a rolling deployment.

A person can edit a stored record in the tools of its storage. The edit must obey the same rules. An invalid edit fails at read time with `Errors.SerializationFailed`.

Golden-file tests enforce the rules:
- The unit test project of the owner of each persisted type keeps one JSON sample for each released version of that type, in its `Samples/` folder, for example `tests/MsgHatch.Domain.Tests.Unit/Samples/`.
- A test deserializes each sample with the code under test.
- A sample never changes after its release.
- The library has samples for its own types. The example app shows the same pattern for consumer types.

### 4.14 Time

**Confirmed rule (D31):** the library reads the current time only from an injected `TimeProvider`. Each time-based behavior is then testable with `FakeTimeProvider` from `Microsoft.Extensions.TimeProvider.Testing`.
- `AddMsgHatch` registers `TimeProvider.System` with `TryAddSingleton`. A consumer or a test that registers its own provider first keeps it.
- One internal helper reads `GetUtcNow().UtcDateTime`, because the stored fields are UTC `DateTime` values.
- The stores never read the clock. The library gives each time to the store: `nowUtc` to `ClaimDueAsync`, and the times in each message and in each publish try. So a consumer's store needs no `TimeProvider`.
- Each wait takes its time from `TimeProvider`. The poll delay of the dispatch loop is a `CancellationTokenSource(TimeSpan, TimeProvider)` timeout on the signal wait. The analyzer enforces this rule (D32).
- An elapsed time comes from `TimeProvider.GetTimestamp` and `TimeProvider.GetElapsedTime`.
- Each `FakeTimeProvider` in the tests starts at `2030-01-01T00:00:00Z`. This time is far from the real time, so a test fails if the code reads the system clock.

Next try time (D33). For a failed try with the number `n`, with `BaseDelay` and `MaxDelay` of the retry options of the channel registration of the message (D87):
1. `delay = min(MaxDelay, BaseDelay × 2^(n − 1))`.
2. `h = BinaryPrimitives.ReadUInt32LittleEndian(first 4 bytes of the message id) ^ ((uint)n × 2654435761)`.
3. `factor = 0.8 + 0.4 × h / uint.MaxValue`.
4. `NextTryAtUtc = FinishedAtUtc + delay × factor`.
5. When the error has a `RetryAfter`, `NextTryAtUtc` is the later of step 4 and `FinishedAtUtc + min(RetryAfter, MaxDelay)` (D306). So a wait of the provider moves the next try later, and never earlier than the schedule.

### 4.15 Service wrappers

**Confirmed rule (D34):** each service interface can have a logging wrapper. A wrapper implements the same interface, calls the inner implementation, and logs to `ILogger<T>` through source-generated `LoggerMessage` methods.

These interfaces get a logging wrapper (D36):

| Interface | Implemented by | Logging wrapper |
|---|---|---|
| `INotificationService` | Library | `NotificationServiceLoggingWrapper` |
| `IMessageRouter<TEvent>` | Consumer or `MsgHatch.Subscriptions` | `MessageRouterLoggingWrapper<TEvent>` |
| `IRegistryWriter` | Library | `RegistryWriterLoggingWrapper`. It logs each change at the Information level (section 4.20). |
| `IRegistrySettingsStore` | Library or consumer | `RegistrySettingsStoreLoggingWrapper` |
| `ITenantSource` | Consumer | `TenantSourceLoggingWrapper` |
| `IDeliveryQueries` and `IDeliveryCommands` | Library or an HTTP client | `DeliveryQueriesLoggingWrapper` and `DeliveryCommandsLoggingWrapper`. The commands wrapper logs each change at the Information level (section 4.19). |
| `IInboxProcessor` and `IOutboxDispatcher` | Library | `InboxProcessorLoggingWrapper` and `OutboxDispatcherLoggingWrapper` |
| `ITransport` | Library or consumer | `TransportLoggingWrapper` |
| `IInboxStore`, `IOutboxStore`, `IInboxDispatchStore`, and `IOutboxDispatchStore` | Library or consumer | `InboxStoreLoggingWrapper`, `OutboxStoreLoggingWrapper`, `InboxDispatchStoreLoggingWrapper`, and `OutboxDispatchStoreLoggingWrapper` |
| `ISubscriptionStore` | `MsgHatch.Subscriptions` or consumer | `SubscriptionStoreLoggingWrapper` |
| `IRecipientDirectory` | Consumer | `RecipientDirectoryLoggingWrapper` |
| `ISubscriptionQueries` and `ISubscriptionCommands` | `MsgHatch.Subscriptions` or an HTTP client | `SubscriptionQueriesLoggingWrapper` and `SubscriptionCommandsLoggingWrapper` |
| `IRoutingVerifier` | `MsgHatch.Subscriptions` | `RoutingVerifierLoggingWrapper` |
| `IPayloadLoader<TEvent, TPayload>` | Consumer | `PayloadLoaderLoggingWrapper<TEvent, TPayload>` |
| `IEmailClient` | Library (SES, Resend) or consumer | `EmailClientLoggingWrapper` |
| `ITemplateEngine<TPayload>` | Library or consumer | `TemplateEngineLoggingWrapper<TPayload>` |
| `ITemplateQueries` and `ITemplateCommands` | `MsgHatch.Templates` or an HTTP client | `TemplateQueriesLoggingWrapper` and `TemplateCommandsLoggingWrapper` (D244). They log the message type, the language, the part, and counts, and never a text, a company id, or the message of an issue. |
| `ITemplateStore` | Library or consumer | `TemplateStoreLoggingWrapper` |
| `ITemplateWriter` of a writable `ITemplateStore` | Library or consumer | `TemplateWriterLoggingWrapper`, which is also the `ITemplateStore` of the store, so a cast of the registered store finds the writer (D218) |
| `IBrokerClient` | Library (Wolverine, Amazon SNS) or consumer | `BrokerClientLoggingWrapper` |

Pure in-memory components get no wrapper: `IMessageRegistry`, the template selector, the parser, the renderer, the composers, `RouteGrouping`, `SubscriptionResolver`, `DeterministicId`, and the in-process dispatch signal. They do no I/O, and a wrapper call for each message costs time in the hot path. A composer does its I/O through the loader and the template engine, which have wrappers. The cache backend gets no logging wrapper, because the caching wrapper logs each hit, each miss, and each backend error.

What a logging wrapper logs:

| Result | Level | Fields |
|---|---|---|
| Success | Debug | The operation, the elapsed time, and counts, for example the number of routes |
| `Errors.OperationCancelled` | Information | The operation and the elapsed time |
| Transient error | Warning | The operation, the elapsed time, and the error code, name, and message |
| Permanent error | Error | The same fields as for a transient error |

- The public method `ErrorLogLevels.For` in `MsgHatch.Domain.Failures` gives the level of a failed call. Each wrapper of the library uses it, and a wrapper of another package can use it too.
- One exception: `TemplateEngineLoggingWrapper` logs `Errors.NotFound` of a `TemplateRequest` with `Optional` true at the Debug level, because a message can omit an optional part, for example the text body of an email. So each email with no text template does not log an error.
- A second exception: `TemplateEngineLoggingWrapper` logs a check with issues, and `Errors.TemplateInvalid` of `CheckAsync`, at the Information level, because a text that the check rejects is the input of an operator, not a fault of the system (D219, D241). It logs only the number of the issues, with the event 2016. A check that passes logs at the Debug level. The wrapper never logs the text.
- A third exception: `TemplateEngineLoggingWrapper` logs `Errors.TemplateInvalid` of the render of a draft (event 2032), and `TemplateQueriesLoggingWrapper` logs `Errors.TemplateInvalid` of a preview (event 2030), at the Information level, because the edit page asks for a preview after each pause in typing, and a draft with an open tag is the input of an operator (D258).
- A wrapper logs only these values: message ids, outgoing message ids, tenant ids, message types, channels, content types, message flags, try numbers, counts, error data, elapsed times, and the subscription ids, levels, and sources of a resolution trace. The structured property `MessageFlags` lets log analytics leave test messages out.
- The wrapper of a tenant service logs the tenant of its key.
- A wrapper never logs a channel address, a payload value, rendered text, or a domain event (D37).
- Each async wrapper method has `[AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]`, so a call that completes asynchronously does not allocate a state machine.
- For `RunAsync`, the dispatcher wrappers log the start and the stop of the loop at the Information level. The wrappers of the inner components log each claim and each send.

Wiring (D35 and D40):
- The registration methods register each implementation and its wrappers. No decorator package is necessary.
- `MsgHatchOptions.EnableLogging` adds or removes all logging wrappers.
- The order from outside to inside is: the logging wrapper, the caching wrapper, the implementation.

Subscription cache (D38 and D39). `SubscriptionStoreCachingWrapper` is opt-in. One setup call of a cache package adds it, for example `UseFusionSubscriptionCache`:
- The wrapper is a tenant service around the subscription store of its key (D126). It caches only `FindForDeliveryAsync`. It caches the subscriptions of one message type and one owner in one entry, with the key `{KeyPrefix}:{Tenant}:{MessageType}:d` for the defaults, `{KeyPrefix}:{Tenant}:{MessageType}:c:{CompanyId}` for a company, and `{KeyPrefix}:{Tenant}:{MessageType}:u:{UserId}` for a user. `{Tenant}` comes from its key: `n` for `TenantKey.None` and `t:{TenantId}` for a tenant, so the two forms never collide (D114). Each `{TenantId}`, `{CompanyId}`, and `{UserId}` of a key or a tag is percent-encoded with `Uri.EscapeDataString`, so it holds no `:`, and two tenants never share a key or a tag (D153).
- For a call, the wrapper calls `TryGetAsync` for each key. It calls the inner store one time for all missing owners, splits the result by owner, and caches each owner, also with an empty list. A user with no subscription for a message type then costs no store call on the next event.
- The inner store always returns the default subscriptions too. When the defaults are already in the cache, the wrapper ignores these extra rows.
- Each entry gets the tenant tag `{KeyPrefix}:{Tenant}` and the owner tag `{KeyPrefix}:{Tenant}:d`, `{KeyPrefix}:{Tenant}:c:{CompanyId}`, or `{KeyPrefix}:{Tenant}:u:{UserId}`.
- `SetAsync` gets `SubscriptionCacheOptions.Expiration`. The expiration is the job of the backend, so the wrapper reads no time.
- `QueryAsync`, `GetAsync`, `AddAsync`, `UpdateAsync`, and `DeleteAsync` go to the inner store with no cache. After a successful `AddAsync`, `UpdateAsync`, or `DeleteAsync`, the wrapper removes the owner tag of that subscription. For `DeleteAsync`, it reads the subscription first, to find its owner.
- The wrapper does not cache an `Error` from the inner store.
- A backend error from `TryGetAsync` counts as a miss. A backend error from `SetAsync` or `RemoveByTagAsync` does not fail the call. The wrapper logs each backend error at the Warning level.
- `ISubscriptionCache` is a tenant service too. `InvalidateAsync(SubscriptionOwner)` removes one owner tag of the tenant of its key, and `InvalidateAllAsync` removes the tenant tag of its key. So one tenant never clears the cache of another tenant. A consumer calls them after a change of the subscription data outside the library, for example a direct database update.

The cache backends (phase 11):
- `HybridCacheSubscriptionBackend` in `MsgHatch.Subscriptions.Caching.Hybrid`. HybridCache has no read method without a factory. So `TryGetAsync` calls `GetOrCreateAsync` with a factory that returns no value, and with the flags `DisableLocalCacheWrite` and `DisableDistributedCacheWrite`, so that a miss is not stored. `SetAsync` and `RemoveByTagAsync` call the methods of the same names.
- HybridCache keeps a value of a mutable type in memory as serialized bytes, and gives each read a new copy. The default serializer cannot read the abstract `ChannelSettings` of a `Subscription`. So the backend keeps each list in the public class `HybridCacheSubscriptionEntry`, which is sealed and has `[ImmutableObject(true)]`. HybridCache keeps this instance in memory with no serializer, and gives it to each read with no copy (D152).
- `FusionCacheSubscriptionBackend` in `MsgHatch.Subscriptions.Caching.FusionCache`. It calls `TryGetAsync`, `SetAsync` with the tags, and `RemoveByTagAsync` of `IFusionCache`. FusionCache keeps the instance in memory with no serializer. With a FusionCache backplane, an invalidation also reaches the other instances.
- Each backend catches the exceptions of its cache library and returns an `Error` (section 4.12).
- A distributed second level needs a serializer: of `HybridCacheSubscriptionEntry` in HybridCache, and of `IReadOnlyList<Subscription>` in FusionCache. The consumer configures it in the cache library.

### 4.16 Log events

Each namespace has one `internal static partial class Log` with `[LoggerMessage]` extension methods of `ILogger`, so a caller writes `logger.AddFailed(...)`. Structured property names are PascalCase, for example `{MessageType}`. Each area has its own event id range:

| Area | Event ids |
|---|---|
| Pipeline, routers, and `INotificationService` | 1000 to 1099 |
| `MsgHatch.Subscriptions`: the subscription router, the stores, the cache, the management API, and the routing verifier | 1100 to 1199 |
| Inbox | 1200 to 1299 |
| Outbox | 1300 to 1399 |
| Dispatch loop | 1400 to 1499 |
| Persistence | 1500 to 1599 |
| SQLite stores | 1600 to 1699 |
| File stores | 1700 to 1799 |
| Delivery management | 1800 to 1899 |
| Email, templates, and the placeholder engine | 2000 to 2099 |
| Amazon SES | 2100 to 2199 |
| SignalR | 2200 to 2299 |
| Broker and its clients | 2300 to 2399 |
| Subscription HTTP endpoints | 2400 to 2499 |
| Cache backends | 2500 to 2599 |
| Delivery HTTP endpoints | 2600 to 2699 |
| EF Core stores | 2700 to 2799 |
| MongoDB stores and the MongoDB dispatch signal | 2800 to 2899 |
| Registry administration and its HTTP endpoints | 2900 to 2999 |
| Resend | 3000 to 3099 |
| Named pipe dispatch signal | 3100 to 3199 |
| RabbitMQ dispatch signal | 3200 to 3299 |

### 4.17 Subscription router and management

**Confirmed rule (D97):** the package `MsgHatch.Subscriptions` is a router with subscriptions on three owner levels. Core MsgHatch does not reference it. `UseSubscriptions()` selects `SubscriptionRouter<TEvent>` as the router of each message type with no router of its own (D99). `docs/subscription-domain.md` describes the domain, each action, its use cases, and numbered examples for the tests.

Scope (D16). `MessageBuilder<TEvent>.WithScope(Func<TEvent, NotificationScope>)` registers the scope function of a message type. `WithScope` and `WithTestEvent` (section 4.18) add their functions to the internal `SubscriptionMessage<TEvent>` of the message type. `SubscriptionRouter<TEvent>` needs the scope function, so the startup validation of section 4.7 fails for a message type with the subscription router and no `WithScope`.

Routing procedure. `SubscriptionRouter<TEvent>.RouteAsync` does these steps. The router, its store, and its directory are the tenant services of the key of the received message, so the router uses only the subscriptions and the profiles of that tenant (D116, D126):
1. **Scope.** It calls the scope function of the message type. It gets a `NotificationScope`, and removes duplicate user ids.
2. **Directory.** For a user scope, it calls `IRecipientDirectory.GetUsersAsync` one time. For a company scope, it calls `GetCompanyAsync`, and calls `GetCompanyUsersAsync` only when a channel of the message type has no group delivery.
3. **Subscriptions.** It calls `ISubscriptionStore.FindForDeliveryAsync` one time, with the message type, the companies of the users or the scope company, and the users.
4. **Resolution.** First, it loads the profiles that the fixed subscriptions need: `GetCompanyAsync` for each company of a user scope that has a subscription for the company, and `GetUsersAsync` one time for the named users that step 2 did not load. Then `SubscriptionResolver` resolves the subscriptions (D72, D74, D75, D88 to D91). It sorts arrays and uses slices, with no LINQ:
   - **Overridable part, for one user and one channel.** A mandatory default subscription for the channel applies. Otherwise a mandatory subscription of the company of the user for the channel applies. Otherwise the most specific level with a subscription for the scope users for that channel applies: the user, then the company of the user, then the default. An opt-out for that channel or for all channels counts as a subscription for that channel, and gives no delivery.
   - **Company scope and a channel with `GroupDelivery`.** The same order for the company: a mandatory default subscription, a mandatory subscription of the company, a subscription of the company, and then a default subscription. It gives one effective subscription with the company as the target.
   - **Company scope and a channel without `GroupDelivery`.** The overridable part for each user of the company, with only the channels without `GroupDelivery`. So no user gets the message of a group channel two times.
   - **Fixed part (D89, D90).** For each company that the event concerns, the company of each user of a user scope or the scope company:
     - Each default subscription and each subscription of that company for the company gives one delivery with the company as the target.
     - Each subscription of that company for named users gives one delivery to each named user whose company in the directory is that company. The resolution skips a named user of another company, so that the data of one company does not reach another company.
     - Each default subscription for named users gives one delivery to each named user.
     - A fixed subscription gives one delivery for each event, also when several users of the scope belong to its company.
   - **Merge (D91).** For one target and one channel, the resolver keeps one effective subscription. A fixed subscription applies before an overridable one. Between two fixed subscriptions, the higher level applies. On the same level, a subscription for named users or for the company applies before a mandatory one for the scope users.
   - A subscription for a channel that the message type has not registered is skipped.
   - The language is the language of the channel settings, `ILanguageSettings.Language` (D145). If it is null, the language is the UI language of the user, or the default language of the company for a company target. If that is also missing, it is `MsgHatchOptions.DefaultLanguage`.
   - The address comes from `UserProfile.Addresses` for a user target, and from `CompanyProfile.Addresses` for a company target on a channel without `GroupDelivery`. When the profile has no address for the channel, the route has no address, and the address source is `None`. The email composer then fails that message (section 4.8).
   - The template scope is the text of the company of the target user, or of the target company (D80).
   - The resolver records a `ResolutionTrace` for each effective subscription, and an `OptOutDecision` for each opt-out that applies (section 4.4, D92).
5. **Routes.** It returns the effective subscriptions as the routes, each with its `ResolutionTrace` as the route trace (D98). It logs each opt-out decision at the Debug level, because an opt-out gives no route.

A store error or a directory error fails the routing, and so the event (section 4.8).

Resolution of the subscription example (section 1.1) for one event of the type B with the users Client 1 and Client 2:
- Client 1 has a user subscription for B and the channel Email, so the company opt-out of Acme does not apply to Client 1. Client 1 gets an email in English.
- Client 2 has no user subscription for B. Acme has an opt-out for B, so Client 2 gets nothing.
- Acme has a fixed subscription for B for the company. The event concerns Acme, so `admin@acme.example` gets one email in Hungarian. The opt-out of Acme does not stop it, because the opt-out is for the scope users only.

**Confirmed rule (D53, D231):** `ISubscriptionQueries` lists and `ISubscriptionCommands` changes the subscriptions of all three levels. `SubscriptionManager` of `MsgHatch.Subscriptions` implements both interfaces on its contract `ISubscriptionStore` (D78). The queries have `GetChannelsAsync`, `GetMessageTypesAsync`, `GetSubscriptionsAsync`, and `GetSubscriptionAsync`, and the commands have `CreateAsync`, `UpdateAsync`, and `DeleteAsync`.

Actions:
- `GetChannelsAsync` returns the descriptor of each registered channel, with its features (D77), from `IMessageRegistry`.
- `GetMessageTypesAsync(query)` returns each message type with a scope function, or only `query.Type`, with its registered channels (D81). `AppliedSubscriptions` holds, for each channel, the subscription or the opt-out that applies, each with its own level:
  - With `query.User`: the subscription or the opt-out that applies to that user for each channel, after the merge of the routing procedure above. Then the fixed subscriptions for named users that name the user. The directory gives the company of the user. An unknown user gives `Errors.NotFound`.
  - With only `query.Company`: for each channel, the subscription or the opt-out that applies to the scope users of that company: a mandatory default subscription, a subscription or an opt-out of the company, or a default subscription or opt-out. Then the fixed subscriptions of the company, and the default subscriptions for the company.
  - With no user and no company: the default subscriptions and the default opt-outs.
- `GetSubscriptionsAsync(query)` returns the stored subscriptions that match all given filters, with `ISubscriptionStore.QueryAsync`.
- `GetSubscriptionAsync(id)` returns one subscription, or `Errors.NotFound`.
- `CreateAsync(draft)` validates the draft, and then adds a subscription. The id is `Guid.CreateVersion7` of the time from `TimeProvider`, the revision is 1, and `CreatedAtUtc` and `LastModifiedAtUtc` are the time from `TimeProvider`. A draft with no recipients gets `SubscriptionRecipients.ScopeUsers`.
- `UpdateAsync(id, expectedRevision, draft)` validates the draft, and replaces the kind, the channel, the settings, the named users, and `Mandatory`. The owner, the message type, and the recipient kind cannot change. The revision increases by 1, and `LastModifiedAtUtc` is the time from `TimeProvider` (D79).
- `DeleteAsync(id)` deletes one subscription.

Validation (D234). `CreateAsync` and `UpdateAsync` validate the draft first, and check the conflicts only for a valid draft. So an invalid draft never gives `Errors.Conflict`. They return `Errors.ValidationFailed`, and make no store change, in the cases below. First, the public `SubscriptionDraftRules.Check(draft)` runs the rules that need no state: the owner matches its level, an opt-out has no settings, is for the scope users, and is not mandatory, a channel subscription has a channel and settings, a user subscription is for the scope users, a mandatory subscription is a default or company subscription for the scope users, and the named users are 1 to 100 different users. Then the checks that need the registry, the directory, the store, or the culture data run in the order of the list. So an input that breaks a rule and another check gives the error of the rule, and each implementation, also an HTTP client, gives the same first error. `UpdateAsync` runs the rules before it reads the stored subscription. A null draft or query gives an `ArgumentNullException`. The cases:
- The owner does not match its level (section 4.4).
- The message type is not registered, or has no scope function, so the subscription router does not route it.
- A subscription names a channel that the message type has not registered. Or a subscription of the kind `Channel` has no channel or no settings.
- The settings type is not the settings type of the channel.
- The settings have a language, but the channel has no feature `Languages`.
- The language is not null, and `CultureInfo.GetCultureInfo` does not know it.
- The settings validator of the channel rejects the settings. Each channel package registers an `IChannelSettingsValidator` for its settings type. For example, `EmailFormat.Html` needs the feature `RichText`.
- A subscription of the kind `OptOut` has settings.
- `UpdateAsync` has another owner, another message type, or another recipient kind than the stored subscription.
- A user subscription has recipients other than the scope users.
- The named users are empty, have a duplicate, or have more than 100 users. Or the directory does not know a named user. Or a named user of a company subscription belongs to another company. The manager calls `IRecipientDirectory.GetUsersAsync` one time for the named users.
- An opt-out has recipients other than the scope users, or is mandatory.
- A mandatory subscription is a user subscription, or has recipients other than the scope users.

Conflicts. `CreateAsync` and `UpdateAsync` return `Errors.Conflict` in these cases:
- The owner already has a subscription for the same message type, channel, and recipient kind. An opt-out for one channel counts as a subscription for that channel.
- An opt-out for all channels, and the owner already has a subscription for the scope users for the same message type. Or a subscription for the scope users, and the owner already has an opt-out for all channels for the same message type.
- `UpdateAsync` with an `expectedRevision` that is not the stored revision.
- A company or user subscription for the scope users, or an opt-out, for one channel, while a higher level has a mandatory subscription for the same message type and channel: the default level, or for a user, the company of the user (D88). An opt-out for all channels is allowed. It leaves the mandatory channel in place.

The caching wrapper removes the cache entries of the owner after each change (section 4.15).

HTTP endpoints (D54). `MapMsgHatchSubscriptions(this IEndpointRouteBuilder routes, string prefix = "/subscriptions", Action<SubscriptionEndpointOptions>? configure = null)` in `MsgHatch.Subscriptions.AspNetCore` maps these minimal API endpoints, and returns the `RouteGroupBuilder`:

| Method | Path | Body | Success |
|---|---|---|---|
| GET | `{prefix}/channels` | none | 200 with the channel descriptors |
| GET | `{prefix}/message-types?messageType=&companyId=&userId=` | none | 200 with the message type states |
| GET | `{prefix}?messageType=&companyId=&userId=&level=` | none | 200 with the subscriptions |
| GET | `{prefix}/{id}` | none | 200 with the subscription |
| POST | `{prefix}` | The draft: `level`, `companyId`, `userId`, `messageType`, `kind`, `channel`, `settings`, `settingsType`, `recipients` (`kind` and `users`), `mandatory` | 201 with the subscription and a `Location` header |
| PUT | `{prefix}/{id}` | The draft and `revision` | 200 with the subscription |
| DELETE | `{prefix}/{id}` | none | 204 |

- Each route takes the optional query parameter `tenantId`. The endpoint resolves `ISubscriptionQueries`, `ISubscriptionCommands`, and `IRoutingVerifier` with `TenantKey.For(tenantId)`, and with `TenantKey.None` when the parameter is missing (D126).
- The `settings` object is read with the `JsonTypeInfo` of the settings type of the given channel, from the content type registry. For example, `{ "channel": "Email", "settings": { "language": "hu", "format": "PlainText" } }`. With the optional `settingsType`, for example `Settings:SignalR`, the endpoint reads the settings with that registered settings type, so the manager checks the settings of the type of another channel (D247). A `settingsType` that is not a registered content type `Settings:{channel}` gives 400. `SubscriptionRequest.From` writes `settingsType` for typed settings, and no value for `JsonChannelSettings`.
- The consumer adds the authorization on the returned group, for example `.RequireAuthorization("SubscriptionAdmin")`. The library adds no authorization.
- With `o => o.CurrentUser = user => ...` and, for several tenants, `o.CurrentTenant = user => ...`, the same endpoints also exist under `{prefix}/me`. They take the user id from the signed-in user, and the tenant from `CurrentTenant` in place of the query parameter. They use only user-level subscriptions of that user. A subscription of another owner gives 404 there. They ignore the owner fields of a body and the owner filters of a query. An unknown id gives the same 404 as another owner, and a signed-in user with no user id gives 403 (D154).
- Each error is problem details (RFC 9457) with the status of `ErrorProblems.StatusCodeOf` (D233): `Errors.ValidationFailed` gives 400, `Rejected` 403, `NotFound` 404, `Conflict` 409, `Expired` 410, `TemplateInvalid` 422, `RateLimited` 429, `Unavailable` 503, and each other error 500. The body has the error name as the title, the error message as the detail, and the extension member `errorType` with `code`, `name`, and `isTransient` of `ErrorTypeBody`. A `POST` or a `PUT` body with a content type that is not JSON gives 415 (D156), with no `errorType`.
- The bodies, their JSON context `SubscriptionEndpointJson`, `SettingsJson`, and `JsonChannelSettings` are public types of `MsgHatch.Subscriptions.Api.Contracts` (D227, D228). Each body record has public mappings in both directions, for example `SubscriptionResponse.From(subscription, registry)` and `ToSubscription(registry)`. A mapping from the wire checks each string with `Create`, and gives `Errors.SerializationFailed` for a value that is not valid. A response of a channel with no settings type in the registry of the host gives `JsonChannelSettings(JsonElement Json)`, and a request writes its JSON with no change (D229). The server still gives `Errors.ValidationFailed` for the settings of a channel with no settings type. The route paths are the internal class `SubscriptionApiRoutes` in `src/Shared`, which the endpoint package compiles. `RequestValues` and `Caller` stay in `MsgHatch.Subscriptions.AspNetCore`, because they check the input from outside with `Errors.ValidationFailed`, and hold the rules of `/me` (D154).
- The JSON of the endpoints uses camelCase names and enum values as strings, through a source-generated `JsonSerializerContext`. A flags value is one text, for example `"Languages, RichText"` (D155). A subscription in a response has the field names of the draft, with `id`, `revision`, `createdAtUtc`, and `lastModifiedAtUtc`, so a client can send it back as the body of a `PUT`.
- `POST {prefix}/verification` with a `RoutingVerificationRequest` runs the routing verification of section 4.18, and gives 200 with the report. Only the route group with no `/me` has it (D163). The endpoint rejects a body with no test users, no languages, or an empty language with 400, and the verifier runs `RoutingVerificationRules` too (D248).

### 4.18 Test messages and routing verification

**Confirmed rule (D82):** an incoming message can be a test message. The caller gives `MessageFlags.Test` to `AcceptAsync` or `ProcessAsync`, for example when a test client or a header of the broker message marks the event as a test. The flag goes to each outgoing message, to the stores, and to each log record:
- `IDeliveryCommands.DeleteTestDataAsync` calls `DeleteTestDataAsync` of the inbox dispatch store and of the outbox dispatch store, and returns the two counts. The route `DELETE {prefix}/test-data` calls it (D291). It deletes the test data of all tenants. A test run calls it at its end. Before phase 15, the tests call the dispatch stores directly.
- Analytics can leave test data out with the `Flags` field of the stores and the `MessageFlags` property of the log records.
- A channel handles a test message as its `TestDeliveryOptions` say (section 4.8, D83). The default `Suppress` records a try and sends nothing, so that a test never reaches a real recipient by mistake.

**Confirmed rule (D85):** `IRoutingVerifier` of `MsgHatch.Subscriptions` runs a full routing test of the subscription router. It resolves and generates the messages of all message types with a scope function, for all stored subscriptions, all test users, and all languages, and reports each result. It generates the messages with `INotificationService.PreviewAsync`, so it stores nothing and sends nothing (D100).

Test events. `MessageBuilder<TEvent>.WithTestEvent(string testCase, Func<UserId, TEvent> create)` of `MsgHatch.Subscriptions` registers one test case of a message type. The function makes a test event for one test user, so that the payload loader loads the real data of that user. Several test cases of one message type give the variations of the test messages, for example a long name, accented characters, or optional fields that are null.

Procedure of `VerifyAsync`. The verifier is a tenant service. Its store and its directory are those of its key, so a verification checks one tenant (D116, D126). First, the public `RoutingVerificationRules.Check(request)` gives `Errors.ValidationFailed` for no list of test users, no list of languages, or a language that is null, empty, or white space, and the verifier then calls no store (D248). Each implementation of `IRoutingVerifier` runs it first, so the HTTP client gives this error with no HTTP call:
1. It calls `ISubscriptionStore.QueryAsync` one time, for all subscriptions, or for the subscriptions of `request.Type`.
2. It calls `IRecipientDirectory.GetUsersAsync` one time for `request.TestUsers`.
3. For each message type with no test event, it adds one `Skipped` row.
4. For each test case, each test user, and each language of `request.Languages`:
   1. It copies the profile of the test user, with that language as the UI language.
   2. It makes the test event with the function of the test case.
   3. It runs the routing procedure of section 4.17 with two changes: the profile copy is the directory result for the test user, and the subscriptions of step 1 replace the store call. The directory gives the other profiles that the fixed subscriptions need, for example a company address or a named user.
   4. It calls `INotificationService.PreviewAsync` one time with the test event and the routes (section 4.8).
   5. It adds one row for each route: `Passed` when the `RouteResult` has a message, or `Failed` with the error code and name, for example `NotFound` for a template that is missing in that language and in the default language (D123). Each row has the owner of the subscription that applied, its target, and its resolution trace. The target of a fixed subscription can be the company or a named user, not the test user.
   6. It adds one `Passed` row with no channel when the resolution gives no delivery, for example for an opt-out. The owner of the row is the owner of the opt-out of the `OptOutDecision`, and the row has no trace. A row with no delivery and no opt-out has no owner.
   7. A test user that the directory does not know gives one `Failed` row with `Errors.NotFound`, with no owner, no target, and no channel, in place of steps 1 to 6 (D160). An error of the routing or of the preview gives one `Failed` row with that error and no channel, and the verification continues. Only `Errors.OperationCancelled` stops it (D161).
5. It adds one `Skipped` row for each stored subscription of a message type of step 4 that no variation used, for example a subscription of a company with no test user. The `Skipped` row of step 3 already covers the subscriptions of a message type with no test event (X6). A variation uses a subscription when that subscription applied to a route or as an opt-out, or when the trace of a route or an opt-out decision lists it as skipped. An opt-out decision has its own skipped list, but its row has no trace, so the trace of the row alone is not enough (X3). This shows the gaps of the test users.

The verifier stores nothing and sends nothing. A test job can fail when `RoutingVerificationReport.Failed` is greater than 0. `docs/subscription-domain.md` has the examples X1 to X7.

### 4.19 Delivery management

**Confirmed rule (D107, D289):** `IDeliveryQueries` and `IDeliveryCommands` are the API for a UI of the consumer. The UI shows the traffic and the history of the inbox and the outbox, and repairs stuck messages and down channels. The internal `DeliveryManager` implements both on the tenant stores and the dispatch stores (D108, D127), and `MsgHatch.Api.Client` implements both over HTTP (D294, section 4.23). They are tenant services: their message members use the tenant stores of their key. The channel members of the commands, `DeleteTestDataAsync`, and `PurgeAsync` act on all tenants through the dispatch stores, because a channel is global (D106). So only the commands of `TenantKey.None` change this global state: `PauseChannelAsync`, `ResumeChannelAsync`, `DeleteTestDataAsync`, and `PurgeAsync` of the commands of a tenant give `Errors.ValidationFailed`. `GetChannelsAsync` of the queries works for each key.

The queries have the channels, the traffic, the pages, the detail of an outgoing message, and the content. The commands have the pause, the resume, the retries, the cancels, the delete of the test data, and the purge. `AddMsgHatch` registers `DeliveryManager` as the default scoped tenant service of both interfaces, each with its logging wrapper. `UseDeliveryQueries` and `UseDeliveryCommands` select another implementation, and a second selection gives the setup error of D62.

Types (`MsgHatch.Domain.Management`):

```csharp
public readonly record struct MessageCursor(DateTime CreatedAtUtc, Guid Id);

public sealed record MessagePage<T>(IReadOnlyList<T> Items, MessageCursor? Next);

public sealed record InboxQuery(
    IReadOnlyList<Guid>? Ids = null, DeliveryStatus? Status = null, MessageType? Type = null,
    DateTime? CreatedFromUtc = null, DateTime? CreatedToUtc = null, DateTime? DueBeforeUtc = null,
    bool IncludeTest = false, int Limit = 50, MessageCursor? After = null);

public sealed record OutboxQuery(
    IReadOnlyList<Guid>? Ids = null, DeliveryStatus? Status = null, MessageType? Type = null,
    ChannelId? Channel = null, DeliveryTarget? Target = null, Guid? SourceMessageId = null,
    DateTime? CreatedFromUtc = null, DateTime? CreatedToUtc = null, DateTime? DueBeforeUtc = null,
    bool IncludeTest = false, int Limit = 50, MessageCursor? After = null);

public sealed record ReceivedMessageSummary(
    DeliveryState Delivery, TenantId? Tenant, MessageType Type, int ContentVersion, MessageFlags Flags);

public sealed record OutgoingMessageSummary(
    DeliveryState Delivery, Guid SourceMessageId, TenantId? Tenant, MessageType Type, ChannelId Channel,
    DeliveryTarget Target, string ContentType, int ContentVersion, MessageFlags Flags);

public sealed record OutgoingMessageHistory(OutgoingMessage Message, IReadOnlyList<PublishTry> Tries);

public sealed record OutgoingMessageDetail(
    OutgoingMessageSummary Summary, string? Trace, IReadOnlyList<PublishTry> Tries);

public sealed record MessageContent(Guid Id, string Json);

public sealed record TrafficQuery(
    DateTime? CreatedFromUtc = null, DateTime? CreatedToUtc = null, DeliveryStatus? Status = null,
    bool IncludeTest = false);

public readonly record struct MessageCount(
    MessageType Type, ChannelId? Channel, DeliveryStatus Status, bool Test, int Count, DateTime? OldestDueAtUtc);

public sealed record TrafficReport(IReadOnlyList<MessageCount> Inbox, IReadOnlyList<MessageCount> Outbox);

public sealed record ChannelStatus(
    ChannelDescriptor Descriptor, ChannelDown? Down, int Pending, int PendingTest, DateTime? OldestDueAtUtc);

public readonly record struct DeliveryActionResult(int Matched, int Changed);
```

Rules of the types:
- A query matches the messages of the tenant of the store that match all given filters. A null filter matches each value. `CreatedFromUtc` is included, and `CreatedToUtc` is not. With `IncludeTest` false, a query leaves out the messages with `MessageFlags.Test`.
- `DueBeforeUtc` matches the pending messages whose `NextTryAtUtc` is before it. With the current time minus some minutes, it finds the stuck messages: due, but not sent.
- A page has the messages in the order newest first: `CreatedAtUtc` descending, then `Id` descending. `After` is the cursor of the last message of the previous page, and the next page starts after that message. `Next` is the cursor of the last message of the page, or null when the page has fewer messages than `Limit`. This paging stays correct while the messages change (D112).
- A summary has no content: no event, no envelope, no address, and no payload value. `DeliveryState.LastError` can contain the error message of a provider.
- `OutgoingMessageHistory` is the store result of `GetAsync`: the whole message as its closed type, and all its publish tries in the order of their numbers.
- `OutgoingMessageDetail.Trace` is the JSON text of the route trace, written with the newest registered version of its type, with no `$type` (D290). `MessageContent.Json` is the JSON text of the whole message with its event or its envelope, as `MsgHatchJson` writes it with the content type registry of the server. An admin host has no message types (D235), so it shows the text, and a host that needs the typed message reads its tenant store with `IInboxStore.GetAsync` or `IOutboxStore.GetAsync`.
- `MessageCount` counts the messages of one message type, one channel, one status, and one test flag (D164). The channel is null for the inbox. `Test` is true for the messages with `MessageFlags.Test`. With `IncludeTest` false, a count has no row with `Test` true. `OldestDueAtUtc` is the earliest `NextTryAtUtc` of the counted messages, only for the status `Pending`.
- `ChannelStatus` joins the descriptor of a registered channel, its down state from the store, the number of its pending real messages and of its pending test messages, and the oldest due time of all its pending messages, in all tenants, because a channel is global (D106, D164).
- `DeliveryActionResult.Matched` counts the messages that the query found. `Changed` counts the messages that the action changed. The difference is the messages that the rules of the action skip.

Actions:
- `GetChannelsAsync` returns one `ChannelStatus` for each registered channel, from `IMessageRegistry`. It calls `GetDownChannelsAsync`, and `CountAsync` of the outbox dispatch store with the status `Pending` and `IncludeTest` true, one time each (D164).
- `PauseChannelAsync(channel, note)` calls `SetChannelDownAsync` with the reason `Operator`, the time from `TimeProvider`, and the note. Each publisher instance of the channel stops at its next claim (D106). A pause of a paused channel adds no row, and keeps the first note (D277).
- `ResumeChannelAsync(channel)` calls `SetChannelUpAsync` with no reason and the time from `TimeProvider`, so it ends a down state of each reason. Then it notifies the topic of the channel. If the provider still fails, the first transient error marks the channel down again (section 4.9).
- `GetTrafficAsync(query)` calls `CountAsync` of the inbox tenant store and of the outbox tenant store, one time each.
- `GetReceivedMessagesAsync` and `GetOutgoingMessagesAsync` call `QueryAsync` of their tenant store.
- `GetOutgoingMessageAsync(id)` calls `IOutboxStore.GetAsync`, and returns the summary, the JSON text of the route trace, and all publish tries, with no envelope. An unknown id gives `Errors.NotFound`, and a trace type that the host does not register gives `Errors.SerializationFailed`.
- `GetReceivedContentAsync(id)` and `GetOutgoingContentAsync(id)` return the whole message, with its event or its envelope, as `MessageContent` (D109, D290). An unknown id gives `Errors.NotFound`.
- The inbox store is optional. With no inbox store, an inbox page is empty, the inbox counts are empty, and an inbox content request gives `Errors.NotFound`.
- `RetryReceivedAsync`, `RetryOutgoingAsync`, `CancelReceivedAsync`, and `CancelOutgoingAsync` are bulk actions on the messages of a query:
  1. The manager reads the matching messages with `QueryAsync`, page by page, with `Limit` 500.
  2. For each page, it calls `RetryAsync` or `CancelAsync` of the tenant store with the ids of the page. A retry of the outbox calls the store one time for each content type of the page, because each content type has its own retry options.
  3. After a retry with changes, it notifies the topic of each changed channel, or the inbox topic, so that the loops claim at once.
  4. It returns the sum of the matched messages and of the changed messages.
- The manager has the comment `ponytail: a bulk action runs inside the call, a background job if a UI needs very large actions`.
- `DeleteTestDataAsync()` calls `DeleteTestDataAsync` of the inbox dispatch store and of the outbox dispatch store, and returns the two counts (section 4.18).
- `PurgeAsync(finishedBeforeUtc, maxCount)` calls `PurgeAsync` of the inbox dispatch store and of the outbox dispatch store, each with `maxCount`, so a large inbox never blocks the purge of the outbox. It returns the two counts (D128). A host calls it from a timer, for example each night with a retention of 30 days. The retention is also the window of the duplicate detection: a message id that comes again after the purge of its received message is a new message.

Retry rules (D111). `RetryAsync(ids, nowUtc, addedTries)` changes each message in one atomic operation:
- A `Failed` message gets the status `Pending`, `NextTryAtUtc` = `nowUtc`, and `MaxTryCount` = `TryCount` + `addedTries`. So it gets a new set of tries, and keeps its history.
- A `Pending` message with no active lease gets `NextTryAtUtc` = `nowUtc`, so it is due at once. Its `MaxTryCount` stays.
- A `Done` or `Cancelled` message, a message with an active lease, and a message whose `ExpiresAtUtc` is at or before `nowUtc` do not change. So a retry never resends a delivered message, and never sends a message after its expiry.
- `addedTries` is the `MaxTryCount` of the retry options of the content type of the message (D87), or of the retry options `Inbox` for the inbox.
- The store returns the number of changed messages. An unknown id changes nothing, and is not an error.

Cancel rules (D110). `CancelAsync(ids, nowUtc)` changes only a `Pending` message with no active lease:
- The message gets the status `Cancelled`, and no loop claims it again.
- The outbox tenant store also records a publish try with the next try number, `nowUtc` as the start and the finish time, the outcome `Cancelled`, and no error fields. It sets `TryCount` and `LastTryAtUtc`, and keeps `LastError`. So the history shows when the message was cancelled.
- The store returns the number of changed messages.

Validation. The public `DeliveryRules` of `MsgHatch.Domain.Management` holds the rules that need no state (D292). `DeliveryManager` and the HTTP client run them first, so a request that breaks a rule gives `Errors.ValidationFailed`, and calls no store and sends no HTTP request. The rules have the messages of the manager:
- `Limit` is less than 1 or more than 500.
- `Ids` is empty, or has more than 1,000 ids.
- `CreatedFromUtc` is not before `CreatedToUtc`.
- A bulk action has a query with no filter. One call then cannot retry or cancel the whole store. `Limit`, `After`, and `IncludeTest` are not filters for this rule.
- The note of `PauseChannelAsync` has more than 500 characters.
- A manager of a tenant gets a call of `PauseChannelAsync`, `ResumeChannelAsync`, `DeleteTestDataAsync`, or `PurgeAsync`.
- `PurgeAsync` has a `maxCount` less than 1.

The check that the `finishedBeforeUtc` of `PurgeAsync` is not after the time from `TimeProvider` stays in the manager, because the clock of a client can differ. It also gives `Errors.ValidationFailed`, and calls no store.

`PauseChannelAsync` and `ResumeChannelAsync` with a channel that is not registered give `Errors.NotFound`.

The library has no user concept for an operator. `DeliveryCommandsLoggingWrapper` logs each change at the Information level: the action, the channel, the filters of the query, and the counts. `DeliveryQueriesLoggingWrapper` logs each read at the Debug level with the count. Both wrappers keep the event ids 1800 to 1804 of the delivery manager. The host adds who did the action, for example in its HTTP log.

HTTP endpoints (D54, D109). `MsgHatch.AspNetCore` maps two route groups, so that the host can give the content a stricter authorization policy:
- `MapMsgHatchDelivery(this IEndpointRouteBuilder routes, string prefix = "/delivery")` maps the overview and the actions.
- `MapMsgHatchDeliveryContent(this IEndpointRouteBuilder routes, string prefix = "/delivery")` maps the content routes.
- Each method returns its `RouteGroupBuilder`. The host adds the authorization, for example `.RequireAuthorization("DeliveryAdmin")` on the first group and `.RequireAuthorization("DeliveryContent")` on the second group. The library adds no authorization.

| Group | Method | Path | Body | Success |
|---|---|---|---|---|
| Overview | GET | `{prefix}/channels` | none | 200 with the channel states |
| Overview | POST | `{prefix}/channels/{channel}/pause` | `note` | 204 |
| Overview | POST | `{prefix}/channels/{channel}/resume` | none | 204 |
| Overview | GET | `{prefix}/traffic?createdFrom=&createdTo=&status=&includeTest=` | none | 200 with the traffic report |
| Overview | GET | `{prefix}/inbox?ids=&status=&messageType=&createdFrom=&createdTo=&dueBefore=&includeTest=&limit=&after=` | none | 200 with a page of summaries |
| Overview | POST | `{prefix}/inbox/retry` and `{prefix}/inbox/cancel` | The filters of the query | 200 with `matched` and `changed` |
| Overview | GET | `{prefix}/outbox?` the inbox parameters and `channel=&targetKind=&targetId=&sourceMessageId=` | none | 200 with a page of summaries |
| Overview | GET | `{prefix}/outbox/{id}` | none | 200 with the detail: the summary, the trace, and the tries |
| Overview | POST | `{prefix}/outbox/retry` and `{prefix}/outbox/cancel` | The filters of the query | 200 with `matched` and `changed` |
| Overview | POST | `{prefix}/purge` | `finishedBefore` and `maxCount` | 200 with `inboxMessages` and `outboxMessages` |
| Overview | DELETE | `{prefix}/test-data` | none | 200 with `inboxMessages` and `outboxMessages` (D291) |
| Content | GET | `{prefix}/inbox/{id}/content` | none | 200 with the received message and its event |
| Content | GET | `{prefix}/outbox/{id}/content` | none | 200 with the outgoing message and its envelope |

- `ids` can repeat in a query string. Each route takes the optional query parameter `tenantId`. It selects the `IDeliveryQueries` or the `IDeliveryCommands` of `TenantKey.For(tenantId)`, or of `TenantKey.None` with no parameter (D126, D289). The pause, resume, purge, and test data routes act on all tenants, so they take no `tenantId`, and a `tenantId` gives 400. A cursor is the text `{CreatedAtUtc}_{Id}`, with the time in the ISO 8601 format of D57.
- A target filter of the outbox has a `targetKind`. A tenant target has no `targetId` or an empty one, and each other target has a `targetId` (D315). Each other form, for example a `targetId` with no `targetKind`, gives 400.
- The internal source `src/Shared/DeliveryApiRoutes.cs` holds the route paths, the names of the query values, and the text and the parse of a cursor (D291). `MsgHatch.AspNetCore` and `MsgHatch.Api.Client` compile it, so the routes and the client use one format.
- The bodies and their JSON context `DeliveryEndpointJson` are in `MsgHatch.Api.Contracts`, with public `From` methods and mappings back to the domain types (D228, D291). A name or a tenant that is not valid in a response body gives `Errors.SerializationFailed`. The JSON uses camelCase names and enum values as strings. A content route writes the JSON text of `MessageContent`, and the detail writes the JSON text of the trace as a JSON value.
- Each route writes its errors with `ErrorProblems` (D233, D291): the status of `ErrorProblems.StatusCodeOf`, the error name as the title, the message as `detail`, and the member `errorType`. So `Errors.ValidationFailed` gives 400, `Errors.NotFound` gives 404, and `Errors.Unavailable` gives 503. Before phase 36, each other error type gave 500 with no `errorType`. A body that is not valid JSON, a query value that is not valid, and a `tenantId` of a global route give 400 with the error type `ValidationFailed`.

A page of failed email messages, for `GET /delivery/outbox?channel=Email&status=Failed&limit=1`:

```json
{
  "items": [
    {
      "delivery": {
        "id": "7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31",
        "status": "Failed",
        "tryCount": 8,
        "maxTryCount": 8,
        "createdAtUtc": "2030-01-01T10:00:00Z",
        "lastTryAtUtc": "2030-01-01T10:31:12Z",
        "nextTryAtUtc": "2030-01-01T10:32:11Z",
        "leaseUntilUtc": null,
        "expiresAtUtc": null,
        "lastError": "Unavailable: Sending is paused for this account."
      },
      "sourceMessageId": "0b6c1f7e-2a44-4d8e-9a3b-61c0f5e2d7a9",
      "tenant": null,
      "type": "OrderShipped",
      "channel": "Email",
      "target": { "kind": "User", "id": "1" },
      "contentType": "Email:OrderShippedEmail",
      "contentVersion": 1,
      "flags": "None"
    }
  ],
  "next": "2030-01-01T10:00:00.0000000Z_7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31"
}
```

Two repairs with this API:
- **No publisher runs.** The channel page shows `Email` with 120 pending messages, the oldest due 40 minutes ago, and no down state. A person starts the email publisher, and the messages go out.
- **A provider outage.** The channel page shows `Email` down with the reason `Errors` and the error `Unavailable`. A person repairs the SES account, and resumes the channel, so the publishers claim at once. The outbox list with the channel `Email`, the status `Failed`, and the start time of the outage shows the messages that failed before the down state. A retry with the same filters gives them a new set of tries.

### 4.20 Registry administration

**Confirmed rule (D117, D118):** the code defines the channels and the message types. `IMessageRegistry` gives them to the pipeline, the subscription manager, and the delivery manager. A setup in code uses the in-memory reader, and needs no writer and no database. An administration host can add stored settings on top of the code with `IRegistryWriter`.

Types (`MsgHatch.Domain.Routing`):

```csharp
public sealed record ChannelEntry(
    ChannelId Channel, string? DisplayName, string? Description, bool Enabled,
    int Revision, DateTime LastModifiedAtUtc);

public sealed record MessageTypeEntry(
    MessageType Type, string? DisplayName, string? Description, bool Enabled,
    IReadOnlyList<ChannelId> DisabledChannels, int Revision, DateTime LastModifiedAtUtc);

public sealed record RegistryEntries(IReadOnlyList<ChannelEntry> Channels, IReadOnlyList<MessageTypeEntry> MessageTypes);
```

Rules of the entries:
- An entry holds only the settings that a person can change (D118). The code keeps the event type, the channels of a message type, the features, and the settings type.
- `DisplayName` has at most 200 characters, and `Description` at most 2,000 characters. Null means the value of the code. An empty text, or a text with only white space, is invalid (D171).
- `ChannelEntry.DisplayName` replaces `ChannelDescriptor.Name` in the reader. `MessageTypeEntry.DisplayName` replaces `MessageTypeDescriptor.DisplayName`.
- `DisabledChannels` has only channels that the message type registered, each one time.
- A channel or a message type with no entry has the values of the code, and is enabled.
- The registry settings belong to the whole system, not to a tenant (D114).

`StoredRegistry`, selected with `UseStoredRegistry()`, is one singleton that implements `IMessageRegistry` and `IRegistryWriter`:
- It keeps a frozen snapshot: the descriptors of the code, merged with the entries of `IRegistrySettingsStore`. The entry wins for each value that it has. A read uses the snapshot, with no store call.
- A timer of `TimeProvider` reads the store again after each `RegistryOptions.RefreshInterval`, and the registry swaps the snapshot after each read. A write in its own process also swaps the snapshot. So another process sees a write within one `RefreshInterval`. It has the comment `ponytail: a refresh interval in each process, a shared cache or a change signal if a change must reach all processes at once`.
- The registry starts its first read when the container creates it, and the pipeline waits for the end of that read before step 2 (D168). Before the first successful read, the snapshot has the values of the code. A store error on a read keeps the last snapshot, and the registry logs it at the Warning level.
- `GetEntriesAsync` reads the entries of the store, with their revisions.
- `SaveChannelAsync` and `SaveMessageTypeAsync` validate the entry with the public stateless rules `RegistryEntryRules` (D285): the display name, the description, the revision, and a `DisabledChannels` that is not null and has each channel one time. The check that each disabled channel is a channel of the message type needs the registry, so `StoredRegistry` runs it after the rules. The `Revision` of the entry is the expected revision, and 0 means a new entry. They save the entry with the next revision and the time from `TimeProvider` as `LastModifiedAtUtc`, and return the saved entry. An entry of an unregistered channel or message type gives `Errors.NotFound`. An invalid entry gives `Errors.ValidationFailed`. An expected revision that is not the stored revision gives `Errors.Conflict` (D79).
- `ResetChannelAsync` and `ResetMessageTypeAsync` delete the entry, so the values of the code apply again.

Effect on the delivery (D118, section 4.8, steps 2 and 3):
- A disabled message type gives no outgoing message. The pipeline does not call its router.
- A route on a disabled channel, or on a channel in `DisabledChannels` of its message type, is skipped.
- A message that is already in the outbox is still sent, because the settings act in the pipeline. To hold the messages of a channel, a person pauses the channel (D106).
- The settings do not change the stored subscriptions.
- `PreviewAsync` and the routing verification ignore the settings, and check the routing and the composition of the code (D169).

Registry settings stores:
- `InMemoryRegistrySettingsStore`, which `UseInMemoryStores()` also registers. It is not durable.
- The file store keeps one file for each entry: `{RootPath}/registry/channels/{channel}.json` and `{RootPath}/registry/message-types/{type}.json`.
- SQLite has the tables `MsgHatchChannelEntry` and `MsgHatchMessageTypeEntry`, with the entry fields as columns, `DisabledChannels` as a JSON array, and `Revision` for the update with the expected revision.
- The EF Core and MongoDB packages add their settings stores in their phases.
- Each store passes `RegistrySettingsStoreTestsBase` (section 5.5).

Registry queries (D268, D269). `IRegistryQueries` of `MsgHatch.Domain.Management` gives the registry to a management client, for example the pages of section 4.24:

```csharp
public interface IRegistryQueries
{
    ValueTask<ErrorOr<IReadOnlyList<RegistryChannel>>> GetChannelsAsync(CancellationToken ct);
    ValueTask<ErrorOr<IReadOnlyList<ChannelState>>> GetChannelHistoryAsync(ChannelId channel, CancellationToken ct);
    ValueTask<ErrorOr<IReadOnlyList<RegistryMessageType>>> GetMessageTypesAsync(CancellationToken ct);
}

public sealed record RegistryChannel(
    ChannelId Id, string Name, string? Description, ChannelFeatures Features, bool Enabled, int Revision, ChannelState? Current = null,
    ChannelEntry? Entry = null);

public sealed record RegistryMessageType(
    MessageType Type, string? DisplayName, string? Description, IReadOnlyList<ChannelId> Channels, bool Enabled,
    IReadOnlyList<ChannelId> DisabledChannels, int Revision, MessageTypeEntry? Entry = null);

public interface IRegistryCommands
{
    ValueTask<ErrorOr<ChannelCheck>> CheckChannelAsync(ChannelId channel, CancellationToken ct);
    ValueTask<ErrorOr<ChannelEntry>> SaveChannelAsync(ChannelEntry entry, CancellationToken ct);
    ValueTask<ErrorOr<MessageTypeEntry>> SaveMessageTypeAsync(MessageTypeEntry entry, CancellationToken ct);
    ValueTask<Error?> ResetChannelAsync(ChannelId channel, CancellationToken ct);
    ValueTask<Error?> ResetMessageTypeAsync(MessageType type, CancellationToken ct);
}

public sealed record ChannelCheck(ChannelId Channel, int? ErrorCode, string? ErrorName, bool Changed)
{
    public bool Healthy => ErrorCode is null;
}
```

- The types hold the values of `IMessageRegistry`, the revision of the stored entry, and from phase 35 the stored entry itself (D286). They have no settings type and no event type, so they can go over HTTP.
- The names and the descriptions of the queries are the merged values. So an edit page reads the stored values from `Entry`: a null value of the entry is the value of the code, and an empty field saves null (D286, D287).
- The internal `RegistryManager` joins `IMessageRegistry` with `IRegistryWriter.GetEntriesAsync`, and takes each entry and its revision from the entries of that one read. With no writer, for example with the in-memory reader of a setup in code, each revision is 0 and each entry is null. An error of the writer is the error of the query.
- From phase 32 (D278), `RegistryManager` also reads `GetChannelStatesAsync` of the outbox dispatch store one time, from 30 days before now (`ChannelState.HistoryWindow`). `Current` of each channel is its latest row, with the kind and the duration of the previous state, and null for a channel with no row. `GetChannelHistoryAsync` gives the rows of one channel in the window and the row that was current at its start, newest first, and `Errors.NotFound` for a channel that is not registered. An error of the store is the error of the query.
- `AddMsgHatch` registers `RegistryManager` as the default tenant service of `AddDefaultTenantService`, with its logging wrapper. `UseRegistryQueries` selects another implementation, for example the HTTP client of section 4.23, and a second selection gives the setup error of D62.
- The registry belongs to the whole system (D114), so each tenant key gives the same values. It is still a tenant service, so the pages keep the rule of D249, and the client uses the selection method of D231.
- The logging wrapper logs each call with the tenant of its key, the count or the error, and the duration, with the events 2909 to 2914 (D244). It never logs a display name or a description.
- From phase 33 (D283), `IRegistryCommands.CheckChannelAsync` runs the health check of the transport of one channel in the host. `RegistryManager` implements it: with no error, it calls `SetChannelUpAsync` with the reason `Errors`, and a new `Up` row notifies the dispatch signal of the channel. With an error, it calls `SetChannelDownAsync` with the reason `Errors`, the code and the name of the error, and `NextCheckAtUtc` after `CheckInterval` of the channel. So the store adds no row when the state does not change, and a check never ends a pause (D106, D277).
- A channel that is not registered, or that has no transport in the host, gives `Errors.NotFound`. A cancelled check gives `Errors.OperationCancelled` and changes nothing, because a cancel is not an outage. An error of the store is the error of the command.
- From phase 35 (D284), `IRegistryCommands` also saves and resets the stored settings, with the parameters and the results of `IRegistryWriter`. The `Revision` of an entry is the expected revision, and a save ignores `LastModifiedAtUtc`, as the writer does. `RegistryManager` runs `RegistryEntryRules` first, and then calls the writer. With no writer, each of the four commands gives `Errors.NotFound` with the message "The host has no registry writer. A setup in code cannot change the registry settings." An error of the writer is the error of the command. Phase 31 had no command interface, because the pages were read-only, and the PUT and DELETE routes called `IRegistryWriter` until phase 35.
- `AddMsgHatch` registers `RegistryManager` as the default of `IRegistryCommands` too, and `UseRegistryCommands` selects another implementation. Its logging wrapper logs each check with the events 2915 and 2916: the channel, the tenant of its key, the error name of the check and the change, or the error of the command. It logs each save and each reset with the events 2917 and 2918: the operation, the channel or the message type, the tenant of its key, and the new revision, or the expected revision and the error of the command. It never logs a display name or a description.

HTTP endpoints. `MapMsgHatchRegistry(this IEndpointRouteBuilder routes, string prefix = "/registry")` in `MsgHatch.AspNetCore` maps these endpoints, and returns the `RouteGroupBuilder`. The host adds the authorization. The six bodies and their JSON context `RegistryEndpointJson` are in `MsgHatch.Api.Contracts`, so the bodies of one API are in one package (D228, D270). `RegistryChannelResponse` and `RegistryMessageTypeResponse` have `From` and the mappings back to `RegistryChannel` and `RegistryMessageType`, and a name that is not valid gives `Errors.SerializationFailed`. From phase 35, they carry the stored entry as `entry`, with the bodies `ChannelEntryResponse` and `MessageTypeEntryResponse` and their mappings back `ToEntry` (D286). `ChannelEntryRequest.From` and `MessageTypeEntryRequest.From` give the PUT bodies of an entry for the client (D284). From phase 32, `ChannelStateResponse` maps `ChannelState` with `previousDurationMs` as a number, and `ChannelDownResponse` moves from `MsgHatch.AspNetCore` to the contracts, so the registry bodies and the delivery body `ChannelStatusResponse` use one package (D278). The internal source `src/Shared/RegistryApiRoutes.cs` holds the route paths, and `MsgHatch.AspNetCore` and `MsgHatch.Api.Client` compile it.

| Method | Path | Body | Success |
|---|---|---|---|
| GET | `{prefix}/channels` | none | 200 with each channel: its id, name, description, features, enabled flag, entry revision, `current` state row, and stored `entry` |
| GET | `{prefix}/channels/{channel}/history` | none | 200 with the state rows of the channel in the last 30 days and the row before them, newest first (D278) |
| POST | `{prefix}/channels/{channel}/check` | none | 200 with `channel`, `errorCode`, `errorName`, and `changed` of the check. The route is also in a host with no registry writer (D283) |
| PUT | `{prefix}/channels/{channel}` | `displayName`, `description`, `enabled`, and `revision` | 200 with the saved entry |
| DELETE | `{prefix}/channels/{channel}` | none | 204 |
| GET | `{prefix}/message-types` | none | 200 with each message type: its name, display name, description, channels, enabled flag, disabled channels, entry revision, and stored `entry` |
| PUT | `{prefix}/message-types/{type}` | `displayName`, `description`, `enabled`, `disabledChannels`, and `revision` | 200 with the saved entry |
| DELETE | `{prefix}/message-types/{type}` | none | 204 |

- The GET routes call `IRegistryQueries` of `TenantKey.None`, so they show the revision and the stored entry of each item, or 0 and null for no entry. A host that selects another implementation of `IRegistryQueries` changes what the GET routes give.
- From phase 35, the PUT and DELETE routes call `IRegistryCommands` of `TenantKey.None`, as the GET routes call the queries (D284). A host that selects another implementation of `IRegistryCommands` changes what these routes do.
- Each route writes its errors as problem details with `ErrorProblems` (D233): `title` is the name of the error type, `detail` is the message, and the member `errorType` holds the code, the name, and the transient flag. `Errors.ValidationFailed` gives 400, `Errors.NotFound` 404, and `Errors.Conflict` 409. Each other type gets the status of `ErrorProblems.StatusCodeOf`, for example 503 for `Errors.Unavailable`, and 500 for a type with no status of its own. A body that is not valid JSON also gives a problem with `errorType`.
- A PUT body needs a JSON content type, else 415 (D156). `enabled` and `revision` are required, and a missing `disabledChannels` means no disabled channel. An invalid channel or message type name in the path or in the body gives 400, and calls no command.
- `MapMsgHatchRegistry` maps each route in each host (D284). With the in-memory reader of a setup in code, the host has no writer: each revision is 0, and a PUT or a DELETE gives 404 with the problem body and the message of the command. So the client gives the same error and message as the in-process commands. Until phase 34, such a host had no PUT and DELETE routes.

### 4.21 Telemetry

**Confirmed rule (D131):** MsgHatch gives traces and metrics through the .NET APIs `ActivitySource` and `Meter`, with no OpenTelemetry dependency and no extension package. The names are public:

```csharp
// MsgHatch.Domain.Telemetry
public static class MsgHatchTelemetry
{
    public const string SourceName = "MsgHatch";
    public const string MeterName = "MsgHatch";
}
```

A host turns them on in its OpenTelemetry setup:

```csharp
services.AddOpenTelemetry()
    .WithTracing(t => t.AddSource(MsgHatchTelemetry.SourceName))
    .WithMetrics(m => m.AddMeter(MsgHatchTelemetry.MeterName));
```

Rules:
- The telemetry is in the pipeline and in the dispatch loops, not in the wrappers. A span for each store call would repeat the spans of the database client libraries, for example SqlClient or the MongoDB driver.
- With no listener, `StartActivity` returns null, and a measurement goes nowhere. The code then sets no tag, and the cost is almost zero.
- A span attribute or a metric tag never holds a channel address, a payload value, rendered text, or a domain event (D37).
- The span names and the attributes follow the OpenTelemetry messaging semantic conventions. These conventions have the status Development, so phase 1 records the version that the code follows, in the XML documentation of `MsgHatchTelemetry`.
- A log record inside a span gets the trace id and the span id from the logging provider, for example the OpenTelemetry logging provider. MsgHatch needs no code for this.

Spans. One internal static `ActivitySource` has the name `MsgHatchTelemetry.SourceName`:

| Span | Kind | Where | Parent or link |
|---|---|---|---|
| `process {MessageType}` | Consumer | `ProcessAsync`, and the inbox processor for each claimed message | `ProcessAsync`: a child of `Activity.Current`, for example of the span of an HTTP request. The inbox processor: a new trace, with a link to `ReceivedMessage.TraceParent` (D132). |
| `send {Channel}` | Client | The send worker, for each try that calls a transport | A new trace, with a link to `OutgoingMessage.TraceParent` (D132). |

Span attributes:
- `messaging.system` is `msghatch`. `messaging.operation.type` and `messaging.operation.name` are `process` or `send`.
- `messaging.destination.name` is the message type for `process`, and the channel for `send`.
- `messaging.message.id` is the id of the received message, or of the outgoing message.
- `msghatch.message_type`, `msghatch.channel` for `send`, and `msghatch.test`, which is true for a test message.
- `msghatch.tenant` holds the tenant id, only for a message with a tenant (D133).
- `process`: `msghatch.routes`, `msghatch.stored`, `msghatch.failed`, and `msghatch.skipped`, from `NotificationResult`.
- `send`: `msghatch.try_number`, and `msghatch.outcome` with the `PublishOutcome` of the try.
- An error sets the span status `Error`, and the attribute `error.type` to the name of the `ErrorType`, for example `RateLimited`. The span records no error message, because the message of a provider can contain an address (section 6).

Trace context of a message (D132):
- `ReceivedMessage.TraceParent` and `OutgoingMessage.TraceParent` hold the W3C `traceparent` text of a span, for example `00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01`, or null.
- `AcceptAsync` stores the id of `Activity.Current` when that activity uses the W3C id format.
- The pipeline gives each outgoing message the id of its `process` span. With no `process` span, it gives the id of `Activity.Current`, so that the send still links to the trace of the caller.
- The messages of one event share one trace context. MsgHatch makes no `create` span for each message, because one event can give thousands of messages (D43).
- The trace state is not stored. The code has the comment `ponytail: traceparent only, add the tracestate if a vendor needs it`.
- The broker client publishes inside the `send` span. A broker library with its own tracing makes its own span as a child of `Activity.Current`, and writes its trace context into its own message headers. MassTransit does this only when its `ActivitySource` has a listener, so a host that wants the trace across the broker also adds the source of the library. Wolverine writes the id of `Activity.Current`, which is the `send` span, into its header `parent-id` also with no listener. It makes its own `send` span only with a listener, for example with `AddSource("Wolverine")` (checked facts below). For MassTransit, the headers are `MT-Activity-Id` and `MT-Activity-Correlation-Context`, not `traceparent`.

Metrics. The internal singleton `MsgHatchMetrics` makes its `Meter` with `IMeterFactory.Create(MsgHatchTelemetry.MeterName)`. `AddMsgHatch` calls `AddMetrics()`, so `IMeterFactory` also resolves in a container with no host. Each instrument is made one time. A measurement takes its tags in a `TagList`, which does not allocate for up to 8 tags. The metrics have no tenant tag (D133).

| Instrument | Type | Unit | Tags | Measurement |
|---|---|---|---|---|
| `msghatch.messages.received` | Counter | `{message}` | `msghatch.message_type`, `msghatch.test` | 1 for each event of `AcceptAsync` or `ProcessAsync` |
| `msghatch.messages.stored` | Counter | `{message}` | `msghatch.message_type`, `msghatch.channel`, `msghatch.test` | 1 for each outgoing message that the pipeline stores |
| `msghatch.routes.failed` | Counter | `{route}` | `msghatch.message_type`, `msghatch.channel`, `error.type` | 1 for each route that the pipeline fails in steps 3 to 5 |
| `msghatch.process.duration` | Histogram | `s` | `msghatch.message_type`, `msghatch.test`, and `error.type` for a failed event | The time of one process of an event |
| `msghatch.publish.tries` | Counter | `{try}` | `msghatch.message_type`, `msghatch.channel`, `msghatch.outcome`, `msghatch.test` | 1 for each recorded publish try |
| `msghatch.messages.failed` | Counter | `{message}` | `msghatch.message_type`, `msghatch.channel`, `error.type` | 1 for each outgoing message that gets the status `Failed` |
| `msghatch.send.duration` | Histogram | `s` | `msghatch.channel`, `msghatch.outcome` | The time of one call of a transport |
| `msghatch.delivery.delay` | Histogram | `s` | `msghatch.message_type`, `msghatch.channel` | The finish time of a `Succeeded` try, minus the `CreatedAtUtc` of its message |
| `msghatch.queue.length` | Observable gauge | `{message}` | `msghatch.queue`: `inbox`, or the channel | The messages in the work queue of each dispatch loop of this process |
| `msghatch.channel.down` | Observable gauge | `1` | `msghatch.channel` | 1 when the last claim of the loop of the channel returned a down state, and 0 otherwise |

Rules of the metrics:
- All times come from `TimeProvider`: a duration from `GetTimestamp` and `GetElapsedTime`, and the delivery delay from the times of the try (section 4.14).
- The callbacks of the observable gauges read only the state of the loops in memory. They never call a store.
- The OpenTelemetry .NET SDK keeps at most 2,000 tag combinations for each metric by default. From version 1.10.0, it adds each measurement above this limit to one series with the tag `otel.metric.overflow=true`. The totals then stay correct, but the values for each tag are lost. So each tag of an instrument has few values, and the tenant is not a tag (D133).
- The duration histograms give their bucket boundaries as `InstrumentAdvice<double>`: 0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, and 10 seconds. The delivery delay uses 0.1, 0.5, 1, 5, 15, 60, 300, 900, 3600, 21600, and 86400 seconds. `System.Diagnostics.DiagnosticSource` 9.0.0 added this advice, and the OpenTelemetry .NET SDK uses it from version 1.10.0. So a host needs that SDK version or a later one.

Checked facts (2026-10-31). These external facts carry the design of this section. A phase that pins a newer version reads the source again:

| Fact | Source |
|---|---|
| The messaging operation types include `process` and `send`. A send span with a separate creation context has the kind `CLIENT` and links to that context. The conventions have the status Development. | [Messaging spans](https://github.com/open-telemetry/semantic-conventions/blob/main/docs/messaging/messaging-spans.md) |
| `AddMetrics` is in the package `Microsoft.Extensions.Diagnostics`, and a host registers `IMeterFactory`. | [AddMetrics](https://learn.microsoft.com/en-us/dotnet/api/microsoft.extensions.dependencyinjection.metricsserviceextensions.addmetrics), [Metrics instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation) |
| `MetricCollector<T>` of `Microsoft.Extensions.Diagnostics.Testing` collects the measurements of one instrument, also through an `IMeterFactory`. | [Metrics.Testing namespace](https://learn.microsoft.com/en-nz/dotnet/api/microsoft.extensions.diagnostics.metrics.testing?view=net-11.0-pp) |
| `InstrumentAdvice<T>` came with `System.Diagnostics.DiagnosticSource` 9.0.0. The OpenTelemetry .NET SDK uses its bucket boundaries from version 1.10.0. | [Metrics instrumentation](https://learn.microsoft.com/en-us/dotnet/core/diagnostics/metrics-instrumentation) |
| The default limit is 2,000 tag combinations for each metric. From SDK 1.10.0, the SDK always adds the measurements above it to a series with `otel.metric.overflow=true`. | [OpenTelemetry .NET metrics](https://github.com/open-telemetry/opentelemetry-dotnet/blob/main/docs/metrics/README.md), [Metrics SDK specification](https://opentelemetry.io/docs/specs/otel/metrics/sdk/) |
| Wolverine collects its tracing through `AddSource("Wolverine")`, and its meters through `AddMeter("Wolverine*")`. | [Wolverine logging and instrumentation](https://github.com/jasperfx/wolverine/blob/main/docs/guide/logging.md) |
| Checked 2026-11-03 for `WolverineFx` 6.61.0. Each `SendAsync` and each `PublishAsync` writes the id of `Activity.Current` into `Envelope.ParentId`, also with no listener on the source `Wolverine`. The envelope mappers write `ParentId` into the header `parent-id`. The `send` span of Wolverine, of kind `Producer`, starts with `ParentId` as its parent, and does not change `ParentId`. So the header holds the W3C id of the MsgHatch `send` span. Wolverine writes no `traceparent` header, except in its CloudEvents interop. A run of 6.61.0 with no listener on `Wolverine` gave the id of the `send` span in `ParentId`, with `PublishAsync` and with `SendAsync`. | [MessageBus.cs](https://github.com/jasperfx/wolverine/blob/29806dd8a8180d165878a6dcb8f096abd782dc84/src/Wolverine/Runtime/MessageBus.cs), [EnvelopeMapper.cs](https://github.com/jasperfx/wolverine/blob/29806dd8a8180d165878a6dcb8f096abd782dc84/src/Wolverine/Transports/EnvelopeMapper.cs), [EnvelopeConstants.cs](https://github.com/jasperfx/wolverine/blob/29806dd8a8180d165878a6dcb8f096abd782dc84/src/Wolverine/EnvelopeConstants.cs), [WolverineTracing.cs](https://github.com/jasperfx/wolverine/blob/29806dd8a8180d165878a6dcb8f096abd782dc84/src/Wolverine/Runtime/WolverineTracing.cs) |
| MassTransit makes its send span from its `ActivitySource` `MassTransit`, as a child of `Activity.Current`. It writes the context into `MT-Activity-Id` and `MT-Activity-Correlation-Context`. With no listener, it makes no span. | [LogContextActivityExtensions.cs](https://github.com/masstransit/masstransit/blob/develop/src/MassTransit/Logging/Diagnostics/LogContextActivityExtensions.cs), [Discussion #5418](https://github.com/MassTransit/MassTransit/discussions/5418) |

### 4.22 Template administration

**Confirmed rule (D217 to D220):** an operator lists, reads, writes, and deletes the templates of a writable store, with no build and no deployment. The engine of the payload type checks each written text before the store keeps it, so a bad text gives `Errors.TemplateInvalid`, and the store keeps the old text.

`ITemplateQueries` and `ITemplateCommands` in `MsgHatch.Templates.Management` are the C# API (D222, D231). The queries have `GetTemplatesAsync` and `GetTemplateAsync`, and the commands have `SaveTemplateAsync` and `DeleteTemplateAsync`. `TemplateManager` implements both interfaces. Each template setup method and `AddTemplateEngine` register them one time, as tenant services (D126). The services of `TenantKey.None` use the shared template store, and the services of a tenant use only the template store of that tenant. Actions:
- `GetTemplatesAsync(TemplateQuery)` calls `ITemplateWriter.ListAsync` of the store. The manager sets `VersionText` of each entry, of a read, and of a save with `RenderVersion` of the store (D267).
- `GetTemplateAsync(TemplateLookup)` calls `ITemplateStore.FindAsync` with the exact lookup, with no fallback, so it also reads the embedded store. A miss gives `Errors.NotFound`.
- `SaveTemplateAsync(TemplateLookup, text, expectedVersion)` checks the text with the engine of each payload type whose registration of the message type declares the part and the kind (D219, D220). It uses the engines of its own key. It calls `ITemplateWriter.SaveAsync` only after all checks pass.
- `DeleteTemplateAsync(TemplateLookup)` calls `ITemplateWriter.DeleteAsync`. It accepts each part, also a part that no registration declares, so an operator can remove an old file.
- `GetTemplatePartsAsync(MessageType?)` gives `TemplatePartInfo(Type, Part, Engines, HasSample)` for each part and kind that a registration of the message type declares, or of each message type with no filter (D220, D240). `Engines` holds the engine name of each registration that reads the part, each name one time, in the order of the registrations. `HasSample` is true when a registration that reads the part has a sample payload (D254). A message type with no registration gives an empty list.
- `CheckTemplateAsync(TemplateLookup, text)` checks a draft with the engine of each registration that reads the part and the kind, as a save does, and saves nothing (D241). It gives the issues of all engines, or an empty list for a text that passes. A save with an issue gives `Errors.TemplateInvalid` with the message of the first issue.
- `PreviewTemplateAsync(TemplateLookup, text)` renders a draft with the engine and the sample payload of each registration that reads the part and the kind, and saves nothing (D254 to D256). It gives `TemplatePreview(Registration, Text)` for each such registration with a sample, in the order of the registrations, or an empty list for a part with no sample. It runs the same checks as a check of a draft, so a message type with no registration gives `Errors.NotFound`, and a part that no registration declares gives `Errors.ValidationFailed`. A text that does not parse or render gives `Errors.TemplateInvalid`. It is a query, because it changes nothing.
Validation (D234). The public `TemplateLookupRules` runs the rules that need no state first: `Check(TemplateLookup)` for the scope, the part, and a null or empty language (D246), `Check(TemplateQuery)` for the scope of a list, and `CheckText(string)` for the length of a text. Then the queries and the commands check the language, which needs the culture data of the machine, and then the checks that need the store or the engines run. So a request that breaks a rule and another check gives the error of the rule, for example an unknown language with a part that is not a name gives the error of the part. A null query or text gives an `ArgumentNullException`. Validation gives `Errors.ValidationFailed`, and calls no store, in these cases:
- The language is empty, or is not the name of a predefined culture of `CultureInfo.GetCultureInfo(name, predefinedOnly: true)`. So `HU`, `hu-hu`, and `en-US-x-test` are invalid, because the name of their culture is `hu`, `hu-HU`, and `en-US`. A send looks for the file of the language of its route as it is written, so a file of another spelling is never found on a file system that knows the case of a name.
- The scope is not a valid `CompanyId`, or its folder name of `ScopeFolders.FolderOf` has more than 255 characters, the limit of a folder name on Windows.
- The part is not a name of ASCII letters, digits, and `_` that starts with a letter.
- A save or a check has a part and a kind that no registration of the message type declares (D220).
- A save or a check has a text with more than 1,000,000 characters.
- The store of the key is not an `ITemplateWriter`, for a list, a save, or a delete.

A save or a check of a message type with no registration of a template engine gives `Errors.NotFound`. A key with no template store gives `Errors.NotFound`. The queries and the commands have a logging wrapper each, also for the in-process default, so a host with an HTTP client logs each call too (D244, section 4.15). The store wrapper logs each change, and the engine wrapper logs each check.

HTTP endpoints (D224). `MapMsgHatchTemplates(this IEndpointRouteBuilder routes, string prefix = "/templates")` in `MsgHatch.Templates.AspNetCore` maps these minimal API endpoints, and returns the `RouteGroupBuilder`. The host adds the authorization, for example `.RequireAuthorization("TemplateAdmin")` (D225). The library adds no authorization.

| Method | Path | Body | Success |
|---|---|---|---|
| GET | `{prefix}?messageType=&companyId=` | none | 200 with the entries: `messageType`, `language`, `companyId`, `part`, `kind`, and `version` |
| GET | `{prefix}/{messageType}/{language}/{part}/{kind}?companyId=` | none | 200 with the entry and its `text` |
| PUT | `{prefix}/{messageType}/{language}/{part}/{kind}?companyId=` | `text` and `version` | 200 with the saved entry |
| DELETE | `{prefix}/{messageType}/{language}/{part}/{kind}?companyId=` | none | 204 |
| GET | `{prefix}/parts?messageType=` | none | 200 with the declared parts: `messageType`, `part`, `kind`, and `engines` (D240) |
| POST | `{prefix}/{messageType}/{language}/{part}/{kind}/check?companyId=` | `text` | 200 with the issues: `message`, `line`, and `column` (D241). The check is a query, because it changes nothing. |
| POST | `{prefix}/{messageType}/{language}/{part}/{kind}/preview?companyId=` | `text` | 200 with the previews: `registration` and `text` (D256). The preview is a query, because it changes nothing. |

- Each route takes the optional query parameter `tenantId`. The endpoint resolves `ITemplateQueries` or `ITemplateCommands` with `TenantKey.For(tenantId)`, and with `TenantKey.None` when the parameter is missing (D126).
- An endpoint checks only the values that need a type: the message type, the kind (`PlainText` or `Html`, in this case), and the tenant id. An invalid value of them gives 400, and calls no manager. The manager checks the language, the part, and the company id, so these rules have one place.
- The bodies and their JSON context `TemplateEndpointJson` are public types of `MsgHatch.Templates.Api.Contracts` (D227, D228), with public mappings in both directions: `TemplateEntryResponse`, `TemplateResponse`, `TemplateIssueResponse`, `TemplatePartResponse` with `hasSample`, and `TemplatePreviewResponse`. The bodies of a check and of a preview are `TemplateCheckRequest` and `TemplatePreviewRequest`, each with the text. `TemplateResponse.ToSource()` gives a key with a stable id of the lookup, because the API has no template id (D224). The route paths are the internal class `TemplateApiRoutes` in `src/Shared`. The endpoints read and write each body with `TemplateEndpointJson`: camelCase names, enum values as strings, and the required properties `text` and `version`, which are not nullable. A `PUT`, a check, or a preview with a content type that is not JSON gives 415 (D156). Invalid JSON, a missing required property, and a null `text` give 400.
- Each error is problem details with the status of `ErrorProblems.StatusCodeOf`, the error name as the title, the error message as the detail, and the extension member `errorType` (D233), as for the subscription endpoints (section 4.17). So `Errors.TemplateInvalid` gives 422, and `Errors.Unavailable` gives 503.
- A response never has the template id, because the id of the file store is a path on the host.

### 4.23 HTTP API clients

**Confirmed rule (D227, D231, D236):** a host calls the subscription and template management APIs of another host through the query and command interfaces of D231. `MsgHatch.Subscriptions.Api.Client` and `MsgHatch.Templates.Api.Client` implement these interfaces over HTTP. `MsgHatch.Api.Client`, the client of the core APIs, implements the registry queries and commands of section 4.20 and the delivery queries and commands of section 4.19 in the same way (D271, D294). A consumer cannot see which implementation it uses: the same input gives the same result and the same error (D233, D234). A host can read over HTTP and write in process, or the reverse, or use one implementation for both.

Generator (D236). `HttpApiClientGenerator` of `MsgHatch.SourceGeneration` is the second incremental generator of the package:
- It adds the internal attributes `HttpApi`, `HttpCall`, and `HttpBody` of the namespace `MsgHatch.Http` with `RegisterPostInitializationOutput`. They have `Microsoft.CodeAnalysis.EmbeddedAttribute`, so each compilation with the generator has its own copy, and no other assembly sees them, also with `InternalsVisibleTo`.
- Its input is an internal interface with `[HttpApi(typeof(TJsonContext))]`. Each method has `[HttpCall(method, route)]` with `GET`, `POST`, `PUT`, `DELETE`, or `PATCH`, and a route of `src/Shared`, for example `SubscriptionApiRoutes.Subscription`.
- A parameter whose name is in the route is a route value. A constraint, for example `{id:guid}`, is not part of the name. One parameter with `[HttpBody]` is the JSON body. Each other parameter, except the last `CancellationToken`, is a query value, which a request leaves out when it is null.
- A route value or a query value is a string, a `Guid`, a number, a `bool`, an enum, or a `DateTime`. A query value can also be a nullable value type, or an `IReadOnlyList<T>` with `T` one of these types (D293). The request escapes each value, writes a number in the invariant culture, a `bool` as `true` or `false`, an enum with its name, and a `DateTime` as the ISO 8601 text of D57, which is the format `O`. A list gives one `name=value` pair for each item, and no pair for null or an empty list.
- A method returns `Task<ErrorOr<TResponse>>`, or `Task<Error?>` for a success with no body.
- The generator writes an internal sealed class with the name of the interface with no `I`, for example `TemplateApi` for `ITemplateApi`. Its constructor takes the `HttpClient` and the prefix of the route group. The relative URI is the prefix and the route, so a base address with a path, for example `https://api.example.com/admin/`, keeps its path.
- The class writes each body and reads each success body with the `JsonTypeInfo<T>` property of the JSON context, so it uses no reflection at run time and no proxy (D237). The generator finds the property in the metadata of the context, so the context comes from a referenced assembly, for example a contracts package. In the same compilation, the generator cannot see the properties that the System.Text.Json generator writes.
- An error status gives `ErrorProblems.ErrorOf(status, errorType, detail)` with the members `errorType` and `detail` of the problem body (D233). A body that is not a JSON object, or an `errorType` that does not parse, gives the type of the status only. `HttpRequestException` and a timeout of the `HttpClient` give `Errors.Unavailable` with the exception. A cancelled token gives `Errors.OperationCancelled`. A success body that does not parse, or that is the JSON null, gives `Errors.SerializationFailed`. So no method throws for an error status, a network error, or a timeout.

The generator gives an error diagnostic, and writes no class, for an interface that it cannot write:

| Id | Cause |
|---|---|
| `MSGHATCH002` | The interface is generic or nested. |
| `MSGHATCH003` | The JSON context is not a `JsonSerializerContext`. |
| `MSGHATCH004` | A member is not a method with `HttpCall`, for example a property. |
| `MSGHATCH005` | The HTTP method is not `GET`, `POST`, `PUT`, `DELETE`, or `PATCH`. |
| `MSGHATCH006` | A route value has no parameter with its name. |
| `MSGHATCH007` | A method has a second parameter with `HttpBody`. |
| `MSGHATCH008` | A route value or a query value has a type that the generator cannot write, for example a list of an unsupported type or a list of nullable values, a route value is nullable or a list, or a `CancellationToken` is not the last parameter. |
| `MSGHATCH009` | A method does not return `Task<ErrorOr<TResponse>>` or `Task<Error?>`. |
| `MSGHATCH010` | A body type or a response type has no `JsonTypeInfo` property in the JSON context. |

Client packages (D227). Each package has only the package `Microsoft.Extensions.Http`, and no ASP.NET Core reference, so a worker, a console host, or Blazor WebAssembly can use it. Its public types are only its options, their source-generated validator, and its setup class, and no public member has a type of `System.Net.Http`. Each package sets `IsAotCompatible` (D237).

| Package | Interfaces | Setup methods | Options section under the root (D328) | Default prefix |
|---|---|---|---|---|
| `MsgHatch.Templates.Api.Client` | `ITemplateQueries`, `ITemplateCommands` | `UseTemplateApiClient`, `UseTemplateQueriesApiClient`, `UseTemplateCommandsApiClient` | `{root}:TemplateApiClient` | `/templates` |
| `MsgHatch.Subscriptions.Api.Client` | `ISubscriptionQueries`, `IRoutingVerifier`, `ISubscriptionCommands` | `UseSubscriptionApiClient`, `UseSubscriptionQueriesApiClient` (the queries and the verifier, D232), `UseSubscriptionCommandsApiClient` | `{root}:SubscriptionApiClient` | `/subscriptions` |
| `MsgHatch.Api.Client` | `IRegistryQueries`, `IRegistryCommands` | `UseRegistryApiClient` | `{root}:RegistryApiClient` | `/registry` |
| `MsgHatch.Api.Client` | `IDeliveryQueries`, `IDeliveryCommands` | `UseDeliveryApiClient` | `{root}:DeliveryApiClient` | `/delivery` |

- The options are `BaseAddress`, a required absolute URI with no default, `Prefix`, with the default prefix of `MapMsgHatchTemplates`, `MapMsgHatchSubscriptions`, `MapMsgHatchRegistry`, or `MapMsgHatchDelivery`, and `Timeout`, from 1 second to 10 minutes, with the default 30 seconds. A value from code wins over the configuration, and an options error stops the start of the host.
- Each setup method takes `Action<TOptions>? configure = null` and `Action<IHttpClientBuilder>? configureHttp = null`, and has an overload with `Uri baseAddress`. Each interface has its own named HTTP client, whose name stays internal. The host adds its handlers with `configureHttp`, for example a handler that adds a token.
- Each setup method calls the selection methods of D231 with its own name, so a second selection of one interface gives the setup error "… select an implementation of …". The selection adds the logging wrapper of the interface (D244), which logs each call. A host with `MsgHatchRoles.None` that only calls the clients starts (D230).
- The internal adapters, for example `HttpTemplateQueries`, run the rules of D234 first: `TemplateLookupRules`, `SubscriptionDraftRules`, `RoutingVerificationRules`, and `DeliveryRules` (D248, D292). So an input that breaks a rule gives the error of the rule with no HTTP call. Then an adapter maps the domain values to the bodies (D228), sends `tenantId` for the key of a tenant and no `tenantId` for `TenantKey.None`, calls the generated class, and maps the response back. A list with a null item, or a value that is not valid, gives `Errors.SerializationFailed`.
- `HttpTemplateQueries.PreviewTemplateAsync` runs `TemplateLookupRules.Check` and `CheckText` first, and then posts the text to the route `/preview` of the lookup (D256). The server renders the preview with its engines and its samples, so a client host needs no engine and no payload type. A preview with an empty registration or a null text gives `Errors.SerializationFailed`.
- `HttpRegistryQueries` runs no rule, because a `ChannelId` is valid by its type and the other queries have no input (D271). The history call sends the name of the channel, and gives `Errors.NotFound` for a channel that the server does not register (D278). `HttpRegistryCommands` sends the channel check, and a body with an error code and no error name, or the reverse, gives `Errors.SerializationFailed` (D283). From phase 35, it also sends the saves and the resets of the stored settings with `PUT` and `DELETE` (D284). A save runs `RegistryEntryRules` first, and gives the error of a rule with no HTTP call, with the same message as the server (D285). It sends the body of `ChannelEntryRequest.From` or `MessageTypeEntryRequest.From`, and maps the saved entry back with `ToEntry`. The registry routes have no `tenantId`, so the adapter sends none, and each tenant key gets the same values (D269). The registry routes write their errors with `ErrorProblems` (section 4.20), so the client gives the error type of the server, for example `Errors.Unavailable` for an error of the registry writer.
- `HttpDeliveryQueries` and `HttpDeliveryCommands` call the two route groups of section 4.19 under one prefix, so a host maps both groups with the same prefix (D294). They run `DeliveryRules` first, and give the error of a rule with no HTTP call (D292). The adapter of a tenant key sends its tenant as `tenantId` for the queries and the bulk actions. The pause, the resume, the delete of the test data, and the purge act on all tenants, so they send no `tenantId`. The filters of a page go as query values, with the repeated `ids` and the created and due times of D293, and the cursor as the text of `DeliveryApiRoutes`. A bulk action sends `InboxFilterRequest.From` or `OutboxFilterRequest.From`. The content methods read the body as a `JsonElement`, and give its raw text, so the text is the text of the server. A content body that is not a JSON object, a next cursor that does not parse, or a page with no items gives `Errors.SerializationFailed`. The descriptor of a channel state has the settings type `ChannelSettings`, because the body has no settings type. The routes write their errors with `ErrorProblems`, so the client gives the error type of the server, for example `Errors.Unavailable` for an error of a store.
- The template API has no template id (D224), so `TemplateSource.Key.Id` of a read is a stable id of the lookup. A caller reads it as opaque, and sends back only the version.
- The subscription client calls the routes with no `/me`, because a `/me` route narrows each query to the signed-in user, and a browser calls it (D154). It reads the settings of a channel with the entry `Settings:{channel}` of the registry of its host, or gives `JsonChannelSettings` (D229), and a request names the settings type of typed settings (D247). A row of a verification report has its trace only when the host registers the trace type `Trace:Subscription`.
- A create is not idempotent. So a host does not retry a `POST`, for example with a retry handler of `configureHttp`.
- `IHttpClientFactory` of .NET 10 logs the URI of each request with the query as `*`, for example `Start processing HTTP request GET http://localhost/templates?*`. So the log of the client holds no user id and no company id. The path holds only the message type, the language, the part, the kind, or the id of a subscription. A host that sets the switch `System.Net.Http.DisableUriRedaction` gets the query in its log.

Setup of a Blazor admin UI (D235). The admin UI uses the clients for all reads and writes, so the API host runs the validation, the engine checks, and the cache invalidation in one place:

```csharp
builder.Services.AddTransient<TokenHandler>();
builder.Services.AddMsgHatch(o => o.Roles = MsgHatchRoles.None)
    .UseSubscriptionApiClient(configureHttp: http => http.AddHttpMessageHandler<TokenHandler>())
    .UseTemplateApiClient(configureHttp: http => http.AddHttpMessageHandler<TokenHandler>())
    .UseRegistryApiClient(configureHttp: http => http.AddHttpMessageHandler<TokenHandler>());
```

The base addresses come from the sections `SubscriptionApiClient`, `TemplateApiClient`, and `RegistryApiClient` under the root of `AddMsgHatch`, by default `MsgHatch:SubscriptionApiClient`, `MsgHatch:TemplateApiClient`, and `MsgHatch:RegistryApiClient`. In Interactive Server mode, `IHttpClientFactory` makes the handlers in a DI scope of its own, and not in the scope of the circuit. So `TokenHandler` reads the token of the signed-in user through a circuit services accessor: a `CircuitHandler` stores the services of the circuit in an `AsyncLocal` for each inbound activity, and the handler reads them there, as the ASP.NET Core documentation "Access server-side Blazor services from a different DI scope" shows. In WebAssembly, the handler of the authentication library of the host adds the token. Section 4.24 describes the admin UIs.

### 4.24 Admin UIs

**Confirmed rule (D235, D239 to D242, D252, D257, D259 to D267, D272 to D279, D283, D287, D295 to D298):** MsgHatch ships the list, view, and edit pages of the subscriptions and the templates, the pages of the registry, which change the state of a channel with its check (D283), its pause, and its resume (D297), and the stored settings of a channel or a message type (D287), and the delivery pages of the traffic, the outbox, and the inbox (D295, D296, D298), as Razor class libraries. A host adds them in its setup, selects the HTTP clients or the in-process implementations of D231, and adds its own authorization. The text of a template has the editor of its engine, and the Liquid editor is a package of its own.

| Package | References | Content |
|---|---|---|
| `MsgHatch.AdminUI` | `MsgHatch.Api.Contracts`, `Microsoft.AspNetCore.Components.Web` | The parts that both admin UIs use: `AdminListPage`, `AdminEditPage`, `AdminList<TItem>` with the states loading, error, and empty and the parameter `Pager`, `AdminPager`, `AdminActions`, `AdminDialog`, `DeleteDialog`, `ErrorAlert`, and `AdminErrors.TextOf`. The registry pages `ChannelListPage`, `ChannelViewPage`, `ChannelEditPage`, `MessageTypeListPage`, and `MessageTypeEditPage`, the fields of a stored entry `RegistryEntryFields`, the channel parts `ChannelStateBadge` and `ChannelHistoryDialog`, the column extension point `IMessageTypeColumn` with `MessageTypeCellItem` and `AddMessageTypeColumn<TColumn>`, `RegistryAdminRoutes`, the delivery pages `OutboxListPage`, `OutgoingMessagePage`, `InboxListPage`, `ReceivedMessagePage`, and `TrafficPage`, the delivery parts `CursorPager`, `DeliveryStatusBadge`, `DeliveryBulkActions`, `DeliveryResultAlert`, `MessageContentPanel`, and `ChannelPauseDialog`, `DeliveryAdminRoutes`, and `AddMsgHatchAdminUI` (D272 to D279, D287, D295 to D298) |
| `MsgHatch.Templates.AdminUI` | `MsgHatch.Templates.Api.Contracts`, `MsgHatch.AdminUI` | `TemplateListPage`, `TemplateViewPage`, `TemplateEditPage`, the metadata line `TemplateMetadata`, the preview `TemplatePreviewPane`, the editor extension point `TemplateEditorBase` with `AddTemplateEditor<TEditor>`, `PlainTemplateEditor`, the HTML highlight `HtmlTokenizer` with `HighlightedTextArea` and its JavaScript module `highlightedTextArea.js`, `TemplateAdminRoutes`, and `AddMsgHatchTemplatesAdminUI` |
| `MsgHatch.Templates.Liquid.AdminUI` | `MsgHatch.Templates.AdminUI` | `LiquidTemplateEditor`, `LiquidTokenizer`, and `AddLiquidTemplateEditor` |
| `MsgHatch.Subscriptions.AdminUI` | `MsgHatch.Subscriptions.Api.Contracts`, `MsgHatch.AdminUI` | `SubscriptionListPage`, `SubscriptionEditPage`, `SubscriptionEditor`, `RoutingVerificationPage`, the internal column "Subscriptions" of the message types page (D276), `SubscriptionAdminRoutes`, and `AddMsgHatchSubscriptionsAdminUI` |

Rules of the pages:
- A page resolves `ISubscriptionQueries`, `ISubscriptionCommands`, `IRoutingVerifier`, `ITemplateQueries`, `ITemplateCommands`, `IRegistryQueries`, `IRegistryCommands`, `IDeliveryQueries`, or `IDeliveryCommands` of `TenantKey.None` through the `IServiceProvider` of its DI scope (D249, D269). It uses no other service of MsgHatch, except `IContentTypeRegistry` for the settings of a subscription. So the host selects the HTTP clients or the in-process implementations in its setup (D235). The one exception is `MessageTypeListPage`, which also resolves the registered `IMessageTypeColumn` services, and each column resolves only the query interfaces of `TenantKey.None` (D275). The channel pages, the delivery lists, and the traffic page also read `TimeProvider`, which `AddMsgHatchAdminUI` adds when the host has none.
- A page sets no render mode, so the host selects Interactive Server or WebAssembly. A page uses the CSS classes of Bootstrap 5, which the host loads, and no component library.
- A page handles each failure as an `ErrorOr` value. `AdminErrors.TextOf` gives a fixed text for `Rejected` (the signed-in user has no permission), `Unavailable` (the API is not available), and `Conflict` (the page shows the stored version now), and the message of each other error, or the name of its type when it has no message. A list shows its error in place of its rows. An edit page shows the error of its load in place of the form. It shows the error of a save, a delete, or a reset in the middle area of its `AdminActions`, between the left and the right buttons, so the error stays on screen with them. `Conflict` loads the stored version again. A create loads no stored version, so the subscription editor gives the message of a `Conflict` of a create to the parameter `Text` of `ErrorAlert`, for example that the owner already has a subscription for the message type, the channel, and the recipient kind.
- An edit page keeps a draft of its edit, shows the pending changes and the alert of a successful save in the middle area of its `AdminActions` under the error of a save, and asks with `DeleteDialog` before a delete. Each field with a change that is not saved has the yellow border of the Bootstrap class `border-warning`, so the user sees which fields the pending changes name. `RegistryEntryFields` gets the names of the changed fields in its parameter `Changes`. The delete runs only after the user confirms it. The question shows the details of the stored item, what changes after the delete, and that the delete cannot be undone (D262). The parameter `Action` of `DeleteDialog`, with the default "Delete", is the title, the text of the button, and the noun of the warning, so a registry edit page asks "Reset" with "The reset cannot be undone." (D287).
- Each dialog uses `AdminDialog`, and each page and dialog puts its buttons into `AdminActions`: Cancel or Back on the bottom left, and the main action on the bottom right, with the main action last, for example Delete and then Save (D261). The optional area `Middle` between them takes the free width of the bar, puts its parts in a column, and its text wraps there. On a bar narrower than its content and the buttons, it goes to a line of its own. A dialog whose body has its own `AdminActions` gives `AdminDialog` no `Start` and no `End`, so the dialog has no footer.
- A list pages its rows in the page, 25 rows on one page, because the APIs give each list in full. The delivery lists are the exception: they read the pages of the server, and give `AdminList` the pager Newer and Older (D295).
- A list page uses `AdminListPage` (D259): a header with the title and its links, a toolbar with the filters on the left and the actions on the right, and the list. `AdminList` puts its table in an area that scrolls by itself, with a sticky row of column names, and its pager under that area. A list page with a new item has the button "+ New …" on the right of the toolbar, and the button opens a dialog (D260). The filters are in a framed bar with small uppercase labels (D265). A filter applies when its value changes, with no button: a select at each change, and a text field 500 ms after the last character, so a word that the user types gives one query. A change of a select during the pause applies the text at once. Only the answer of the last query shows, so an earlier answer that comes late does not replace it. Each row of the template list and of the subscription list has Delete, with the question of D262 (D263).
- An edit page or a view page uses `AdminEditPage` (D264): a header, a body that scrolls by itself, and the bar of the actions under it, so the actions stay on screen.
- A version of a template shows as its `VersionText`, and as the number when no store rendered it (D267).
- A link of a page and its navigation use the address of the route builder with no leading `/`, so a host with a base path keeps its path.
- Each package sets `IsAotCompatible` (D237). Each setup method keeps the pages of its package in a trimmed host with `DynamicDependency` (D250).

The pages and their route builders:

| Address | Page | Route builder |
|---|---|---|
| `/msghatch/templates` | `TemplateListPage` | `TemplateAdminRoutes.List` |
| `/msghatch/templates/{messageType}/{language}/{part}/{kind}?companyId=` | `TemplateEditPage` | `TemplateAdminRoutes.Edit(TemplateLookup)` |
| `/msghatch/templates/{messageType}/{language}/{part}/{kind}/view?companyId=` | `TemplateViewPage` | `TemplateAdminRoutes.View(TemplateLookup)` |
| `/msghatch/subscriptions` | `SubscriptionListPage` | `SubscriptionAdminRoutes.List` |
| `/msghatch/subscriptions?new=true` | `SubscriptionListPage` with the dialog of a new subscription | `SubscriptionAdminRoutes.New` |
| `/msghatch/subscriptions/{id}` | `SubscriptionEditPage` | `SubscriptionAdminRoutes.Edit(Guid)` |
| `/msghatch/subscriptions?type={type}&level={level}` | `SubscriptionListPage` with the filters of a message type and a level (D276) | `SubscriptionAdminRoutes.ListOf(MessageType, SubscriptionLevel)` |
| `/msghatch/subscriptions/verification` | `RoutingVerificationPage` | `SubscriptionAdminRoutes.Verification` |
| `/msghatch/channels` | `ChannelListPage` | `RegistryAdminRoutes.Channels` |
| `/msghatch/channels/{channel}` | `ChannelViewPage` | `RegistryAdminRoutes.Channel(ChannelId)` |
| `/msghatch/channels/{channel}/edit` | `ChannelEditPage` | `RegistryAdminRoutes.ChannelEdit(ChannelId)` |
| `/msghatch/message-types` | `MessageTypeListPage` | `RegistryAdminRoutes.MessageTypes` |
| `/msghatch/message-types/{type}/edit` | `MessageTypeEditPage` | `RegistryAdminRoutes.MessageTypeEdit(MessageType)` |
| `/msghatch/traffic` | `TrafficPage` | `DeliveryAdminRoutes.Traffic` |
| `/msghatch/outbox?status=&type=&channel=&targetKind=&targetId=&source=&from=&to=&test=` | `OutboxListPage` | `DeliveryAdminRoutes.Outbox`, `DeliveryAdminRoutes.OutboxOf(...)` |
| `/msghatch/outbox/{id}` | `OutgoingMessagePage` | `DeliveryAdminRoutes.OutgoingMessage(Guid)` |
| `/msghatch/inbox?status=&type=&from=&to=&test=` | `InboxListPage` | `DeliveryAdminRoutes.Inbox`, `DeliveryAdminRoutes.InboxOf(...)` |
| `/msghatch/inbox/{id}` | `ReceivedMessagePage` | `DeliveryAdminRoutes.ReceivedMessage(Guid)` |

Template pages:
- The list has filters for the message type and the company, which go into `TemplateQuery`. The message types of the filter come from `GetTemplatePartsAsync`. The filters for the language, the part, and the kind apply to the loaded list in the page, because the lists have no filters and no paging on the server. The part filter offers the declared parts, the kind filter offers each `TemplatePartKind`, and the language filter ignores the case. Each row has the buttons View and Edit (D257). The button "+ New template" opens a dialog with a declared part of `GetTemplatePartsAsync` (D220, D240), a language, and an optional company (D260). "Open the editor" checks the lookup with `TemplateLookupRules`, and opens the edit page, with the editor and the live preview. An error of the parts, or no declared part, shows in the dialog.
- The view page reads the parts of the message type and the template with no fallback. It shows the metadata line, the stored text in `HighlightedTextArea` with `ReadOnly`, so a user can select and copy it, and the preview of the stored text. An html part has the HTML highlight of D252. The page has the buttons Edit and Back. An error, also `NotFound`, shows its text and the link back to the list.
- The edit page reads the parts of the message type and the template with no fallback. `NotFound` gives a new template with the version 0. The page shows the metadata line, the editor of the part under the title "Editor", the preview of the draft under the title "Live preview", and the save, with the expected version of the last read or save. `TemplateInvalid` shows its message as an issue under the text, and `Conflict` loads the stored version again. A stored template has the delete.
- `TemplateMetadata` shows the message type, the language, the company, the part with its kind, and the version on one line of small labels and values (D257).

Preview (D254 to D257):
- On a wide screen, the text is on the left and the preview on the right. On a narrow screen, the preview is under the text. The two columns are the Bootstrap classes `col-lg-6` of one `row`.
- `TemplatePreviewPane` calls `PreviewTemplateAsync` of `TenantKey.None` for its lookup and its text. It calls for the first text at once, and for each later text 500 ms after the last change, through `TimeProvider`. A new change cancels the pause and the call before it, and the pane shows only the result of the last call. So the view page calls one time for the stored text, and the edit page calls for the stored text and then after each pause in typing.
- An html preview is an `iframe` with `sandbox=""` and `srcdoc`. The empty sandbox gives the frame an opaque origin and runs no script, so a script of a template never runs in the admin host. A text preview is a `pre`.
- A part with more than one registration with a sample gets a select of the registration. `HasSample` of the part decides with no call: a part with no sample shows "No sample data", and so does an empty list. An error of the preview shows its text in place of the preview, for example the message of `TemplateInvalid` for an open tag.
- `AddMsgHatchTemplatesAdminUI` registers `TimeProvider.System` when the host has no time provider, and keeps `TemplateViewPage` in a trimmed host with `DynamicDependency` (D250).

Editor extension point (D240):
- An editor is a component that derives from `TemplateEditorBase`, with the parameters `Lookup`, `Text`, `TextChanged`, `Issues`, `Disabled`, and `Changed`. `Changed` is true when the text is not the stored text, and the editor gives its text area the yellow border of a change. An issue has precedence, so a text with an issue keeps the style of an invalid field. `Issues` holds the issues of the page, for example the message of `TemplateInvalid` from a save.
- `AddTemplateEditor<TEditor>(this IServiceCollection services, string engine)` registers an editor for an engine name. Its type parameter has `DynamicallyAccessedMembers(All)`, so the trimming keeps each registered editor (D237). A later registration of one engine replaces an earlier one.
- A part that one engine reads, with a registered editor for that engine, gets that editor. Each other part gets `PlainTemplateEditor`, which shows each issue under the text. It shows an `html` part in `HighlightedTextArea` with the HTML spans (D252), and a `txt` part in a text area. A part that no registration declares has no engine, so it gets the plain editor.
- The page shows the editor with `DynamicComponent`. The analyzer gives IL2111 for each `DynamicComponent`, so the page suppresses it with its reason (D250).
- Before each save, the page calls `CanSaveAsync` of the editor, and saves only when it gives true (D251). The protected method `CheckAsync` calls `CheckTemplateAsync` of `TenantKey.None` for the lookup of the editor, so an editor package needs no other service.

HTML highlight (D252):
- `HtmlTokenizer.Tokenize` gives a span for each part of an HTML text, with no gap: `Text`, `Markup` (`<`, `</`, `<!`, `>`, `/>`, and `=`), `TagName`, `AttributeName`, `AttributeValue` with its quotes, `Comment` (`<!--` to `-->`), and `Entity` (for example `&amp;` or `&#160;`). It is no parser. A `<` starts a tag only before a letter, `/`, or `!`. A tag, a comment, or a value with no end runs to the end of the text.
- `HtmlTokenizer.CssClassOf` gives the CSS class of a span kind, for example `html-tagname`.
- `HighlightedTextArea` puts the text area over a `pre` with the segments of the text. A segment has its CSS class and an optional mark of an issue. The module `_content/MsgHatch.Templates.AdminUI/highlightedTextArea.js` keeps the scroll of the two layers in step. The component styles the HTML classes, and an engine package styles its own classes with `::deep`.

Liquid editor (D242):
- `AddLiquidTemplateEditor()` registers `LiquidTemplateEditor` for the engine `liquid`, which `LiquidTemplateEditor.EngineName` holds, and `TimeProvider.System` when the host has no time provider. A test checks that the name equals `LiquidTemplates.Engine.Name`, so the package needs no reference to Fluid.
- `LiquidTokenizer.Tokenize` gives a span for each part of the text: `Text`, `Delimiter` (`{{`, `}}`, `{%`, and `%}`, with their `-` mark), `TagName`, `FilterName` (a name after `|`), `Quoted`, `Number`, `Code` (each other part of a tag), `Comment`, and `Raw`. It is no parser.
- `LiquidTokenizer.FindIssues` gives an issue with the line and the column, from 1 as in Fluid, for an open `{{` or `{%`, and for each `{% liquid` tag, also in `raw` or `comment`, with the regular expression of the engine (D226). The editor marks them with no call.
- After the user stops typing for 500 ms, the editor calls `CheckTemplateAsync`. A new key cancels the pause and the call before it. The pause uses `TimeProvider`. `CanSaveAsync` calls the check at once, and gives false for a text with an issue. When the check did not run, the editor shows its error, and `CanSaveAsync` gives true, so that the save gives its error.
- The editor shows the text in `HighlightedTextArea` (D252). It marks the character at the line and the column of each issue with `mark`, and shows each message under the text. An issue with no position, for example an unknown filter, shows only its message. For an `html` part, the characters of each Liquid `Text` span get their HTML span kinds. The styles are scoped CSS, so the host loads its bundle `{assembly of the host}.styles.css`.

Subscription pages:
- The list has filters for the owner level and the message type, which go into `SubscriptionQuery`. The filters for the channel, the kind, and the mandatory flag apply to the loaded list in the page, because `SubscriptionQuery` has no such filter, and the lists have no filters and no paging on the server.
- The list starts with the filters of the query parameters `type` and `level`, which `SubscriptionAdminRoutes.ListOf` gives. A value that is not valid gives no filter for that value, and the query `new=true` still opens the dialog of a new subscription (D276).
- Phase 32 removes the page "Applied subscriptions" (`MessageTypesPage`) and its link (D274). `ISubscriptionQueries.GetMessageTypesAsync`, its route, and its client stay.
- The column "Subscriptions" of the message types page counts the stored subscriptions of each message type for each `SubscriptionLevel` with one `GetSubscriptionsAsync(new SubscriptionQuery())`, and gives "Default N", "Company N", and "User N", also for 0. It reads the registered message types with `IRegistryQueries`, so each message type gets its counts. Each count links to `SubscriptionAdminRoutes.ListOf` of its message type and its level (D276).

Registry pages (D272, D273, D287):
- The pages call `IRegistryQueries` of `TenantKey.None`, and a page that needs the channels and the message types calls both queries. An error of either query is the error of the page. The check and the edit pages also call `IRegistryCommands` of `TenantKey.None` (D283, D284). Until phase 34, the pages were read-only, except the check.
- The channel list shows the id, the name, the features as Bootstrap badges, the enabled flag as "Yes" or "No", the number of message types of each channel, the state, the start of the state, the up time, the pending messages, the pending test messages, and the oldest due time. Each row has the buttons Pause or Resume, History, and Check in a first row, and View and Edit in a second row (D279, D283, D287, D297).
- The up time is the part of the last day, the last week, and the last 30 days in which the channel was up. The list reads `GetChannelHistoryAsync` of each channel, one call for each channel at each read of the list. `ChannelTimeline` counts the down time of each window in ticks from the state rows, with the state at the start of the window from the newest row at or before it, as the bar of `ChannelHistoryDialog` does. The percent is cut down to two decimals, so "100 %" shows only with no down time. An error of a history read shows above the list, and the up time of that channel stays empty.
- Check calls `IRegistryCommands.CheckChannelAsync` of `TenantKey.None`, disables the Check buttons until the result, and reads the channels again. An alert above the list shows "{channel}: healthy." or "{channel}: {error name}.", and then "The channel is up again.", "The channel is down now.", or "The state did not change.". A check with no error has `alert-success`, a failed check `alert-warning`, and an error of the command the text of `AdminErrors` with `alert-danger`.
- `ChannelStateBadge` shows "Up" with `text-bg-success`, or "Down" with `text-bg-danger` and the reason: the error name for `Errors`, or "Paused" and the note for `Operator`. "Since" is the start of `Current` in UTC and the time since then from `TimeProvider`, with the previous state and its duration, for example "2026-11-18 16:13 UTC (17 h 2 min), after Down for 4 min", or "No down state recorded" for a channel with no row.
- `ChannelHistoryDialog` shows a bar of the last 30 days with `progress-stacked`, red with `bg-danger` where the channel was down and green with `bg-success` where it was up, and the rows newest first: the start, the state, the reason, the error name, the note, and the previous state with its duration. The row that was current at the start of the window gives the state at the start of the bar, and with no such row the channel was up. An error shows in the dialog, and Close is on the left.
- In the `Development` environment, `ExampleApp` writes an example history at its start with `ExampleStartup.SeedChannelHistoryAsync`, so that the dialog has data (D288). It writes past rows through `SetChannelDownAsync` and `SetChannelUpAsync` of the outbox dispatch store, only for a channel whose `Current` is null. Email gets a down state for errors 40 days ago for 2 hours, before the window, a down state for errors 12 days ago for 45 minutes, a pause 5 days ago for 3 hours with the note "Example maintenance", and a down state for errors 26 hours ago for 7 minutes. SignalR gets a down state for errors 20 days ago for 6 hours, and Broker gets no row. Each down state for errors has the error `Unavailable`, and each down state ends with an `Up` row. So each channel is up after the seed, and a second start adds no row.
- The channel view page shows the id, the name, the description, the features, the enabled flag, and the revision of the stored entry. It lists the message types that register the channel, each with its display name and its state on the channel: "Enabled", "Message type disabled", or "Disabled on this channel". A disabled message type sends on no channel, so its state is "Message type disabled" also when the channel is in its `DisabledChannels`. The page also shows the fields State, Since, Pending, Pending test, and Oldest due, and has Back on the left, and Pause or Resume, History, and Edit on the right, with Edit as its main action (D279, D287, D297). An unknown channel shows the text of `NotFound` and the link back.
- The message types page shows each message type with its display name, its description, its enabled flag, and its channels. Each channel links to its view page, and a channel in `DisabledChannels` has the mark "disabled". Each column of `AddMessageTypeColumn` follows, in the order of registration, with each item of a cell as a badge, and an item with an address as a link. An error of a column shows above the list with its header, its cells stay empty, and the other columns still show (D275). Each row ends with Edit (D287).
- The edit pages (D287) use `AdminEditPage`, and read the item with the queries. `RegistryEntryFields` holds the fields of the stored entry: the display name, the description, and the enabled flag. The fields show the values of `Entry` (D286), so an empty text field means the value of the code, and a save sends null for it. With no stored value, the placeholder shows the value of the code. With a stored value, the placeholder is "An empty field gives the value of the code.".
- A help text under the enabled flag tells three facts: a change applies to the next events, the outbox still sends the messages that it holds, and another process applies the change within its refresh interval.
- The message type edit page also has one check box "Send on {channel}" for each channel of the message type. A cleared box puts its channel into `DisabledChannels`, in the order of the channels of the message type.
- The page shows the pending changes, for example "Changes that are not saved: Display name, Send on Sms.". A save sends the revision of the last read as the expected revision. A save shows "Saved." with the values of a second read. A `Conflict` reads the item again, and shows `AdminErrors.ConflictText`. Each other error shows its text, and the fields keep the draft.
- The actions are Back on the left, and Reset and Save on the right, with Save last (D261). Reset shows only for a stored entry, and asks with `DeleteDialog` and the action "Reset". The details name the stored values and the revision, and tell that a disabled item is enabled after the reset. A reset reads the item again, so the page shows the values of the code.
- `AddMsgHatchAdminUI` keeps the five registry pages and the five delivery pages in a trimmed host with `DynamicDependency` (D250). A host adds the assembly of `RegistryAdminRoutes` to its router.
- The channel list and the channel view page also call `IDeliveryQueries.GetChannelsAsync`, and match each `ChannelStatus` to its channel by the id (D297). An error of this query shows above the list or above the fields, and the registry columns and fields still show, with no pending counts. The button Pause shows for a channel whose `Current` is up or has no row, and Resume for a down channel, as `ChannelStateBadge` shows it. Pause opens `ChannelPauseDialog` with a note. The dialog checks the note with `DeliveryRules.CheckPause` before the call, so a note of more than 500 characters gives `ValidationFailed` in the dialog with no call. An empty note goes as null, and a note goes with no white space at its ends. An error of the pause shows in the dialog. Resume calls `ResumeChannelAsync`, and an error shows above the list. After a pause or a resume, the page reads the channels again, so the state shows the new row.
- The edit page has the owner level with its company id or user id, the message type, the kind, the channel, the settings, the recipients with the named users, one user id on each line, and the mandatory flag. The channel list has only the channels that the message type registers, and the stored channel. A change of the message type selects the first channel of the new type when the type does not register the selected channel. A new subscription starts with the first channel of the first message type and the settings `{}`. An opt-out has no settings, so for the kind `OptOut` the settings field is disabled, and the save sends no settings. It checks each id with `Create` before a call. An update sends the expected revision. The public component `SubscriptionEditor` holds the form, the save, and the delete, so the edit page and the dialog of a new subscription share them (D260). In the dialog, a create closes the dialog and loads the list again, and the left button is Cancel. On the edit page, the left button is Back. The query `new=true` of the list opens the dialog, and Cancel then gives the list address with no query.
- The page edits the settings of a channel as JSON text (D229). For a channel whose `ChannelDescriptor.SettingsType` is a settings type of the host, `SettingsJson.Read` reads the text with the entry `Settings:{channel}` of the content type registry. For a channel with the settings type `ChannelSettings`, the page sends the text as `JsonChannelSettings`, with no change. Text that is not JSON gives `Errors.ValidationFailed` with no call. The page shows a stored subscription with `SettingsJson.Write`, indented.
- The verification page sends the test users, one user id on each line, the languages, one on each line, and an optional message type to `IRoutingVerifier` (D163), and shows the totals and a row for each check.

Delivery pages (D295 to D298):
- The outbox list and the inbox list put their filters into `OutboxQuery` and `InboxQuery`, because an outbox can hold millions of messages. The outbox filters are the status, the message type, the channel, the target kind and id, the source message, the created times in UTC, and the test flag. The inbox filters are the status, the message type, the created times, and the test flag. A select, a time, and the test flag apply at once, and a text field 500 ms after the last character. A value that is not valid, for example a source message that is not an id, gives `ValidationFailed` in place of the rows, with no call. A tenant target with an id, or another target with no id, is not valid (D315).
- A list starts with the filters of the address of `DeliveryAdminRoutes.OutboxOf` or `InboxOf`. A value of the address goes into its field, and a value that is not valid gives `ValidationFailed`, so the user sees the field to correct. A time of the address is UTC to the second, as the value of an input `datetime-local`.
- A list reads 50 messages at a time. `AdminList` shows each item that it gets, and shows its parameter `Pager` in place of the page numbers, also under an empty page. `CursorPager` has Newer and Older. Older reads the page after the `Next` cursor of the server, and Newer goes back to the cursor of the page before, which `DeliveryListState` keeps. A change of a filter reads the first page. Only the answer of the last read shows.
- A row shows the created time, the status as a badge with the badge "Test" for a test message, the message type, the channel and the target of an outgoing message, the tries as "3 / 8", the last error, and View.
- `DeliveryBulkActions` has "Retry filtered" and "Cancel filtered". They are disabled when `DeliveryRules.CheckBulkAction` finds no filter, and the test flag is no filter (D292). Each one asks in `AdminDialog` first, with Back on the left. `DeliveryResultAlert` then shows "Matched N, changed M.", or the text of the error, and the list reads the shown page again. A change of a filter hides the result.
- The page of an outgoing message reads `GetOutgoingMessageAsync`, and shows the summary, the tries in a table, and the trace as JSON text with indents. `DeliveryTexts.IndentedJsonOf` writes the text with `Utf8JsonWriter`, with no reflection (D237), and keeps a text that is not JSON. The page of a received message reads a page of `InboxQuery` with its one id and the test messages, and gives `NotFound` for an empty page. A link of the outgoing page opens the received page of its source, and a link of the received page opens the outbox list with the filter of its source message, with the test messages for a test message.
- `MessageContentPanel` reads the content only after a click on "Show content", so a look at a message reads no personal data (D296). An error, for example `Rejected` for a token with no role `DeliveryContent`, shows only in the panel, and the rest of the page stays.
- Cancel and Retry of a message page act on the query of the one id with the test messages, and show the counts, or "The message did not change, because it is done, cancelled, leased, or expired." for no change. Then the page reads the message again. Back is on the left, and Cancel and Retry on the right.
- The traffic page reads `GetTrafficAsync` with the filters of `TrafficQuery`: the status, the created times, and the test flag. It shows the rows of the inbox and of the outbox, and the count of each row links to its list with the status, the message type, the channel, the times, and the test flag of the row. "Delete test data" asks with `DeleteDialog`. "Purge" opens a dialog with the finish time, by default the current time to the second, and the maximum count of each store, by default 1000. The dialog checks the maximum count with `DeliveryRules.CheckPurge` before the call, and an error shows in the dialog. After a delete or a purge, the page shows "Deleted N received messages and M outgoing messages.", and reads the traffic again.

Setup of a host:

```csharp
builder.Services.AddTransient<TokenHandler>();
builder.Services.AddMsgHatch(o => o.Roles = MsgHatchRoles.None)
    .UseSubscriptionApiClient(configureHttp: http => http.AddHttpMessageHandler<TokenHandler>())
    .UseTemplateApiClient(configureHttp: http => http.AddHttpMessageHandler<TokenHandler>())
    .UseRegistryApiClient(configureHttp: http => http.AddHttpMessageHandler<TokenHandler>())
    .UseDeliveryApiClient(configureHttp: http => http.AddHttpMessageHandler<TokenHandler>());
builder.Services.AddMsgHatchAdminUI().AddMsgHatchSubscriptionsAdminUI().AddMsgHatchTemplatesAdminUI().AddLiquidTemplateEditor();
builder.Services.AddRazorComponents().AddInteractiveServerComponents();

var app = builder.Build();
app.UseAntiforgery();
app.MapStaticAssets();
app.MapRazorComponents<App>().AddInteractiveServerRenderMode()
    .AddAdditionalAssemblies(typeof(SubscriptionAdminRoutes).Assembly, typeof(TemplateAdminRoutes).Assembly, typeof(RegistryAdminRoutes).Assembly)
    .RequireAuthorization("MsgHatchAdmin");
```

The `Router` of the host gets the same three assemblies in `AdditionalAssemblies`. The pages of `MsgHatch.AdminUI` and its shared parts with no route are in one assembly, and the router finds only the pages. The host loads Bootstrap 5 and its styles bundle in `App.razor`. So that a list page scrolls only its list (D259), the layout of the host is one column with the height of the window, for example `<div class="d-flex flex-column vh-100">`, and its main area fills the rest with `class="d-flex flex-column flex-grow-1"` and `style="min-height: 0; overflow-y: auto"`. In a layout with no fixed height, the page scrolls as a whole. The admin parts use only the color variables of Bootstrap, so the host selects the light or the dark mode with `data-bs-theme` (D266). A host that uses the in-process implementations calls `UseSubscriptions` and a template setup method in place of the clients, and needs no token handler.

Example (`examples/AdminUiExample`). An Interactive Server host of both admin UIs, the registry pages, the delivery pages, and the Liquid editor, with no message type and no store. It calls the APIs of `ExampleApp` at `http://localhost:5080/` with the prefixes `/api/subscriptions`, `/api/templates`, `/api/registry`, and `/api/delivery` of its `appsettings.json`. `ApiTokenHandler` adds the token of the option `AdminUiExample:ApiToken`, which is required, to each request. The token comes from the user secrets of the project, which git ignores, for example a token of `dotnet user-jwts create --project examples/ExampleApp --name 1 --role TemplateAdmin --role RegistryAdmin --role DeliveryAdmin --role DeliveryContent`, which also passes the policy of the subscription routes. A token with no role `RegistryAdmin` gives 403 on the registry pages (D170). A token with no role `DeliveryAdmin` gives 403 on the delivery pages and on the pending counts of the channel pages, and a token with no role `DeliveryContent` gives 403 only on the content of a message page (D165, D298). The menu has the links Channels, Message types, Subscriptions, Templates, Verification, Traffic, Outbox, and Inbox (D274, D298), and marks the link of the open page, also "Channels" on the view page and the edit page of a channel, "Message types" on the edit page of a message type, "Outbox" on the page of an outgoing message, and "Inbox" on the page of a received message. The host has no sign-in, listens only on `http://localhost:5090`, and loads Bootstrap 5.3.3 from a pinned CDN address with its integrity hash, so the repository holds no copy. Its layout has the height of the window (D259), and its script `theme.js` sets the light or the dark mode before the first paint, from the stored choice or the system setting, with the button "Light or dark" in the navigation bar (D266).

## 5. Repository and conventions

### 5.1 Layout

```
/
├── README.md
├── AGENTS.md
├── LICENSE
├── .gitignore
├── .editorconfig
├── global.json
├── Directory.Build.props
├── Directory.Packages.props
├── BannedSymbols.txt
├── nuget.config
├── dotnet-tools.json
├── coverage.ps1
├── coverage.runsettings
├── pack.ps1                                            (phase 41)
├── MsgHatch.slnx
├── .github/
│   └── workflows/
│       └── build.yml                                   (phase 41)
├── docs/
│   ├── design.md
│   ├── roadmap.md
│   ├── subscription-domain.md
│   └── stores.md                                       (phase 13)
├── src/
│   ├── GeneratedCodeSuppressions.cs                    (phase 1)
│   ├── Shared/                                         (internal sources that several projects compile, after phase 25)
│   │   ├── FileNames.cs                                (after phase 25)
│   │   ├── ApiResults.cs                               (phase 27, D236)
│   │   ├── ErrorProblems.cs                            (phase 26, D233)
│   │   ├── RegistryApiRoutes.cs                        (phase 31, D270)
│   │   ├── SubscriptionApiRoutes.cs                    (phase 26, D228)
│   │   └── TemplateApiRoutes.cs                        (phase 26, D228)
│   ├── MsgHatch.Domain/                                (phase 1)
│   ├── MsgHatch.SourceGeneration/                      (phase 3)
│   ├── MsgHatch.Channels.Email/                        (phase 3)
│   ├── MsgHatch.Channels.Email.AmazonSes/              (phase 4)
│   ├── MsgHatch.Channels.Email.Resend/                 (phase 4)
│   ├── MsgHatch.Channels.SignalR/                      (phase 5)
│   ├── MsgHatch.Channels.Broker/                       (phase 6)
│   ├── MsgHatch.Channels.Broker.Wolverine/             (phase 6)
│   ├── MsgHatch.Data.FileSystem/                       (phase 7)
│   ├── MsgHatch.Data.Sqlite/                           (phase 8)
│   ├── MsgHatch.Subscriptions/                         (phase 9)
│   ├── MsgHatch.Subscriptions.Data.FileSystem/         (phase 10)
│   ├── MsgHatch.Subscriptions.Data.Sqlite/             (phase 10)
│   ├── MsgHatch.Subscriptions.Caching.Hybrid/          (phase 11)
│   ├── MsgHatch.Subscriptions.Caching.FusionCache/     (phase 11)
│   ├── MsgHatch.Subscriptions.AspNetCore/              (phase 12)
│   ├── MsgHatch.AspNetCore/                            (phase 15)
│   ├── MsgHatch.Data.EFCore/                           (phase 18)
│   ├── MsgHatch.Subscriptions.Data.EFCore/             (phase 18)
│   ├── MsgHatch.Data.EFCore.SqlServer/                 (phase 48)
│   ├── MsgHatch.Data.EFCore.PostgreSql/                (phase 49)
│   ├── MsgHatch.Data.EFCore.MySql/                     (phase 52)
│   ├── MsgHatch.Subscriptions.Data.EFCore.SqlServer/   (phase 48)
│   ├── MsgHatch.Subscriptions.Data.EFCore.PostgreSql/  (phase 49)
│   ├── MsgHatch.Subscriptions.Data.EFCore.MySql/       (phase 52)
│   ├── MsgHatch.Data.MongoDb/                          (phase 19)
│   ├── MsgHatch.Subscriptions.Data.MongoDb/            (phase 19)
│   ├── MsgHatch.Dispatch.NamedPipes/                   (phase 21)
│   ├── MsgHatch.Templates/                             (phase 24)
│   ├── MsgHatch.Templates.Liquid/                      (phase 23, renamed in phase 24)
│   ├── MsgHatch.Templates.AspNetCore/                  (phase 25)
│   ├── MsgHatch.Api.Contracts/                         (phase 26)
│   ├── MsgHatch.Subscriptions.Api.Contracts/           (phase 26)
│   ├── MsgHatch.Templates.Api.Contracts/               (phase 26)
│   ├── MsgHatch.Templates.Api.Client/                  (phase 27)
│   ├── MsgHatch.Subscriptions.Api.Client/              (phase 27)
│   ├── MsgHatch.Api.Client/                            (phase 31, D271)
│   ├── MsgHatch.AdminUI/                               (phase 28)
│   ├── MsgHatch.Templates.AdminUI/                     (phase 28)
│   ├── MsgHatch.Templates.Liquid.AdminUI/              (phase 28)
│   └── MsgHatch.Subscriptions.AdminUI/                 (phase 28)
├── tests/                                                                   (section 5.5)
│   ├── Shared/ReadmeCheck.cs                                                 (phase 42, D311)
│   ├── MsgHatch.Domain.Tests.Unit/                                          (phase 1)
│   ├── MsgHatch.SourceGeneration.Tests.Unit/                                (phase 3)
│   ├── MsgHatch.Channels.Email.Tests.Unit/                                  (phase 3)
│   ├── MsgHatch.Channels.Email.AmazonSes.Tests.Unit/                        (phase 4)
│   ├── MsgHatch.Channels.Email.AmazonSes.Tests.Integration/                 (phase 4)
│   ├── MsgHatch.Channels.Email.Resend.Tests.Unit/                           (phase 4)
│   ├── MsgHatch.Channels.Email.Resend.Tests.Integration/                    (phase 4)
│   ├── MsgHatch.Channels.SignalR.Tests.Unit/                                (phase 5)
│   ├── MsgHatch.Channels.Broker.Tests.Unit/                                 (phase 6)
│   ├── MsgHatch.Channels.Broker.Wolverine.Tests.Unit/                       (phase 6)
│   ├── MsgHatch.Channels.Broker.Wolverine.Tests.Integration/                (phase 6)
│   ├── MsgHatch.Data.FileSystem.Tests.Unit/                                 (phase 7)
│   ├── MsgHatch.Data.Sqlite.Tests.Unit/                                     (phase 8)
│   ├── MsgHatch.Subscriptions.Tests.Unit/                                   (phase 9)
│   ├── MsgHatch.Subscriptions.Data.FileSystem.Tests.Unit/                   (phase 10)
│   ├── MsgHatch.Subscriptions.Data.Sqlite.Tests.Unit/                       (phase 10)
│   ├── MsgHatch.Subscriptions.Caching.Hybrid.Tests.Unit/                    (phase 11)
│   ├── MsgHatch.Subscriptions.Caching.FusionCache.Tests.Unit/               (phase 11)
│   ├── MsgHatch.Subscriptions.AspNetCore.Tests.Unit/                        (phase 12)
│   ├── ExampleApp.Tests.Unit/                                               (phase 13)
│   ├── MsgHatch.Domain.Tests.Performance/                                   (phase 13)
│   ├── MsgHatch.Channels.Email.Tests.Performance/                           (phase 13)
│   ├── MsgHatch.Subscriptions.Tests.Performance/                            (phase 13)
│   ├── MsgHatch.AspNetCore.Tests.Unit/                                      (phase 15)
│   ├── MsgHatch.Data.EFCore.Tests.Integration/                              (phase 18)
│   ├── MsgHatch.Subscriptions.Data.EFCore.Tests.Integration/                (phase 18)
│   ├── MsgHatch.Data.EFCore.Tests.Unit/                                     (phase 48)
│   ├── MsgHatch.Subscriptions.Data.EFCore.Tests.Unit/                       (phase 48)
│   ├── MsgHatch.Data.EFCore.SqlServer.Tests.Integration/                    (phase 48)
│   ├── MsgHatch.Data.EFCore.PostgreSql.Tests.Integration/                   (phase 49)
│   ├── MsgHatch.Data.EFCore.MySql.Tests.Integration/                        (phase 52)
│   ├── MsgHatch.Subscriptions.Data.EFCore.SqlServer.Tests.Integration/      (phase 48)
│   ├── MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Integration/     (phase 49)
│   ├── MsgHatch.Subscriptions.Data.EFCore.MySql.Tests.Integration/          (phase 52)
│   ├── SchemaMigrator.Tests.Integration/                                    (phase 48)
│   ├── MsgHatch.Data.MongoDb.Tests.Integration/                             (phase 19)
│   ├── MsgHatch.Subscriptions.Data.MongoDb.Tests.Integration/               (phase 19)
│   ├── MsgHatch.Dispatch.NamedPipes.Tests.Unit/                             (phase 21)
│   ├── WolverineExample.Consumer.Tests.Unit/                                (phase 21)
│   ├── WolverineExample.UserSimulator.Tests.Unit/                           (phase 21)
│   ├── MsgHatch.Templates.Tests.Unit/                                       (phase 24)
│   ├── MsgHatch.Templates.Liquid.Tests.Unit/                                (phase 23, renamed in phase 24)
│   ├── MsgHatch.Templates.AspNetCore.Tests.Unit/                            (phase 25)
│   ├── MsgHatch.Api.Contracts.Tests.Unit/                                   (phase 26, with src/Shared/ErrorProblems.cs)
│   ├── MsgHatch.Subscriptions.Api.Contracts.Tests.Unit/                     (phase 26)
│   ├── MsgHatch.Templates.Api.Contracts.Tests.Unit/                         (phase 26)
│   ├── MsgHatch.Templates.Api.Client.Tests.Unit/                            (phase 27)
│   ├── MsgHatch.Subscriptions.Api.Client.Tests.Unit/                        (phase 27)
│   ├── MsgHatch.Api.Client.Tests.Unit/                                      (phase 31)
│   ├── MsgHatch.AdminUI.Tests.Unit/                                         (phase 28)
│   ├── MsgHatch.Templates.AdminUI.Tests.Unit/                               (phase 28)
│   ├── MsgHatch.Templates.Liquid.AdminUI.Tests.Unit/                        (phase 28)
│   ├── MsgHatch.Subscriptions.AdminUI.Tests.Unit/                           (phase 28)
│   ├── AdminUiExample.Tests.Unit/                                           (phase 28)
│   └── SignalRDemo.Tests.Unit/                                              (phase 46)
└── examples/
    ├── ExampleApp/                                     (phase 13)
    ├── WolverineExample.Contracts/                     (phase 21)
    ├── WolverineExample.Publisher/                     (phase 21)
    ├── WolverineExample.Consumer/                      (phase 21)
    ├── WolverineExample.UserSimulator/                 (phase 21)
    ├── AdminUiExample/                                 (phase 28)
    ├── SignalRDemo/                                    (phase 46)
    └── SchemaMigrator/                                 (phase 48)
```

A phase creates a project only when it puts code into it. Each project is added to `MsgHatch.slnx` with `dotnet sln add`.

Feature folders of `MsgHatch.Domain` (D50): `Failures`, `Identity`, `Routing`, `Delivery`, `Persistence`, `Pipeline`, `Inbox`, `Outbox`, `Dispatch`, `Management`, `Telemetry`, `Time`, and `DependencyInjection`. Feature folders of `MsgHatch.Channels.Email`: the root for the channel types, and `DependencyInjection`. Feature folders of `MsgHatch.Templates`: the root for the template types and stores, `Placeholders`, `Management`, and `DependencyInjection`. Feature folders of `MsgHatch.Subscriptions`: the root for the subscription types, `Resolution`, `Management`, `Caching`, `Verification`, and `DependencyInjection`. Feature folders of `MsgHatch.Data.EFCore` (phase 50): the root for the stores, the options, and the context, `DependencyInjection`, `Messages`, `Registry`, `Model`, `Providers`, and `Migrations`. The other store packages have 4 to 12 files, so they stay flat.

### 5.2 Build files

`global.json`:

```json
{
  "sdk": { "version": "10.0.100", "rollForward": "latestFeature" }
}
```

`Directory.Build.props` sets these properties for all projects:
- `TargetFramework` `net10.0`. `MsgHatch.SourceGeneration` sets `netstandard2.0` in its own project file.
- `LangVersion` `latest`, `Nullable` `enable`, `ImplicitUsings` `enable`.
- `AnalysisLevel` `latest-recommended`.
- `VersionPrefix` `0.1.0`, the one version of all projects (D307, section 5.6).
- `EnforceCodeStyleInBuild` `true`, so that the rules of `.editorconfig` stop the build.
- A `PackageReference` to `Microsoft.CodeAnalysis.BannedApiAnalyzers` with `PrivateAssets="all"`, and `<AdditionalFiles Include="$(MSBuildThisFileDirectory)BannedSymbols.txt" />` (D32 and D63).
- For the projects under `src/`:
  - `TreatWarningsAsErrors` `true` (D51).
  - `GenerateDocumentationFile` `true`.
  - `EnableConfigurationBindingGenerator` `true` (D65).
- The packages of the API boundary set `IsAotCompatible` `true` in their own project files (D237): `MsgHatch.Domain`, `MsgHatch.Subscriptions`, `MsgHatch.Templates`, `MsgHatch.Api.Contracts`, `MsgHatch.Subscriptions.Api.Contracts`, `MsgHatch.Templates.Api.Contracts`, `MsgHatch.Subscriptions.AspNetCore`, `MsgHatch.Templates.AspNetCore`, the three client packages, and the four admin UI packages. So the trim, AOT, and single-file analyzers run in each build, and `TreatWarningsAsErrors` turns each warning into an error. The two `.AspNetCore` packages also set `EnableRequestDelegateGenerator` `true`, so each minimal API endpoint needs no code generation at run time.
  - A compile link to `src/GeneratedCodeSuppressions.cs` (D32).
  - `IsPackable` `true`. `MsgHatch.SourceGeneration` sets `IsPackable` `false` in its own project file (D308).
  - The package metadata (D308, section 5.6): `Authors` `Tamás Bilik`, `Copyright`, `RepositoryUrl` and `PackageProjectUrl` `https://github.com/biliktamas79/MsgHatch`, `PackageLicenseExpression` `Apache-2.0`, `IncludeSymbols` `true` with `SymbolPackageFormat` `snupkg`, `EnablePackageValidation` `true`, and `ContinuousIntegrationBuild` `true` when `GITHUB_ACTIONS` is `true`. Each project file sets its own `Description` and `PackageTags`. `PackageReadmeFile` `README.md` packs the `README.md` of the project into the root of the package (D310).
- For each project whose name ends with `.Tests.Unit`, `.Tests.Integration`, or `.Tests.Performance`:
  - `OutputType` `Exe`, and `IsTestingPlatformApplication` `false` (D52).
  - `NoWarn` for `CA1707`, because the test names have underscores.
  - `NoWarn` for `CA2012`, because the setup of a substitute calls a method that returns a `ValueTask` and does not await it.
- The unit and integration test projects also get the test packages of section 5.5, `RunSettingsFilePath` `coverage.runsettings` (D70), and the compile link of `tests/Shared/ReadmeCheck.cs` with the assembly metadata `MsgHatch.TestProjectDirectory` (D311).
- For all other projects, `IsPackable` `false`.

`Directory.Packages.props` sets `ManagePackageVersionsCentrally` `true` (D48) and `CentralPackageFloatingVersionsEnabled` `true` (D317). It lists one `PackageVersion` for each package in section 4.2. Each version floats within its major, for example `10.*`, so each restore takes the latest stable version of the major. A version with the major 0 floats within its minor, for example `0.15.*`. `dotnet pack` writes the resolved version as the lowest version of a dependency. `Microsoft.CodeAnalysis.BannedApiAnalyzers` has `3.*`, which gives 3.3.4, because the versions 4.14.0 to 5.6.0 do not report a banned indexer (D32). These packages keep an exact version, with the reason in the label of their item group:
- `Microsoft.CodeAnalysis.CSharp` 5.0.0 and `Microsoft.CodeAnalysis.Analyzers` 3.11.0 of the source generator. Roslyn 5.0.0 is the compiler of SDK 10.0.100, which is the minimum SDK in `global.json`. A compiler does not load a generator that references a newer Roslyn version.
- `RabbitMQ.Client` 7.2.2, the version that `WolverineFx.RabbitMQ` 6.65.1 needs. So `MsgHatch.Dispatch.RabbitMq` does not raise the client version of a host with Wolverine. `7.*` gives 7.3.0.

Each analyzer package at the latest version of its major references Roslyn 5.0.0 or older, so it loads on the compiler of SDK 10.0.100 (checked facts of phase 47). A later phase adds the packages of its projects. A new major or a change of an exact version is a commit of its own.

`nuget.config` clears the package sources and adds only nuget.org. So each machine restores from the same source, and a machine with more sources gives no warning `NU1507`.

`BannedSymbols.txt`:

```
P:System.DateTime.Now;Use TimeProvider
P:System.DateTime.UtcNow;Use TimeProvider
P:System.DateTime.Today;Use TimeProvider
P:System.DateTimeOffset.Now;Use TimeProvider
P:System.DateTimeOffset.UtcNow;Use TimeProvider
M:System.Threading.Tasks.Task.Delay(System.Int32);Use Task.Delay with a TimeProvider
M:System.Threading.Tasks.Task.Delay(System.TimeSpan);Use Task.Delay with a TimeProvider
M:System.Threading.Tasks.Task.Delay(System.Int32,System.Threading.CancellationToken);Use Task.Delay with a TimeProvider
M:System.Threading.Tasks.Task.Delay(System.TimeSpan,System.Threading.CancellationToken);Use Task.Delay with a TimeProvider
T:System.Diagnostics.Stopwatch;Use TimeProvider.GetTimestamp
P:System.Environment.TickCount64;Use TimeProvider.GetTimestamp
M:System.Threading.CancellationTokenSource.#ctor(System.Int32);Use the constructor with a TimeProvider
M:System.Threading.CancellationTokenSource.#ctor(System.TimeSpan);Use the constructor with a TimeProvider
M:System.Threading.CancellationTokenSource.CancelAfter(System.Int32);Use a CancellationTokenSource with a TimeProvider
M:System.Threading.CancellationTokenSource.CancelAfter(System.TimeSpan);Use a CancellationTokenSource with a TimeProvider
M:System.Threading.PeriodicTimer.#ctor(System.TimeSpan);Use the constructor with a TimeProvider
M:System.Threading.Tasks.Task.WaitAsync(System.TimeSpan);Use WaitAsync with a TimeProvider
M:System.Threading.Tasks.Task.WaitAsync(System.TimeSpan,System.Threading.CancellationToken);Use WaitAsync with a TimeProvider
M:System.Guid.CreateVersion7;Use Guid.CreateVersion7 with the time from TimeProvider
P:Microsoft.Extensions.Configuration.IConfiguration.Item(System.String);Use a typed options class
```

`.editorconfig` sets these rules as errors for C# files:
- `csharp_style_namespace_declarations = file_scoped`.
- A naming rule for private fields: `_camelCase`.
- A naming rule for interfaces: the prefix `I`.
- A naming rule for type parameters: the prefix `T`.
- `dotnet_diagnostic.CA2007.severity = none`, because the library has no synchronization context to avoid.
- `dotnet_diagnostic.RS0030.severity = error`, so that a banned API stops the build in each project, also where warnings are not errors.
- `dotnet_diagnostic.IDE0305.severity = error`, so that a LINQ `ToArray()` or `ToList()` that a collection expression can replace stops the build.

### 5.3 Naming and code conventions

These conventions apply to all code:
- Projects and namespaces are `MsgHatch.<Area>[.<Provider>]`, for example `MsgHatch.Templates.Liquid`. The folder path is the namespace (D50).
- A channel package is `MsgHatch.Channels.<Channel>[.<Provider>]`, for example `MsgHatch.Channels.Email.AmazonSes`. A provider name is PascalCase, also for an acronym: `AmazonSes`, `AmazonSns`, `RabbitMq`, `MongoDb`, and `PostgreSql` (D324).
- A storage implementation is `MsgHatch.Data.<Technology>`. A package of the subscription router starts with `MsgHatch.Subscriptions`, for example `MsgHatch.Subscriptions.Data.<Technology>` or the cache backend `MsgHatch.Subscriptions.Caching.<Technology>` (D58, D97).
- A `MsgHatch.Data.<Technology>` package references only `MsgHatch.Domain`. A `MsgHatch.Subscriptions.Data.<Technology>` package references `MsgHatch.Subscriptions` and its core package `MsgHatch.Data.<Technology>`. A cache backend references only `MsgHatch.Subscriptions` (D59).
- Each implementation package has its setup methods in its `DependencyInjection` folder. Their names start with `Use`. One call registers all parts of the package for one layout. A second layout, for example one storage for each tenant, has its own `Use…` method (D60 and D61).
- A tenant service has no tenant parameter. It gets its tenant from its key (D126).
- Each file has one public type, and the file name is the type name. A generic type with the same name as a non-generic type is in a file with the suffix `OfT`, for example `ReceivedMessageOfT.cs`.
- Namespaces are file-scoped.
- A type is `sealed` unless it is designed for inheritance. Data types are records. Small values are `readonly record struct`.
- Type parameters name their role: `TEvent`, `TPayload`, `TEnvelope`, `TStore`, `TLoader`, `TComposer`, `TEngine`.
- An async method ends with `Async`, returns `ValueTask` or `ValueTask<T>`, and takes `CancellationToken ct` as its last parameter.
- Private fields are `_camelCase`, also for static fields.
- A UTC time property or parameter ends with `Utc`. A point in time ends with `AtUtc`, for example `CreatedAtUtc` and `LastTryAtUtc`. A bound of a range or of a state ends with `FromUtc`, `ToUtc`, `UntilUtc`, `SinceUtc`, or `BeforeUtc`, for example `CreatedFromUtc`, `LeaseUntilUtc`, `SinceUtc`, and `finishedBeforeUtc`.
- Error types are `static readonly ErrorType` fields of `Errors`, in the namespace `MsgHatch.Domain.Failures`.
- Wrappers are `<Service>LoggingWrapper` and `<Service>CachingWrapper`.
- A log method is a source-generated `[LoggerMessage]` extension method of `ILogger` in a static partial class, so a caller writes `logger.SendFailed(...)` and does not pass the logger.
- A sequence becomes an array or a list through a collection expression, for example `[.. items.Select(...)]`, not through a LINQ `ToArray()` or `ToList()`. The `ToArray()` of a span, a memory, a list, or a stream is not LINQ, and stays.
- Options classes end with `Options` (D49). Each setting is a property of an options class, with data validation attributes, a source-generated validator, and a configuration section (D63 to D65). No code reads an `IConfiguration` value through an indexer.
- The public API has XML documentation. Other comments are only for facts about an external system that the code cannot express, for example an SDK limit. Intent goes into small functions with clear names. A `ponytail:` comment marks a deliberate limit and its upgrade path.
- All text in the code is English.
- Each new test fails first. For a new feature, the test runs one time before the implementation exists. For a change, the test runs one time with the implementation broken on purpose.

### 5.4 Git workflow

- `main` is the only long-lived branch. Phase 0 adds two commits: the plan with the README, and `AGENTS.md`. The commits of the phases follow.
- `AGENTS.md` at the repository root holds the rules for coding agents. Each phase that changes a command or a rule updates it.
- Each phase is one milestone. A milestone is one group of commits, with one commit for each concern. A commit contains the code of its concern and the tests of that code.
- A commit message has an imperative subject of at most 72 characters, an empty line, and a body that tells why.
- At the end of a phase: `dotnet build MsgHatch.slnx` has no errors and no warnings, `dotnet test MsgHatch.slnx` passes, the commits are pushed to `origin/main`, and an annotated tag `phase-N` marks the milestone.
- Nothing is committed or pushed without the owner's request.

### 5.5 Test projects and test design

The tests follow the test guide `.claude/skills/writing-tests/SKILL.md`.

**Test projects (D66).** One test project exists for each production project and each level. A phase creates a test project when it adds the first test for that project.

| Test project | Level | Real dependency | Phase |
|---|---|---|---|
| `MsgHatch.Domain.Tests.Unit` | Unit | none | 1 |
| `MsgHatch.SourceGeneration.Tests.Unit` | Unit | none | 3 |
| `MsgHatch.Channels.Email.Tests.Unit` | Unit | none | 3 |
| `MsgHatch.Templates.Tests.Unit` | Unit | A temporary folder for the file template store | 24 |
| `MsgHatch.Channels.Email.AmazonSes.Tests.Unit` | Unit | none. The AWS client is a substitute, or a real SDK client with no retry of its own on a fake HTTP handler that answers each request with an error and its headers. | 4 |
| `MsgHatch.Templates.Liquid.Tests.Unit` | Unit | none. The template store is a substitute or the embedded templates of the test project. | 23 |
| `MsgHatch.Channels.Email.AmazonSes.Tests.Integration` | Integration | An Amazon SES account, with the settings of `appsettings.Local.json`, which git ignores. The default recipient is the SES mailbox simulator. Each test is skipped with no account (D124). | 4 |
| `MsgHatch.Channels.Email.Resend.Tests.Unit` | Unit | none. A fake HTTP handler is the Resend API. | 4 |
| `MsgHatch.Channels.Email.Resend.Tests.Integration` | Integration | A Resend account, with the API key in `appsettings.Local.json`, which git ignores. The default sender is `onboarding@resend.dev`, and the default recipient is the Resend test address `delivered@resend.dev`. Each test is skipped with no API key (D124). | 4 |
| `MsgHatch.Channels.SignalR.Tests.Unit` | Unit | none. The hub context is a substitute. | 5 |
| `MsgHatch.Channels.Broker.Tests.Unit` | Unit | none. The broker client is a substitute. | 6 |
| `MsgHatch.Channels.Broker.Wolverine.Tests.Unit` | Unit | none. The Wolverine message bus is a substitute, or a real Wolverine runtime in process with a fake transport in memory. | 6 |
| `MsgHatch.Channels.Broker.Wolverine.Tests.Integration` | Integration | A RabbitMQ broker, for example a local container, with its URI in `appsettings.Local.json`, which git ignores. The tests declare only exclusive queues. Each test is skipped with no URI (D140). | 6 |
| `MsgHatch.Channels.Broker.AmazonSns.Tests.Unit` | Unit | none. The SNS client of the AWS SDK is a substitute, or a real SDK client on a fake HTTP handler that answers each request with an error and its headers. The process has no AWS credentials. | 38 |
| `MsgHatch.Channels.Broker.AmazonSns.Tests.Integration` | Integration | An SNS topic and an SQS queue with a raw message delivery subscription to the topic, with the settings of `appsettings.Local.json`, which git ignores. The tests create no AWS resource, and delete each message that they receive. Each test is skipped with no settings. | 38 |
| `MsgHatch.Data.FileSystem.Tests.Unit` | Unit | A temporary folder that the test creates and deletes | 7 |
| `MsgHatch.Data.Sqlite.Tests.Unit` | Unit | SQLite in process, with a database file in a temporary folder that the test creates and deletes (D71) | 8 |
| `MsgHatch.Subscriptions.Tests.Unit` | Unit | none | 9 |
| `MsgHatch.Subscriptions.Data.FileSystem.Tests.Unit` | Unit | A temporary folder that the test creates and deletes | 10 |
| `MsgHatch.Subscriptions.Data.Sqlite.Tests.Unit` | Unit | SQLite in process, with a database file in a temporary folder that the test creates and deletes (D71) | 10 |
| `MsgHatch.Subscriptions.Caching.Hybrid.Tests.Unit` | Unit | none. HybridCache runs as a real in-memory cache. | 11 |
| `MsgHatch.Subscriptions.Caching.FusionCache.Tests.Unit` | Unit | none. FusionCache runs as a real in-memory cache. | 11 |
| `MsgHatch.Subscriptions.AspNetCore.Tests.Unit` | Unit | none. A `TestServer` runs the endpoints in memory, with doubles. | 12 |
| `ExampleApp.Tests.Unit` | Unit | SQLite in process, with a database file in a temporary folder (D71) | 13 |
| `MsgHatch.Domain.Tests.Performance` | Performance | none | 13 |
| `MsgHatch.Channels.Email.Tests.Performance` | Performance | none | 13 |
| `MsgHatch.Subscriptions.Tests.Performance` | Performance | none | 13 |
| `MsgHatch.AspNetCore.Tests.Unit` | Unit | none. A `TestServer` runs the endpoints in memory, with doubles. | 15 |
| `MsgHatch.Data.EFCore.Tests.Integration` | Integration | The SQL Server, the PostgreSQL, and the MySQL of `appsettings.Local.json`, which git ignores, or else each server in a container through Testcontainers (D124, D180). The folders `PostgreSql` and `MySql` run the contracts on PostgreSQL and on MySQL with no dialect (phases 49 and 52) | 18 |
| `MsgHatch.Subscriptions.Data.EFCore.Tests.Integration` | Integration | The SQL Server, the PostgreSQL, and the MySQL of `appsettings.Local.json`, which git ignores, or else each server in a container through Testcontainers (D124, D180). The folders `PostgreSql` and `MySql` run the contract on PostgreSQL and on MySQL with no dialect (phases 49 and 52) | 18 |
| `MsgHatch.Data.EFCore.Tests.Unit` | Unit | The EF Core SQLite provider in process, with a database file in a temporary folder (D71), and the EF Core in-memory provider as a provider that is not relational | 48 |
| `MsgHatch.Subscriptions.Data.EFCore.Tests.Unit` | Unit | The EF Core SQLite provider in process, with a database file in a temporary folder (D71) | 48 |
| `MsgHatch.Data.EFCore.SqlServer.Tests.Integration` | Integration | The SQL Server of `MsgHatch.Data.EFCore.Tests.Integration`, with a database of the migrations of the package (D180, D320) | 48 |
| `MsgHatch.Data.EFCore.SqlServer.Tests.Unit` | Unit | none. The connection string names a closed port, and no test opens a connection (D330). | 54 |
| `MsgHatch.Subscriptions.Data.EFCore.SqlServer.Tests.Integration` | Integration | The SQL Server of `MsgHatch.Data.EFCore.Tests.Integration`, with a database of the migrations of both SQL Server packages (D180, D320) | 48 |
| `MsgHatch.Subscriptions.Data.EFCore.SqlServer.Tests.Unit` | Unit | none. The connection string names a closed port, and no test opens a connection (D330). | 54 |
| `MsgHatch.Data.EFCore.PostgreSql.Tests.Integration` | Integration | The PostgreSQL of `MsgHatch.Data.EFCore.Tests.Integration`, with a database of the migrations of the package (D180, D320) | 49 |
| `MsgHatch.Data.EFCore.PostgreSql.Tests.Unit` | Unit | none. The connection string names a closed port, and no test opens a connection (D330). | 54 |
| `MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Integration` | Integration | The PostgreSQL of `MsgHatch.Data.EFCore.Tests.Integration`, with a database of the migrations of both PostgreSQL packages (D180, D320) | 49 |
| `MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Unit` | Unit | none. The connection string names a closed port, and no test opens a connection (D330). | 54 |
| `MsgHatch.Data.EFCore.MySql.Tests.Integration` | Integration | The MySQL of `MsgHatch.Data.EFCore.Tests.Integration`, with a database of the migrations of the package (D180, D320) | 52 |
| `MsgHatch.Data.EFCore.MySql.Tests.Unit` | Unit | none. The connection string names a closed port, and no test opens a connection (D330). | 54 |
| `MsgHatch.Subscriptions.Data.EFCore.MySql.Tests.Integration` | Integration | The MySQL of `MsgHatch.Data.EFCore.Tests.Integration`, with a database of the migrations of both MySQL packages (D180, D320) | 52 |
| `MsgHatch.Subscriptions.Data.EFCore.MySql.Tests.Unit` | Unit | none. The connection string names a closed port, and no test opens a connection (D330). | 54 |
| `SchemaMigrator.Tests.Integration` | Integration | The SQL Server, the PostgreSQL, and the MySQL of `MsgHatch.Data.EFCore.Tests.Integration` (D180, D320) | 48 |
| `SchemaMigrator.Tests.Unit` | Unit | none. The connection strings name a closed port, and no test opens a connection. The tests set the environment variables of the process, so they run one by one (D330). | 54 |
| `MsgHatch.Data.MongoDb.Tests.Integration` | Integration | The MongoDB of `appsettings.Local.json`, which git ignores, or else MongoDB in a container through Testcontainers (D124, D183) | 19 |
| `MsgHatch.Data.MongoDb.Tests.Unit` | Unit | none. No test sends a command to a server (D330). A client that a test builds starts the background monitor of the driver, which no test waits for. | 54 |
| `MsgHatch.Subscriptions.Data.MongoDb.Tests.Integration` | Integration | The MongoDB of `appsettings.Local.json`, which git ignores, or else MongoDB in a container through Testcontainers (D124, D183) | 19 |
| `MsgHatch.Subscriptions.Data.MongoDb.Tests.Unit` | Unit | none. No test sends a command to a server (D330). | 54 |
| `MsgHatch.Dispatch.NamedPipes.Tests.Unit` | Unit | The named pipes of the operating system, between DI containers in the test process | 21 |
| `MsgHatch.Dispatch.RabbitMq.Tests.Unit` | Unit | none. A TCP listener in the test process closes each connection, so each connect fails, and `FakeTimeProvider` ends the pause. | 40 |
| `MsgHatch.Dispatch.RabbitMq.Tests.Integration` | Integration | A RabbitMQ broker, for example a local container, with its URI in `appsettings.Local.json`, which git ignores. Each test uses its own auto-delete exchange, and the signals declare only exclusive queues. A TCP forwarder in the test process closes the connections for the tests of a lost connection, which are skipped with the scheme `amqps`. Each test is skipped with no URI. | 40 |
| `MsgHatch.Templates.AspNetCore.Tests.Unit` | Unit | none. A `TestServer` runs the endpoints in memory, with a manager substitute for each key. | 25 |
| `MsgHatch.AdminUI.Tests.Unit` | Unit | none. bUnit renders the components in memory. | 28 |
| `MsgHatch.Templates.AdminUI.Tests.Unit` | Unit | none. bUnit renders the pages in memory, with substitutes of the query and command interfaces. | 28 |
| `MsgHatch.Templates.Liquid.AdminUI.Tests.Unit` | Unit | none. bUnit renders the editor in memory, with a substitute of the template queries, `FakeTimeProvider`, and the loose JavaScript interop of bUnit. | 28 |
| `MsgHatch.Subscriptions.AdminUI.Tests.Unit` | Unit | none. bUnit renders the pages in memory, with substitutes of the query and command interfaces, the verifier, and the content type registry. | 28 |
| `AdminUiExample.Tests.Unit` | Unit | none. A `TestServer` runs the host, and a fake primary HTTP handler is the API of `ExampleApp`. | 28 |

Packages:
- `Directory.Build.props` gives each project whose name ends with `.Tests.Unit`, `.Tests.Integration`, or `.Tests.Performance` the property `OutputType` `Exe`. It gives each unit and integration test project references to `xunit.v3`, `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`, `NSubstitute`, `NSubstitute.Analyzers.CSharp`, `Microsoft.Extensions.TimeProvider.Testing`, `Microsoft.Extensions.Diagnostics.Testing`, and `coverlet.collector` (D70).
- `MsgHatch.Domain.Tests.Unit` also references `Microsoft.Extensions.DependencyInjection` and `Microsoft.Extensions.Configuration`, for the DI container and the in-memory configuration of its tests. `MsgHatch.SourceGeneration.Tests.Unit` also references `Microsoft.CodeAnalysis.CSharp`. `ExampleApp.Tests.Unit` also references `OpenTelemetry.Exporter.InMemory`. `MsgHatch.Channels.Email.AmazonSes.Tests.Unit`, `MsgHatch.Channels.Email.Resend.Tests.Unit`, `MsgHatch.Templates.Tests.Unit`, and `MsgHatch.Templates.Liquid.Tests.Unit` also reference `Microsoft.Extensions.DependencyInjection` and `Microsoft.Extensions.Configuration`. `MsgHatch.Channels.Email.Tests.Unit` also references `MsgHatch.Templates.Tests.Unit`, for its test types and its template files. `MsgHatch.Channels.Email.AmazonSes.Tests.Integration` and `MsgHatch.Channels.Email.Resend.Tests.Integration` also reference `Microsoft.Extensions.DependencyInjection` and `Microsoft.Extensions.Configuration.Json`, for the DI container and the appsettings files. The EF Core integration test projects also reference `Testcontainers.MsSql`, `Testcontainers.PostgreSql` (phase 49), `Microsoft.EntityFrameworkCore.SqlServer`, `Microsoft.Data.SqlClient` 6.1.8 (the version with no known vulnerability of its dependencies), `Microsoft.Extensions.DependencyInjection`, and `Microsoft.Extensions.Configuration`, and `MsgHatch.Data.EFCore.Tests.Integration` also references `Microsoft.EntityFrameworkCore.Sqlite` for the SQLite examples of the README, and `Microsoft.EntityFrameworkCore.Design` for the generator of the migrations of the provider packages (D320). The other EF Core integration projects and `SchemaMigrator.Tests.Integration` reference `MsgHatch.Data.EFCore.Tests.Integration` for its fixture of the server, and its appsettings files reach their output. The EF Core unit test projects reference `Microsoft.EntityFrameworkCore.Sqlite`, `Microsoft.EntityFrameworkCore.InMemory`, and `MsgHatch.Data.EFCore.Tests.Unit` also `Microsoft.EntityFrameworkCore.Design` for the migrations of a host on SQLite. The MongoDB integration test projects reference `Testcontainers.MongoDb`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Configuration`, and `Microsoft.Extensions.Configuration.Json`, and `MsgHatch.Subscriptions.Data.MongoDb.Tests.Integration` also references `MsgHatch.Data.MongoDb.Tests.Integration` for its fixture of the server. `MsgHatch.AspNetCore.Tests.Unit` and `MsgHatch.Subscriptions.AspNetCore.Tests.Unit` also reference `Microsoft.AspNetCore.TestHost`. The test projects of the four admin UI packages reference `bunit` 2.15.2 (MIT), and `AdminUiExample.Tests.Unit` references `Microsoft.AspNetCore.TestHost`. The performance projects reference `BenchmarkDotNet`.
- Each test project references the production project under test. A project with subclasses of a contract base class also references the test project that holds the base class.
- Before a phase adds a package, it reads the license of the pinned version from the package metadata. Only MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, and ISC are allowed. A dual license is allowed when one of its licenses is allowed. No license is taken from memory. One exception: a transitive package of the database provider that a test project uses may have the license of the vendor of that provider, when no `src` package references it (D179).
- Assertions use the `Assert` class of xUnit. Fakes of the clock and the logger come from the Microsoft testing packages: `FakeTimeProvider` and `FakeLogger`. All other doubles are NSubstitute substitutes (D14). The exceptions are the fake HTTP handlers of the Resend tests and of `AdminUiExample.Tests.Unit`, because NSubstitute cannot configure the protected `SendAsync` of `HttpMessageHandler`.

Stages. Each level runs in its own stage, and one glob on the path selects it:

| Level | Glob | Stage |
|---|---|---|
| Unit | `**/*.Tests.Unit/*` | The validation of each pull request |
| Integration | `**/*.Tests.Integration/*` | After the merge. The SES and Resend integration tests (phase 4) need an account in `appsettings.Local.json`. The EF Core integration tests (phases 18, 48, and 49) and the tests of `SchemaMigrator` need the SQL Server of `SqlServerIntegration:ConnectionString` and the PostgreSQL of `PostgreSqlIntegration:ConnectionString` in `appsettings.Local.json`, or an agent with a container runtime (D180). The MongoDB integration tests (phase 19) need the MongoDB of `MongoDbIntegration:ConnectionString` in `appsettings.Local.json`, or an agent with a container runtime (D183). Testcontainers also starts its Ryuk container, which removes the containers of a crashed run. A machine with no Ryuk image sets `TESTCONTAINERS_RYUK_DISABLED=true` for its own run (D177). |
| Performance | `**/*.Tests.Performance/*` | A manual or nightly run on a dedicated machine |

The pipeline itself is out of scope. A unit test never needs a database server or a service, so the unit stage needs no infrastructure. Each test that needs no server is a unit test (D330), so the integration stage holds only the tests that need a server, and the unit stage checks each snippet of each README. The coverage of the unit stage leaves out the EF Core migrations that the tools write, because the integration stage runs them on real servers (D70). The SQLite tests need only the native SQLite library, which the `Microsoft.Data.Sqlite` package contains, and a temporary folder (D71).

**Names:**
- A test class is `{Sut}UnitTests`, `{Sut}IntegTests`, or `{Sut}PerfTests`, with the exact name of the system under test. For an interface with many implementations, the class names the implementation, for example `SqliteOutboxStoreUnitTests`.
- A test method is `Method_Condition_ExpectedResult`, for example `ClaimDueAsync_LeaseEnded_ReturnsMessageAgain`.
- A large test class is split into files with a suffix, for example `OutboxStoreTestsBase.ClaimDueAsync.cs`, and keeps one class name.
- The rename of a production type or member also renames its test classes, test methods, and files, in a separate commit.

**Contract base classes (D67).** Each domain interface whose implementations must behave alike has one abstract generic base class. The base class holds all use-case tests of the interface:

| Base class | Test project, folder `Contracts/` | Test data abstraction | Subclasses (phase) |
|---|---|---|---|
| `OutboxStoreTestsBase<TStore>` | `MsgHatch.Domain.Tests.Unit` | `IOutboxStoreTestData` | `InMemoryOutboxStoreUnitTests` (1), `FileSystemOutboxStoreUnitTests` (7), `SqliteOutboxStoreUnitTests` (8), `TenantOutboxDispatchStoreUnitTests` (17), `EntityFrameworkOutboxStoreIntegTests` (18), `MongoDbOutboxStoreIntegTests` (19) |
| `InboxStoreTestsBase<TStore>` | `MsgHatch.Domain.Tests.Unit` | `IInboxStoreTestData` | `InMemoryInboxStoreUnitTests` (2), `FileSystemInboxStoreUnitTests` (7), `SqliteInboxStoreUnitTests` (8), `TenantInboxDispatchStoreUnitTests` (17), `EntityFrameworkInboxStoreIntegTests` (18), `MongoDbInboxStoreIntegTests` (19) |
| `DispatchSignalTestsBase<TSignal>` | `MsgHatch.Domain.Tests.Unit` | none | `InProcessDispatchSignalUnitTests` (2) |
| `TransportTestsBase<TTransport>` | `MsgHatch.Domain.Tests.Unit` | `ITransportTestDriver` | `EmailTransportUnitTests` (4), `SignalRTransportUnitTests` (5), `BrokerTransportUnitTests` (6) |
| `BrokerClientTestsBase<TClient>` | `MsgHatch.Channels.Broker.Tests.Unit` | `IBrokerClientTestDriver` | `WolverineBrokerClientUnitTests` (6), `AmazonSnsBrokerClientUnitTests` (38) |
| `RegistrySettingsStoreTestsBase<TStore>` | `MsgHatch.Domain.Tests.Unit` | `IRegistrySettingsStoreTestData` | `InMemoryRegistrySettingsStoreUnitTests` (16), `FileSystemRegistrySettingsStoreUnitTests` (16), `SqliteRegistrySettingsStoreUnitTests` (16), `EntityFrameworkRegistrySettingsStoreIntegTests` (18), `MongoDbRegistrySettingsStoreIntegTests` (19) |
| `TemplateStoreTestsBase<TStore>` | `MsgHatch.Templates.Tests.Unit` | `ITemplateStoreTestData` | `FileSystemTemplateStoreUnitTests` (3), `EmbeddedResourceTemplateStoreUnitTests` (3) |
| `TemplateEngineTestsBase<TEngine>` | `MsgHatch.Templates.Tests.Unit` | `ITemplateTestData` | `PlaceholderTemplateEngineUnitTests` (3), `LiquidTemplateEngineUnitTests` (23) |
| `TemplateWriterTestsBase<TStore>` | `MsgHatch.Templates.Tests.Unit` | none: each test method uses a message type of its own | `FileSystemTemplateStoreWriterUnitTests` (25) |
| `EmailClientTestsBase<TClient>` | `MsgHatch.Channels.Email.Tests.Unit` | `IEmailClientTestDriver` | `AmazonSimpleEmailServiceClientUnitTests` (4) |
| `SubscriptionStoreTestsBase<TStore>` | `MsgHatch.Subscriptions.Tests.Unit` | `ISubscriptionStoreTestData` | `InMemorySubscriptionStoreUnitTests` (9), `FileSystemSubscriptionStoreUnitTests` (10), `SqliteSubscriptionStoreUnitTests` (10), `EntityFrameworkSubscriptionStoreIntegTests` (18), `MongoDbSubscriptionStoreIntegTests` (19) |
| `SubscriptionCacheBackendTestsBase<TBackend>` | `MsgHatch.Subscriptions.Tests.Unit` | `ISubscriptionCacheBackendTestData` | `HybridCacheSubscriptionBackendUnitTests` (11), `FusionCacheSubscriptionBackendUnitTests` (11) |
| `RecipientDirectoryTestsBase<TDirectory>` | `MsgHatch.Subscriptions.Tests.Unit` | `IRecipientDirectoryTestData` | `ExampleRecipientDirectoryUnitTests` (13) |
| `SubscriptionQueriesTestsBase<T>`, `SubscriptionCommandsTestsBase<T>`, `RoutingVerifierTestsBase<T>` | `MsgHatch.Subscriptions.Tests.Unit` | `IManagementTestHost<T>`, which the subclass starts on the services of each test | `SubscriptionManagerQueriesUnitTests`, `SubscriptionManagerCommandsUnitTests`, `RoutingVerifierUnitTests` (26), and the HTTP clients of phase 27 |
| `TemplateQueriesTestsBase<T>`, `TemplateCommandsTestsBase<T>` | `MsgHatch.Templates.Tests.Unit` | `IManagementTestHost<T>`; each test method uses a message type of its own | `TemplateManagerQueriesUnitTests`, `TemplateManagerCommandsUnitTests` (26), and the HTTP clients of phase 27 |

Rules for a base class:
1. The type parameter is constrained to the interface, for example `where TStore : class, IOutboxDispatchStore`. A store base class also takes a function that gives the tenant store of a key, so that it tests both parts of the store (D127).
2. The constructor takes the implementation under test and its test data abstraction. The base class never names a concrete type.
3. The test data abstraction does what the interface cannot do. It resets the storage, seeds a scenario, and reads the stored state back, for example the publish tries of a message. A driver abstraction makes the provider behind a client or a transport succeed, fail, or wait.
4. A real difference between implementations is a constructor parameter, not a reason to copy the tests.
5. The base class is `partial`, with one file for each operation of the interface.
6. The base class owns a `FakeTimeProvider` that starts at `2030-01-01T00:00:00Z`, and gives each time to the store (section 4.14).
7. The base class holds the shared factories and assertions as protected members. Each test method passes in each value that it asserts.
8. The base class resets the data through `IAsyncLifetime`: before and after each test for a test that writes, and one time for a group that only reads one seeded scenario. In `SubscriptionStoreTestsBase<TStore>`, the read group reads from the class fixture `AcmeAndGlobexScenario`, which seeds the scenario one time on storage of its own (D151).

Rules for a subclass:
1. A subclass has a class declaration and one constructor. The constructor builds the implementation and its test data abstraction, and gives them to the base class.
2. A subclass adds a test method only for a behavior that is unique to its implementation, for example the version from the last write time of a template file.
3. A subclass that needs an xUnit fixture declares it with `IClassFixture<T>` or `[Collection]`. Without the declaration, xUnit cannot construct the subclass.
4. A store outside this repository, for example a SQL Server store of a consumer, gets the same coverage with one subclass. A package that publishes the base classes is out of scope.

Use cases of the store base classes. The phases add the use cases of the other base classes:
- `AddAsync`: an empty batch, one message, several messages, a duplicate id inside one batch, and an id that is already stored. A duplicate id is stored one time.
- The tenants (D126, D127): the tenant store of one key does not see the messages of another key, and a message with another tenant gives `Errors.ValidationFailed`. For the inbox, the same id with two tenants gives two messages, also when the two tenant ids differ only in case, and a change of one of them does not change the other. A tenant id with special characters goes through an add, a claim, and a completion unchanged.
- `ClaimDueAsync`: `maxCount` 0 gives `Errors.ValidationFailed`. With `maxCount` 1 and 2, the claim returns at most that number. Of two due messages, the first added is due later: with `maxCount` 1, the claim takes the message that is due first, and with `maxCount` 2, it returns the two in the order of their due time (D144). A message that is not due yet is not claimed. The lease boundary has three rows: a claim at T + 29, T + 30, and T + 31 seconds after a claim with a lease of 30 seconds.
- `ClaimDueAsync` returns each message as its registered closed type, with its tenant, and without its tries. It claims the messages of all tenants. Two parallel claims get different messages.
- `ClaimDueAsync` gives `NextDueAtUtc`: null with no pending message, and else the earliest `NextTryAtUtc` of the pending messages after the claim, which includes the lease end of a claimed message. For the outbox, a message of another channel does not count.
- `IOutboxDispatchStore.ClaimDueAsync` claims only messages of its channel.
- After its lease, a finished message is not claimed again: a completed or permanently failed received message, and an outgoing message after a try with each final outcome. A message with a next try time is claimed only from that time, with three rows at the limit.
- The down state of a channel (D106). `SetChannelDownAsync` with the reason `Errors` writes one time, and a second call returns false and keeps the first state. The reason `Operator` replaces it. `SetChannelUpAsync` with the reason `Errors` does not end the reason `Operator`, and with no reason it ends each reason. Of two parallel `TryStartChannelCheckAsync` calls, exactly one returns true. A claim of a down channel returns no message, and returns the down state.
- From phase 32, the state rows (D277): the version, `PreviousKind`, and `PreviousDuration` of each row, no row for a call that does not change the state, the rows of `GetChannelStatesAsync` with the row current at `fromUtc`, rows older than 30 days, and one row with no error from two parallel changes.
- From phase 15, the management members (section 4.19): each filter of `QueryAsync` and a combination of filters, the order newest first, a cursor over three pages while a message changes its status, `IncludeTest`, `DueBeforeUtc`, the counts of `CountAsync` with `OldestDueAtUtc`, `GetAsync` with all tries in order, and the rules of `RetryAsync` and `CancelAsync` for each status, for an active lease, and for an expired message. `CountAsync` of a tenant store counts only its tenant, and of the outbox dispatch store all tenants. `PurgeAsync` deletes only finished messages before the given time, at most `maxCount`, and keeps each pending message. A cancelled received message with no try is purged by its `CreatedAtUtc`.
- `RecordTryAsync`: each `PublishOutcome` gives the status of section 4.6. All tries stay in order, and a try number that is not the stored `TryCount` plus 1 changes nothing.
- Each stored text round-trips unchanged, also with accented characters, for example Hungarian `ő` and `ű`, and with special characters, for example `"`, `\`, `<`, and `&`.
- `MessageFlags` round-trip unchanged. `DeleteTestDataAsync` deletes the test messages of all tenants and their tries, keeps each message without the flag, and returns the number of deleted messages. A second call returns 0.

**Test design rules:**
- A unit test is preferred. An integration test exists only for a real out-of-process dependency: Amazon SES and Resend for their email clients, SQL Server and PostgreSQL for the EF Core stores and their migrations, and MongoDB for the MongoDB stores (D124). The contract tests of the EF Core stores also run on the EF Core SQLite provider as unit tests (D318). A secret of an integration test goes only into `appsettings.Local.json`, which git ignores, never into a file in the repository. SQLite runs in process, so its tests are unit tests (D71).
- Tests reach the code only through its public API (D68). An internal type, for example the dispatch loop, the pipeline, `RetrySchedule`, the template selector, or the template parser, is tested through the public interface that uses it.
- A test that needs a DI container builds it with the setup methods of a host: `AddMsgHatch()` and the `Use…` methods. After the setup, it replaces only the doubles, through the factory overloads. No test copies the body of a setup method.
- Each test has `// Arrange`, `// Act`, and `// Assert` comments, one behavior, and no `if`, loop, or `try` in its body.
- A data-driven test is a `[Theory]` with typed `TheoryData<…>` rows, one named row for each case. Each limit gets three rows: one value below it, one at it, and one above it. The limit values are literals in the rows, never the constants of the production code.
- Each validated input gets the cases of its type. A string gets null, empty, only whitespace, and accented or special characters. A number gets zero, a positive value, and a negative value. A collection gets null, empty, one item, several items, and a duplicate item.
- Each options class gets one data-driven test for its attributes and its rules across properties, with the limits of section 4.4. A failure row asserts the name of the failed property. It does not assert the rest of the message.
- A failure test asserts a stable identifier: the `ErrorType` code, the log event id, the HTTP status code, the exception type, or the name of the failed property. It never asserts the text of a message.
- A test asserts a collection whole, with at least two elements.
- A factory gives a valid object by default (D69). A value that an assertion reads is bound to a local variable, and the Act and the Assert read that variable.
- Each async test passes `TestContext.Current.CancellationToken` to each call that takes a token.
- Each SQLite test uses its own database file in the shared temporary folder `msghatch-tests`, so the SQLite tests can run in parallel with the other unit tests. The test deletes its database file and the WAL files of it at the end. The store does not create a folder, and the base classes reset the storage before and after each test, so a folder for each test cannot be deleted at a reset. The connection string comes from a configuration layer of the test, bound to `SqliteStoreOptions` (D65), not from code. A file, not an in-memory database, is necessary, because the tests open two connections and two store instances on the same database.
- A run that reports 0 tests is a failure. VSTest can report an empty xUnit v3 run as a pass, so each run's test count is read.
- The test classes that listen to the spans of MsgHatch are in the one xUnit collection `Telemetry`, so they never run in parallel. An `ActivityListener` is global, and a span changes the trace context of the messages.
- The README tests (D311): `Directory.Build.props` links `tests/Shared/ReadmeCheck.cs` into each unit and integration test project, with the folder of the project as assembly metadata. The test project of each package has `ReadmeUnitTests`, or `ReadmeIntegTests` for the 4 EF Core and MongoDB store packages, which open no connection. Each class runs:
  - `ReadmeCheck.AssertSnippets()`: each C# block of the README equals a region `#region readme:name` of a test file of the project, with the indent removed. Each JSON or Razor block equals the file `Readme/name.json` or `Readme/name.razor`. A marker with no source, a source with no marker, and a C# or JSON block with no marker fail.
  - `ReadmeCheck.AssertOptionsTables(types)`: the README has one options table for each given options type, and no other. Each table names each public property that the configuration binder can fill, a property with a public setter or a get-only collection, and no other name.
  - One test for each region, which runs the example. A setup test builds the services and resolves the main service, and reads the options of `Readme/options.json` through `ReadmeCheck.ReadSettings`, so the JSON example binds and passes the options validation.
- `ReadmeCheckUnitTests` in `MsgHatch.Domain.Tests.Unit` tests the check itself on a README in the test.
- Each new test fails first (section 5.3).

### 5.6 Versions and packages

Versions (D307):
- `Directory.Build.props` sets one `VersionPrefix` for each project. The first version is `0.1.0`. A build with no suffix gives this version, and the workflow adds the suffix `ci.<run number>` to each build with no version tag.
- A phase that changes a library project under `src/` raises the version one time: the patch for fixes only, the minor for a new feature, and the major for a breaking change of the public API. Before 1.0.0, a breaking change raises the minor. A change of only tests, examples, docs, or READMEs raises nothing. `AGENTS.md` has the same rule.

Packages (D308):
- Each project under `src/` except `MsgHatch.SourceGeneration` is a package, 42 packages in all. The package id is the project name. The examples and the test projects are not packages.
- A pack turns each project reference into a package dependency with the version of the pack. For example, `MsgHatch.Channels.Email.AmazonSes` 0.1.0 depends on `MsgHatch.Channels.Email` 0.1.0 or later. An analyzer reference with `ReferenceOutputAssembly="false"` gives no dependency.
- `MsgHatch.Templates` carries the generator assembly in `analyzers/dotnet/cs`, so the placeholder payload generator runs in each consumer. The assembly also holds the HTTP client generator, which adds its internal attributes of the namespace `MsgHatch.Http` to each consumer compilation. A consumer does not use them, and an internal type of one assembly does not conflict with another assembly.
- The SDK adds Source Link, so the nuspec of each package holds the repository URL and the commit. Each package has a symbol package `.snupkg`.
- Each package has the icon `icon.png` in its root as `PackageIcon`. `Directory.Build.props` packs it from `assets/icon.png`: a 128 × 128 PNG, which is transparent outside the rounded tile. nuget.org takes a PNG or a JPEG of at most 1 MB, and recommends 128 × 128. `assets/icon.svg` is the source of the PNG.

`pwsh ./pack.ps1 [-VersionSuffix <suffix>]` packs the solution in Release into `artifacts/packages`. It fails when:
- A package has another version, no license expression `Apache-2.0`, no description, no tags, no repository commit, or no symbol package.
- A package has no icon `icon.png`, or its icon is not a 128 × 128 PNG of at most 1 MB.
- A package depends on `MsgHatch.SourceGeneration`, or on a MsgHatch package of another version.
- A packable project under `src/` has no package, or a package has no packable project under `src/`.
- `MsgHatch.Templates` has no generator, or a package does not hold a file of the `wwwroot` folder of its project or the bundle of its scoped CSS.

The GitHub workflow `.github/workflows/build.yml` (D309):
- On each push of a branch and each pull request, its job `build` installs the SDK of `global.json` on a Linux runner, runs `pwsh ./coverage.ps1` and `pwsh ./pack.ps1 -VersionSuffix ci.<run number>`, and keeps the packages as the artifact `packages`. It runs no integration test.
- The job `build` runs on `ubuntu-latest` (D313). 3 unit tests of `MsgHatch.Templates.Tests.Unit` are skipped there, and run on a Windows machine of a developer: 2 tests check that a reader that holds a template file open blocks a save and a delete, which is a file lock of Windows, and 1 test denies the listing of a folder with a Windows access rule.
- On a tag `v<version>`, it checks that the tag is `v` and the `VersionPrefix`, and packs with no suffix. Then its job `publish` in the environment `nuget` gets a temporary API key through trusted publishing: `NuGet/login` sends the OIDC token of GitHub and the nuget.org user name of the secret `NUGET_USER`. `dotnet nuget push` sends the packages and the symbol packages of the same folder, and `--skip-duplicate` makes a second run safe.
- A tag `phase-N` starts no run.

Package READMEs (D310, D311):
- Each project under `src/` has a `README.md`, and each package carries it as `PackageReadmeFile` in its root. `pack.ps1` fails for a package with no README.
- A README has these parts, in this order: the name and what the package does, the install command, the DI setup, the options, the examples of the main uses, and the version history. A package with no options says so.
- The version history has one entry for each version that changed the package, newest first: the version, the date of the commit that sets the version, and the release notes. A breaking change starts with "Breaking:". It is the only place of the release notes, so no project file sets `PackageReleaseNotes`.
- nuget.org renders CommonMark through Markdig, with tables and code blocks, but no HTML and no image or link with a relative path. So a README links with absolute GitHub URLs, and its markers are link reference definitions, which no renderer shows: `[//]: # (snippet: name)` before a code block, and `[//]: # (options: Type)` before an options table.
- A change of a library project keeps its README in sync in the same commit (`AGENTS.md`).

Before the first publish, the owner:
- Creates a trusted publishing policy on nuget.org for the repository, the workflow file `build.yml`, and the environment `nuget`.
- Adds the nuget.org user name as the repository secret `NUGET_USER`.
- Checks that the package ids are free on nuget.org. The owner can ask nuget.org to reserve the id prefix `MsgHatch`.
- Can add a protection rule with a required reviewer to the environment `nuget`.

After the first publish, the owner sets `PackageValidationBaselineVersion` in `Directory.Build.props` to the published version, for example `0.6.0`. `EnablePackageValidation` with no baseline compares only the target frameworks of one package, so it finds no breaking change against an earlier version. With a baseline, each pack also compares the public API with the baseline package from nuget.org.

## 6. Risks

- **Duplicate emails.** Delivery is at-least-once. If the process stops after SES accepts an email and before `RecordTryAsync`, the email is sent again. SES has no idempotency key, so this risk stays. Resend keeps the `Idempotency-Key` of each send for 24 hours (D138), so a repeated try within that time sends no second email. The second success is a second publish try, so the history shows the duplicate.
- **Lease and buffer.** A message can wait in the work queue before its send. If its lease ends, another instance can claim and send it too. Both senders then record try number 1, the store ignores the second record, and the history shows one try for two sends. The lease safety margin (section 4.9) prevents this in normal operation. Keep `Lease` much longer than the slowest send.
- **Contract discipline.** The golden-file tests find a breaking change only in a type that has samples. A consumer type with no samples can still break the reading of stored records. Such a failure gives `Errors.SerializationFailed`, a permanent error. `docs/stores.md` and the example app show how to add samples.
- **Storage growth.** Finished messages and their publish tries stay in the store until `IDeliveryCommands.PurgeAsync` deletes them (D128). The host calls it from its own timer with its retention, and a host that never calls it keeps each message. The retention is also the window of the duplicate detection of the inbox. `MaxTryCount` limits the number of tries for each message.
- **Stale subscriptions.** With the cache on, a change that does not go through the subscription store, for example a direct database update, takes effect only after `InvalidateAsync` or after the expiration. With many instances, an invalidation reaches the other instances only through a FusionCache backplane. With HybridCache, the local level of another instance keeps its entry until its local expiration. Keep the expiration short.
- **SQLite limits.** SQLite allows one writer at a time, so the claims and the try records of all dispatchers wait for each other. This suits one machine and a moderate volume. The database file must be on a local disk.
- **Management authorization.** The HTTP endpoints accept each company id and user id in their filters and drafts. The consumer must protect them with an authorization policy, or give end users only the `/me` routes.
- **SignalR group membership.** The host's hub must add each connection to the group of the company of its user, and to the group of its tenant (D315). A connection that is in no group gets no company notification and no tenant notification.
- **Large company scopes.** A company scope on a channel without group delivery, for example email, sends one message to each user of the company. A very large company gives a large fan-out in one event (D43).
- **Claim atomicity.** The consumer's store must claim messages atomically. The example app and the file stores show correct implementations. The library cannot check a consumer's store.
- **File store limits.** The file stores depend on an atomic rename, and on Linux and macOS also on hard links (D312). Some network file systems do not guarantee them. Use a database store there.
- **Separate publisher processes.** The in-process signal does not reach another process. On one machine, `UseNamedPipeDispatchSignal` wakes the loops of the other processes of the same OS user (D188). A lost pipe signal only delays a send until the next poll (D190). On another machine, `UseMongoDbDispatchSignal` wakes the loops when all hosts use the MongoDB stores on a replica set (D303). With other stores, `UseRabbitMqDispatchSignal` wakes the loops through a RabbitMQ broker (D305). With no such broker, a publisher there finds new messages at its next poll, at most `MaxPollInterval` later (D102). Keep `MaxPollInterval` short there for the SignalR publisher. All processes need the same stores: SQLite works only on one machine, and the file stores need a folder with atomic rename.
- **SNS delivery to the subscriptions.** A receipt of the SNS client means that SNS accepted the message for the topic. SNS delivers to each subscription later. A subscription that cannot receive, for example a queue whose policy refuses SNS, loses the message with no error to MsgHatch, unless the subscription has a dead-letter queue. The owner of the topic monitors the delivery metrics of SNS.
- **SNS message attributes.** SNS sends at most 10 message attributes to an SQS subscription with raw message delivery, and discards a message with more there with no error. The SNS client refuses a message with more than 10 headers (D301), so the headers of the channel settings and the library headers `msghatch-id`, `msghatch-tenant`, `msghatch-test`, and `msghatch-redirect` share this limit. The size limit of a message with its attributes is the attribute `MaximumMessageSize` of the topic: 256 KiB by default, and at most 1 MiB. A larger message gives `Errors.Rejected`.
- **Error texts.** The error message of a provider, for example of SES, can contain an email address. `LastError`, the publish tries, and the logs keep the error message, so the overview group of the delivery endpoints can show an address. Protect that group too.
- **Delivery authorization.** The delivery endpoints can retry, cancel, and pause for the whole outbox. The consumer must protect them with an authorization policy, and the content group with a stricter one (D109).
- **A publisher that does not run.** The library has no heartbeat of the publishers. A UI finds a stopped publisher only through its pending messages: due since a long time, with no down state of the channel (section 4.19).
- **Tenant isolation.** Each tenant service of a key sees only the data of its tenant (D126), and each cache key and SignalR name has the tenant (D114). A consumer store or directory that ignores its key mixes the data of two tenants. The store and router examples S12 and R19 test this.
- **Keyed services.** The tenant services rely on `KeyedService.AnyKey` of `Microsoft.Extensions.DependencyInjection`: one instance for each requested key (D126). A host with many tenants therefore has many instances of each singleton tenant service. Phase 1 tests the premises in the pinned version.
- **Singleton stores.** The dispatch loops call one store instance from many workers (D130). A consumer store must be thread-safe, for example with one `DbContext` for each operation.
- **In-memory stores.** The in-memory stores are not durable. A restart loses each message that waits in the inbox or the outbox (D119). Use them only for tests, demos, and data that a restart can lose.
- **Many tenants with one storage each.** The dispatch composite skips a tenant until its next due time, and asks all tenants one time in each `MaxPollInterval` (D125). With many tenants, this sweep costs more time than the sends. Use one shared storage with a tenant column there.
- **Registry settings in several processes.** A change of a registry setting reaches another process only after `RegistryOptions.RefreshInterval` (section 4.20).
- **Routers of the consumer.** A router must give one route for each target and channel. The pipeline fails a duplicate route with `Errors.Conflict`. A defect of a router then loses a message, and does not send it two times.
- **MassTransit license.** MassTransit 9 and later need a commercial license for production. Version 8 stays under Apache-2.0, and its support ends on 2026-12-31 ([Massient](https://massient.com/)). The license rule of section 5.5 allows only version 8.
- **Telemetry names.** The messaging semantic conventions have the status Development, so a new version can rename attributes. MsgHatch follows one pinned version. A change of a name breaks the dashboards of the hosts, so it comes only in its own phase (D131).
- **Old trace context.** A message that waited a long time can link to a trace that the backend already deleted. The link then shows nothing, and the send span is still complete (D132).
- **A version that a phase does not raise.** No test finds a change of a library project with no raise of `VersionPrefix`. Two different builds then have the same version, and nuget.org keeps the first one: `--skip-duplicate` skips the second push with no error. `AGENTS.md` makes the raise a rule of each phase (D307).
- **Generator scope.** The generator supports top-level properties only. A template cannot read a nested property, for example `{{Customer.Name}}`.

## 7. Out of scope

- A Razor template engine. The Razor SDK compiles a component at build time, so a host cannot add a Razor template at run time (D204).
- Loops, conditions, and nested properties in placeholder templates.
- A user interface for subscription management or for delivery management. The APIs serve a UI of the consumer.
- A library recipient directory. The company, the UI language, and the addresses of a user are data of the consumer (D76).
- A resend of a delivered message. A retry skips a `Done` message (D111).
- A time series of the traffic. A UI calls `GetTrafficAsync` for each time window.
- A retention timer in the library. The host calls `IDeliveryCommands.PurgeAsync` from its own timer (D128).
- A dispatch signal between machines with no broker for the other stores, for example SQL Server query notifications. A later package implements `IDispatchSignal` for it (D102, D188). Phase 21 adds a named pipe signal between the processes of one machine, phase 39 a change stream signal for the MongoDB stores (D303), and phase 40 a signal through RabbitMQ for each store (D305).
- Streams for very large fan-outs, for example 100,000 recipients for one event. The benchmarks show if chunks are necessary (D43).
- SMS and push channels.
- Database schema migrations for the consumer's stores.
- An analyzer that checks the versioning rules at compile time. The golden-file tests check them instead.
- A MsgHatch package for each EF Core provider. The base package runs on each relational provider, and a host generates the migrations for a provider with no MsgHatch package (D318, D320). The MsgHatch provider packages are for SQL Server (phase 48) and PostgreSQL (phase 49).
- Registry settings and transports for each tenant, for example one SES account for each tenant. The registry and the channels belong to the whole system (D114).
- A `MsgHatch.OpenTelemetry` extension package. The host uses the names of `MsgHatchTelemetry` (D131).
- Broker frameworks with their own outbox, for example Brighter and CAP (DotNetCore.CAP). They duplicate the MsgHatch outbox, its retries, and its history (D134).
- The tenant as a metric tag. An opt-in option can come later (D133).
- Spans for store calls, template rendering, or each route.
