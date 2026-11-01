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
| Channel registration | One `As<Channel>` registration of a message type on one channel, for example `AsEmail`: its name, its composer, its payload loader, its content type, and its retry options (D61, D87, D96). |
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
| Template engine | A component that turns an email template and a template payload into a rendered email. `IEmailTemplateEngine<TPayload>` is its interface (D44). |
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
| Delivery manager | `IDeliveryManager`, the API for a UI that shows the traffic and the history of the messages, and repairs stuck messages and down channels (D107). |
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
| D10 | Storage | Recommended | The consumer can implement the store interfaces. The library ships these stores: the in-memory stores in `MsgHatch.Domain` (D119), two template stores (embedded resources and files), the JSON file stores in `MsgHatch.Data.FileSystem` (D26), the SQLite stores in `MsgHatch.Data.Sqlite` (D55), the EF Core stores in `MsgHatch.Data.EntityFrameworkCore` (D120), and the MongoDB stores in `MsgHatch.Data.MongoDb` (D121). The subscription stores are in `MsgHatch.Subscriptions.Data.FileSystem`, `.Data.Sqlite`, `.Data.EntityFrameworkCore`, and `.Data.MongoDb` (D97). | The consumer writes each store. |
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
| D45 | First engine | Recommended | The placeholder engine first. A Razor engine follows later as the separate package `MsgHatch.Email.Razor` (section 4.10). | Razor components as the first engine. |
| D46 | Location of the template code | Recommended | The template stores, the placeholder engine, and the placeholder writers are in `MsgHatch.Email`, because only email uses templates (D6). | Template code in `MsgHatch.Domain`. |
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
| D58 | Implementation package names | Confirmed | A storage implementation is `MsgHatch.Data.<Technology>`, for example `MsgHatch.Data.Sqlite`, `MsgHatch.Data.EntityFrameworkCore`, or `MsgHatch.Data.MongoDb`. A package of the subscription router starts with `MsgHatch.Subscriptions`, for example `MsgHatch.Subscriptions.Data.Sqlite`, and a cache backend is `MsgHatch.Subscriptions.Caching.<Technology>`, for example `MsgHatch.Subscriptions.Caching.FusionCache` (D97). | Names with no family prefix, for example `MsgHatch.Sqlite`. |
| D59 | Location of the store interfaces | Confirmed | The store interfaces stay in `MsgHatch.Domain`, as repository interfaces of the domain. Each `MsgHatch.Data.<Technology>` package references only `MsgHatch.Domain`. `ISubscriptionStore` is in `MsgHatch.Subscriptions`. Each `MsgHatch.Subscriptions.Data.<Technology>` package references `MsgHatch.Subscriptions` and its core package `MsgHatch.Data.<Technology>`, so it shares the options and the helpers of that package. One setup in code sets these options one time (section 4.7). No `MsgHatch.Data` base project exists. | Subscription data packages with no reference to a core data package. They need their own options classes for the same settings. |
| D60 | Package setup | Confirmed | One setup call registers all parts of an implementation package for the domain interfaces, with their wrappers, options, and third-party services (section 4.7). A second layout of a package, for example one storage for each tenant, has its own `Use…` method. | Many calls for one layout, for example one for the backend and one for the third-party library. |
| D61 | Setup method names | Recommended | `Use<Technology>` selects an implementation, for example `UseSqliteStores`. `Add<Thing>` registers a message type, and `As<Channel>` registers a form of a message type on one channel, for example `AsEmail`. | `Add` for all methods. |
| D62 | Two implementations for one interface | Recommended | A startup validation error that names both setup methods. | The last setup method wins. This hides a configuration error. |
| D63 | Configuration access | Confirmed | Only typed options: each setting is a property of a dedicated options class, with data validation attributes on its properties. No code uses an `IConfiguration` indexer. The analyzer of D32 bans the indexer. | `IConfiguration` indexers. |
| D64 | Options validation | Recommended | A source-generated validator for each options class (`[OptionsValidator]`), with `ValidateOnStart`. It uses no reflection. A rule across properties is in `IValidatableObject.Validate` of the options class. | `ValidateDataAnnotations()`. It validates with reflection, and needs `Microsoft.Extensions.Options.DataAnnotations`. |
| D65 | Options binding | Recommended | Each options class has a constant `SectionName`. Each setup method binds that section with `BindConfiguration`, and then applies its `Action<TOptions>`. The configuration binding source generator creates the binding code. | Options only from `Action<TOptions>` callbacks. |
| D66 | Test projects | Confirmed | One test project for each production project and each level, named `{Project}.Tests.Unit`, `{Project}.Tests.Integration`, or `{Project}.Tests.Performance`, in `tests/` (section 5.5). The level is in the path, so one glob selects each level. | One test project for all code. |
| D67 | Contract base classes | Confirmed | The use-case tests of each domain interface are in one abstract generic base class `{Interface}TestsBase<T>`. Each implementation gets the full coverage with one subclass that only builds the implementation (section 5.5). | Separate tests for each implementation. |
| D68 | Test access | Recommended | Tests reach the code only through its public API and the DI setup methods. No `InternalsVisibleTo`. A type that needs its own test is public for a reason of its own, or it is tested through the public type that uses it. | `InternalsVisibleTo` for the test projects. |
| D69 | Test data | Recommended | Factory methods with valid defaults and named parameters, in each test project. No test data library. | Bogus, with a fixed seed. |
| D70 | Coverage | Recommended | `coverlet.collector` in each unit and integration test project. `coverage.ps1` writes an HTML report with ReportGenerator, a local tool in `dotnet-tools.json`, and fails when the line coverage of the production code is less than 90 %. An uncovered line is a behavior with no test, not a number to raise. | No coverage data. Coverage as a diagnostic with no lower limit. |
| D71 | Level of the SQLite tests | Confirmed | The tests of the SQLite stores, also in the example app, are unit tests. SQLite runs in process, and its only dependency is a database file in a temporary folder, which a unit test may use. This is a deliberate exception to the rule that a database test is an integration test. | Integration tests for SQLite. |
| D72 | Subscription levels | Confirmed | Each message type has three owner levels: default, company, and user. The overridable subscriptions merge for each channel: for one user and one channel, the most specific level with a subscription or an opt-out for that channel applies. An opt-out for all channels covers each channel of its level. Each level can have several channels, each with its own settings. | The most specific level with a subscription replaces the levels above it, with all its channels. |
| D73 | User and company ids | Confirmed | `UserId`, `CompanyId`, and `TenantId` (D114) hold strings. `From<T>` maps any `ISpanFormattable` id, for example `int`, `long`, or `Guid`, with the invariant culture, and `TryAs<T>` maps it back (section 4.4). | A generic id type parameter in each API. |
| D74 | Scope of a delivery | Confirmed | An event has a user scope or a company scope. For a user scope, each user gets the message. For a company scope, a channel with group delivery sends one message to the company, for example to the SignalR group of the company, and SignalR delivers it to the active sessions of all users of the company. | User scopes only. |
| D75 | Company scope on other channels | Recommended | A channel without group delivery, for example email, sends to each user of the company, with the resolution of that user. The per-user resolution skips the channels with group delivery, so that no user gets a message two times. | One message to an address of the company. |
| D76 | Recipient directory | Recommended | The consumer implements `IRecipientDirectory`. It gives the company, the UI language, and the channel addresses of each user, the default language and the channel addresses of a company, for example the email address of the company administrator, and the users of a company. | Addresses and languages in each subscription. |
| D77 | Channel descriptors | Confirmed | Each channel package registers a `ChannelDescriptor<TSettings>` with its `ChannelFeatures` (languages, rich text, raw data, group delivery), a typed `ChannelSettings` record as `TSettings`, and an `IChannelSettingsValidator`. The compiler checks that the settings type derives from `ChannelSettings`, and that the JSON type information of `AddChannelType` has the same settings type. The registry gives each channel as the base record `ChannelDescriptor`. | The same settings fields for all channels. A `Type SettingsType` parameter, which accepts each type and fails each route at run time for a wrong type. |
| D78 | Subscription store | Confirmed | `MsgHatch.Subscriptions` defines `ISubscriptionStore` for the subscriptions of all levels, and resolves the levels itself. `MsgHatch.Subscriptions.Data.FileSystem`, `.Data.Sqlite`, `.Data.EntityFrameworkCore`, and `.Data.MongoDb` implement it. | The consumer implements the store and the resolution. |
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
| D94 | Expiry of an outgoing message | Confirmed | `Expiry` is optional, and null means no expiry. SignalR has a default of 5 minutes, because an old UI update has no value. Email and the broker have no default expiry, because a broker message can be an integration event that another service needs, also after a long outage. A message type that gets stale sets its own expiry with `WithRetry` (D87). A message that a pause or an outage holds back past its expiry gets an `Expired` try, and is not sent. | A required expiry for each channel. It drops broker events after a long outage with no warning, and it cannot express "never", because the range stops at 30 days. |
| D95 | Routing contract | Confirmed | Core MsgHatch routes each event through `IMessageRouter<TEvent>`: the received message goes in, and the message routes come out. A route is one target on one channel, with its channel settings, language, address, template scope, and trace. How the router decides, what it stores, and how configurable it is, is the job of the router. A simple router is a few lines of consumer code (section 4.6). | The subscription model in core. Each consumer then gets the three owner levels, their stores, and their management API, also when it needs none of them. |
| D96 | Composer contract | Confirmed | Each channel registration composes its envelopes through `IEnvelopeComposer<TEvent, TEnvelope>`: the received message and the routes of one channel go in, and one envelope for each route comes out. The payload loader and the template engine are parts of the library composers, not steps of the pipeline (section 4.8). | A payload loader step in the pipeline, and a composer that receives the payloads. The pipeline then knows how each channel gets its data. |
| D97 | Subscription packages | Confirmed | The three-level subscription model is the router package `MsgHatch.Subscriptions`, with its own family: `MsgHatch.Subscriptions.Data.FileSystem`, `.Data.Sqlite`, `.Data.EntityFrameworkCore`, `.Data.MongoDb`, `.Caching.Hybrid`, `.Caching.FusionCache`, and `.AspNetCore`. The core packages contain no subscription code (section 4.17). | The subscription stores in the core data packages. Each consumer of the SQLite outbox then also gets the subscription package. |
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
| D120 | EF Core stores | Confirmed | `MsgHatch.Data.Sqlite` stays a small package with its own SQL (D56). The package `MsgHatch.Data.EntityFrameworkCore` adds stores on an `IDbContextFactory`, with SQL Server as its first provider and a claim statement for each provider. On SQL Server, the claim is a CTE with `ORDER BY NextTryAtUtc`, because `UPDATE TOP` takes rows in no order (section 4.5). | EF Core stores in place of the SQLite package. |
| D121 | MongoDB stores | Recommended | The package `MsgHatch.Data.MongoDb` stores each message as one document, with its tries in an embedded array, through the official MongoDB driver. A claim is a loop of `findOneAndUpdate` calls, one for each message (section 4.5). | One `updateMany` with a claim id, and then a find. A stop between the two calls leaves claimed messages that only the lease frees. |
| D122 | Roadmap phases | Confirmed | Each phase ends in a tested and working state. Phase 1 reaches the outbox with the in-memory stores and no dispatcher. The dispatch loop, each channel, each storage technology, the caching, and each administration API come in their own phases (roadmap). | Phases by layer. A working system then exists only after several phases. |
| D123 | Templates of a tenant | Recommended | The template store of a tenant is a tenant service (D126), and the shared template store is one service for all tenants. The template selector tries the template store of the tenant, with the scope and then with no scope. Then it tries the shared template store, with the scope and then with no scope. It does this for the language, and then for the default language. With no tenant, it tries only the shared store, also with the scope, so the company templates of D80 apply (section 4.10). | Templates for all tenants only. |
| D124 | Integration tests | Recommended | The EF Core and MongoDB phases test their stores in `.Tests.Integration` projects, against a real SQL Server and a real MongoDB in a container, with Testcontainers. The stage after the merge runs them (section 5.5). | Unit tests with an in-memory database provider. They do not test the claim statement. |
| D125 | A store for each tenant | Recommended | A storage package can keep one storage for each tenant, for example one SQLite file for each tenant, through its own setup method, for example `UseSqliteTenantStores<TTenantSource>`. The keyed tenant store of a tenant opens the storage of that tenant, and the key `TenantKey.None` opens the system storage. Only the dispatch stores, which work for all tenants, need a library composite: it claims from the storage of each tenant of `ITenantSource`, and keeps the down states in the system storage (section 4.5). | One shared storage with a tenant column only. |
| D126 | Keyed tenant services | Confirmed | Each tenant service is a keyed DI service with a `TenantKey` key, and no method of it has a tenant parameter. A setup method registers it with `KeyedService.AnyKey`, and adds an unkeyed registration that resolves the key `TenantKey.None`. So a setup with one tenant injects each service with no key, and a setup with tenants resolves the service of each tenant with `TenantKey.For(tenant)`. An implementation that needs its tenant takes a `TenantKey` parameter in its constructor, and the setup method creates it with that key. Each tenant service that it needs has the same key. The pipeline resolves the services of an event with the tenant of its message context, and the inbox processor with the tenant of the received message (section 4.6). | A `TenantId? tenant` parameter on each method of each service. Each signature then shows the tenant, and a null has two meanings. |
| D127 | Tenant stores and dispatch stores | Confirmed | Each storage of messages has two parts. The tenant store (`IInboxStore`, `IOutboxStore`) is a tenant service: it adds, reads, counts, retries, and cancels the messages of one tenant. The dispatch store (`IInboxDispatchStore`, `IOutboxDispatchStore`) is one unkeyed service for all tenants: it claims the due messages, records the tries, keeps the down states, and deletes test data and old messages. One class can implement both parts (section 4.5). | One store interface with a tenant filter for the queries and none for the claim. |
| D128 | Purge | Confirmed | `PurgeAsync` of each dispatch store deletes up to a maximum count of finished messages that finished before a time. `IDeliveryManager.PurgeAsync` and the endpoint `POST {prefix}/purge` call it. The host selects the retention, and the window of the duplicate detection is the same as the retention (section 4.19). | No delete. The stores then grow with no limit, and each claim index grows too. |
| D129 | One transport for each channel | Confirmed | `ITransport` is not generic. It has the channel, `SendAsync(OutgoingMessage, string? redirectTo, CancellationToken)`, and `CheckHealthAsync`. A channel package registers one transport for its channel, so the health check of a down channel has one transport to call. The transport gets the channel envelope from `OutgoingMessage.Content` (section 4.7). | One generic transport and one outgoing handler for each envelope type. A channel package then cannot register a transport for payload types that it does not know. |
| D130 | Singleton lifetimes | Confirmed | The library stores, transports, template stores, and registries are thread-safe singletons. The outbox dispatcher makes no DI scope for each outgoing message. Only a received message gets a DI scope, because the routers and the payload loaders of the consumer run in it: the inbox processor makes one for each claimed message, and a caller of `ProcessAsync` or `AcceptAsync` uses its own scope, for example the scope of an HTTP request. A store of the consumer must be thread-safe too, for example an EF Core store with an `IDbContextFactory` (section 4.7). | Scoped stores, with a DI scope for each claim and each message. Each send then pays for a scope, and a store with state across scopes needs a second lifetime. |
| D131 | Telemetry API | Confirmed | MsgHatch gives traces and metrics through `ActivitySource` and `Meter` of .NET, both named `MsgHatch`. `MsgHatchTelemetry.SourceName` and `MeterName` give the names, and a host turns them on with `AddSource` and `AddMeter` of OpenTelemetry. The spans are at the pipeline and at each send, not at each store call (section 4.21). | A `MsgHatch.OpenTelemetry` package with `AddMsgHatchInstrumentation()` on `OpenTelemetry.Api`. It holds two methods of one line each, and it is one more package to keep current. |
| D132 | Trace context of a message | Confirmed | Each received and outgoing message keeps the W3C `traceparent` of the span that made it. The `process` span of the inbox processor and each `send` span start a new trace, with a span link to this context, as the messaging semantic conventions describe for a send with a separate creation context. So a retry hours later does not stretch the trace of the event (section 4.21). | A parent and child relation. One trace then shows the event and each send, but a trace can last for hours of retries. |
| D133 | Tenant in telemetry | Confirmed | The spans have the tenant as the attribute `msghatch.tenant`. The metrics have no tenant tag, so many tenants do not multiply the time series, and no tenant id goes to the metrics backend. The OpenTelemetry .NET SDK keeps at most 2,000 tag combinations for each metric by default, and merges the rest into one overflow series. An opt-in option can come later with no breaking change (section 4.21). | A tenant tag on each metric, always or with an option. |
| D134 | Broker library | Confirmed | `MsgHatch.Broker` holds the broker channel and the contract `IBrokerClient`. Each broker library has its own client package, in the same way as email has `IEmailClient`. The first client is `MsgHatch.Broker.Wolverine`, because Wolverine has the MIT license and many transports. MassTransit 9 and later need a commercial license, and the support of MassTransit 8 ends on 2026-12-31, so a MassTransit client is a later phase (section 6). A direct client of one broker SDK is the second form of a client. The outbox, the retries, the down state, and the history of MsgHatch already exist, so a bus framework adds little to one publish. The first direct client is `MsgHatch.Broker.AmazonSns`, in the later phases. | One `MsgHatch.MassTransit` package with the channel and the transport together. A second broker library then needs a second copy of the channel. Or a framework with its own outbox, for example Brighter or CAP, which duplicates the MsgHatch outbox (section 7). |
| D135 | Envelope and loader of each channel | Confirmed | Each channel has its own envelope type. An envelope that carries the payload is generic over the payload type, for example `SignalREnvelope<TPayload>` and `BrokerEnvelope<TPayload>`. The email envelope `EmailMessage` carries no payload. So two channel registrations of one message type never share a composer `IEnvelopeComposer<TEvent, TEnvelope>`, also when they have the same payload type. Two channels of one message type with the same payload type use the same payload loader `IPayloadLoader<TEvent, TPayload>`. In one DI scope, their composers get the same loader instance, and each composer calls it with the routes of its own channel. The startup validation enforces the rule: a message type has at most one registration for each channel, its channel registrations have different envelope types, and each of its payload types has one loader type. | The payload itself as the envelope of SignalR, and a startup check that rejects two channel registrations with one envelope type. Or one composer registration for each channel registration, which needs a second DI key next to the tenant key. |

The field name `LastTryAtUtc` follows the naming rule for UTC times (section 5.3).

## 4. Architecture

### 4.1 Flow and components

| Flow step | Library | Consumer implements |
|---|---|---|
| 1 Trigger | `INotificationService`, `ReceivedMessage<TEvent>`, `IInboxProcessor` | Domain event records, their `JsonSerializerContext`, and an inbox store when it uses the inbox |
| 2 Route | `IMessageRouter<TEvent>`, `MessageRoute`, `RouteGrouping`. The package `MsgHatch.Subscriptions` with `SubscriptionRouter<TEvent>`, `SubscriptionResolver`, `ISubscriptionManager`, and its stores. | A router. Or, for the subscription router, `IRecipientDirectory` and a scope function for each message type. |
| 3 Templates | `IEmailTemplateEngine<TPayload>`, the placeholder engine, the template selector, the template parser and cache, `EmbeddedResourceTemplateStore`, `FileSystemTemplateStore` | `ITemplateStore`, only for other storage, for example a database |
| 4 Data | `IPayloadLoader<TEvent, TPayload>`, which the library composers call | Template payload types and their loaders |
| 5 Generate | `IEnvelopeComposer<TEvent, TEnvelope>`, one library composer for each channel, the generated placeholder writers, and the placeholder renderer | A composer, only for an own channel |
| 6 Store | `NotificationPipeline`, `DeterministicId`, the content type registry, `MsgHatchJson`, and the stores | `IOutboxStore` and `IOutboxDispatchStore`, only for other storage |
| 7 Publish | `IOutboxDispatcher`, `PublishTry`, and one `ITransport` for each channel | A transport, only for an own channel |

Steps 3 to 5 run inside the composer of each channel registration (D96). The pipeline knows only the router and the composers.

### 4.2 Projects

| Project | Target | References | Packages | Content |
|---|---|---|---|---|
| `src/MsgHatch.Domain` | `net10.0` | none | `Microsoft.Extensions.DependencyInjection.Abstractions`, `Microsoft.Extensions.Logging.Abstractions`, `Microsoft.Extensions.Options`, `Microsoft.Extensions.Options.ConfigurationExtensions`, `Microsoft.Extensions.Diagnostics` | Failures, identities, the tenant key, the routing contract, the registry reader and writer, delivery types, persistence, the in-memory stores, pipeline, inbox, outbox, dispatch, telemetry, and the DI builder |
| `src/MsgHatch.SourceGeneration` | `netstandard2.0` | none | `Microsoft.CodeAnalysis.CSharp`, `Microsoft.CodeAnalysis.Analyzers` | The generator of placeholder writers (D18) |
| `src/MsgHatch.Email` | `net10.0` | `MsgHatch.Domain`, and `MsgHatch.SourceGeneration` as an analyzer | none | The email channel, the template engine interface, the placeholder engine, the template stores, and `IEmailClient` |
| `src/MsgHatch.Email.AmazonSES` | `net10.0` | `MsgHatch.Email` | `AWSSDK.SimpleEmailV2` | The SES implementation of `IEmailClient` |
| `src/MsgHatch.SignalR` | `net10.0` | `MsgHatch.Domain` | `<FrameworkReference Include="Microsoft.AspNetCore.App" />` | The SignalR channel |
| `src/MsgHatch.Broker` | `net10.0` | `MsgHatch.Domain` | none | The broker channel, the broker composer and transport, and `IBrokerClient` |
| `src/MsgHatch.Broker.Wolverine` | `net10.0` | `MsgHatch.Broker` | `WolverineFx` | The Wolverine implementation of `IBrokerClient` (D134) |
| `src/MsgHatch.Data.FileSystem` | `net10.0` | `MsgHatch.Domain` | none | The JSON file inbox and outbox stores (D26) |
| `src/MsgHatch.Data.Sqlite` | `net10.0` | `MsgHatch.Domain` | `Microsoft.Data.Sqlite` | The SQLite inbox and outbox stores (D55) |
| `src/MsgHatch.Data.EntityFrameworkCore` | `net10.0` | `MsgHatch.Domain` | `Microsoft.EntityFrameworkCore.Relational` | The EF Core stores, with SQL Server as the first provider (D120) |
| `src/MsgHatch.Data.MongoDb` | `net10.0` | `MsgHatch.Domain` | `MongoDB.Driver` | The MongoDB stores (D121) |
| `src/MsgHatch.AspNetCore` | `net10.0` | `MsgHatch.Domain` | `<FrameworkReference Include="Microsoft.AspNetCore.App" />` | The HTTP endpoints of the delivery management API (D107) and of the registry administration (D117) |
| `src/MsgHatch.Subscriptions` | `net10.0` | `MsgHatch.Domain` | none | The subscription router, the subscription model, the resolution, the management API, the subscription cache wrapper, and the routing verifier (D97) |
| `src/MsgHatch.Subscriptions.Data.FileSystem` | `net10.0` | `MsgHatch.Subscriptions`, `MsgHatch.Data.FileSystem` | none | The JSON file subscription store |
| `src/MsgHatch.Subscriptions.Data.Sqlite` | `net10.0` | `MsgHatch.Subscriptions`, `MsgHatch.Data.Sqlite` | none | The SQLite subscription store |
| `src/MsgHatch.Subscriptions.Data.EntityFrameworkCore` | `net10.0` | `MsgHatch.Subscriptions`, `MsgHatch.Data.EntityFrameworkCore` | none | The EF Core subscription store |
| `src/MsgHatch.Subscriptions.Data.MongoDb` | `net10.0` | `MsgHatch.Subscriptions`, `MsgHatch.Data.MongoDb` | none | The MongoDB subscription store |
| `src/MsgHatch.Subscriptions.Caching.Hybrid` | `net10.0` | `MsgHatch.Subscriptions` | `Microsoft.Extensions.Caching.Hybrid` | The HybridCache backend of the subscription cache (D39) |
| `src/MsgHatch.Subscriptions.Caching.FusionCache` | `net10.0` | `MsgHatch.Subscriptions` | `ZiggyCreatures.FusionCache` | The FusionCache backend of the subscription cache (D39) |
| `src/MsgHatch.Subscriptions.AspNetCore` | `net10.0` | `MsgHatch.Subscriptions` | `<FrameworkReference Include="Microsoft.AspNetCore.App" />` | The HTTP endpoints of the subscription management API (D54) |
| `tests/{Project}.Tests.{Level}` | `net10.0`, `Exe` | The production project under test, and the test project with its contract base classes | The test packages of section 5.5 | One test project for each production project and each level (D66, section 5.5) |
| `examples/ExampleApp` | `net10.0`, `Microsoft.NET.Sdk.Web` | The `src` projects of the finished phases (roadmap), and `MsgHatch.SourceGeneration` as an analyzer | `OpenTelemetry.Extensions.Hosting`, `OpenTelemetry.Exporter.OpenTelemetryProtocol` | The subscription example end to end, as a web app |

An analyzer reference is `<ProjectReference Include="..." OutputItemType="Analyzer" ReferenceOutputAssembly="false" />`. The `MsgHatch.Email` package includes the generator assembly under `analyzers/dotnet/cs`, so that a consumer gets the generator with the email package.

### 4.3 The generic boundary

The pipeline is generic in `TEvent`. The dispatchers, the stores, and the transports are not generic. Two closed generic classes connect them to the typed components. The DI registration creates each class one time, so no reflection runs for each message:

- `MessageRegistration<TEvent> : IMessageHandler` holds the channel registrations of `TEvent`. The inbox processor uses it to run the pipeline for a claimed `ReceivedMessage<TEvent>`. The pipeline gets `IMessageRouter<TEvent>` from the DI scope of the event, with the tenant key of the event (D126).
- `ChannelRegistration<TEvent, TEnvelope> : IChannelRegistration<TEvent>` gets the composer of the channel with the same tenant key, calls it, and builds the outgoing messages.

The outbox dispatcher needs no generic class. It gives the claimed `OutgoingMessage` to the `ITransport` of its channel, and the transport reads the envelope from `OutgoingMessage.Content` (D129).

Lookups use a `FrozenDictionary` by content type and version.

### 4.4 Data types

Failures (`MsgHatch.Domain.Failures`, D50):

```csharp
public sealed record ErrorType(int Code, string Name, bool IsTransient);

public sealed record Error(ErrorType Type, string? Message = null, Exception? Exception = null);

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
public enum DeliveryTargetKind { User, Company }

public readonly record struct DeliveryTarget(DeliveryTargetKind Kind, string Id);

public abstract record ChannelSettings;

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
- A route is one delivery target on one channel. A delivery target is a user or a company. Its `Id` is the text of the `UserId` or of the `CompanyId`.
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
- `SubscriptionResolver.Resolve` returns a `SubscriptionResolution` with the deliveries and the opt-out decisions. An empty `Skipped` list is `Array.Empty<SkippedSubscription>()`, so a trace with nothing skipped allocates nothing more.
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
- `MessageFlags.Test` marks a test message. The flag goes from the received message to each outgoing message. The stores persist it, so that `IDeliveryManager.DeleteTestDataAsync` can remove all test data after a test, and analytics can leave test data out.
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

Email (`MsgHatch.Email`):

```csharp
public static class EmailChannel
{
    public static readonly ChannelId Id = new("Email");
    public static readonly ChannelDescriptor<EmailChannelSettings> Descriptor =
        new(Id, "Email", ChannelFeatures.Languages | ChannelFeatures.RichText);
}

public enum EmailFormat { Html, PlainText }

public sealed record EmailChannelSettings(string? Language = null, EmailFormat Format = EmailFormat.Html)
    : ChannelSettings;

public sealed record EmailMessage(
    string To, string Subject, string? HtmlBody, string? TextBody = null,
    string? From = null, IReadOnlyList<string>? Cc = null, IReadOnlyList<string>? Bcc = null);

public sealed record RenderedEmail(string Subject, string? HtmlBody, string? TextBody);
```

An `EmailMessage` has at least one body. The format `PlainText` sends only the text body. The `EmailMessage` is the channel envelope of each email, with no copy of the payload (D17).

SignalR (`MsgHatch.SignalR`) and the broker (`MsgHatch.Broker`):

```csharp
public static class SignalRChannel
{
    public static readonly ChannelId Id = new("SignalR");
    public static readonly ChannelDescriptor<SignalRChannelSettings> Descriptor = new(
        Id, "UI notification",
        ChannelFeatures.Languages | ChannelFeatures.RawData | ChannelFeatures.GroupDelivery);
}

public sealed record SignalRChannelSettings(string? Language = null) : ChannelSettings;

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

The SignalR transport sends `ISignalREnvelope.Payload` with the message type name as the method name, to the target of `OutgoingMessage.Target`. The broker transport gives `IBrokerEnvelope.Payload` and the headers to `IBrokerClient`, which publishes the payload with its run-time type (D134). So no envelope copies the target or the method (D17).

Channel features (D77):

| Channel | Languages | Rich text | Raw data | Group delivery |
|---|---|---|---|---|
| Email | yes | yes, `EmailFormat.Html` | no | no |
| UI notification (SignalR) | yes | no | yes, the payload as JSON | yes, one message to the SignalR group of a company |
| Broker message | no | no | yes, the payload as it is | yes, one message with the company target |

A null language in channel settings means the UI language of the user, or the default language of the company for a company target.

Options (D49, D63, D64, D65). Each options class is a `sealed class` with a public parameterless constructor, a `get; set;` property for each setting, the default value in the property initializer, and a `public const string SectionName`. A `[OptionsValidator]` partial class next to it, for example `DispatchOptionsValidator`, validates the attributes. A rule across properties is in `IValidatableObject.Validate`.

| Class | Section | Property | Default | Validation |
|---|---|---|---|---|
| `MsgHatchOptions` | `MsgHatch` | `DefaultLanguage` | `"en"` | `[Required]`. The culture must be known to `CultureInfo.GetCultureInfo`. |
| | | `EnableLogging` | `true` | none |
| | | `Roles` | `Consumer, Publisher` | Not `None` (D105) |
| `RetryOptions` (named: `Inbox`, the channel name, or the content type of a channel registration) | `MsgHatch:Retry:{Name}` | `MaxTryCount` | 8 (SignalR: 3) | `[Range(1, 100)]` |
| | | `BaseDelay` | 5 seconds | `[Range(typeof(TimeSpan), "00:00:00.001", "01:00:00")]` |
| | | `MaxDelay` | 15 minutes | `[Range(typeof(TimeSpan), "00:00:01", "7.00:00:00")]`, and not less than `BaseDelay` |
| | | `Expiry` | none (SignalR: 5 minutes) | `[Range(typeof(TimeSpan), "00:00:01", "30.00:00:00")]` when it has a value. Null means no expiry (D94). |
| `DispatchOptions` (named: `Inbox` or `Outbox`) | `MsgHatch:Dispatch:{Name}` | `MaxDegreeOfParallelism` | 8 | `[Range(1, 256)]` |
| | | `QueueCapacity` | 16 | `[Range(1, 4096)]`, and at least `MaxDegreeOfParallelism` |
| | | `Lease` | 60 seconds | `[Range(typeof(TimeSpan), "00:00:05", "01:00:00")]` |
| | | `LeaseSafetyMargin` | 10 seconds | `[Range(typeof(TimeSpan), "00:00:00", "00:30:00")]`, and less than `Lease` |
| | | `PollInterval` | 1 second | `[Range(typeof(TimeSpan), "00:00:00.010", "00:10:00")]` |
| | | `MaxPollInterval` | 30 seconds | `[Range(typeof(TimeSpan), "00:00:00.010", "01:00:00")]`, and not less than `PollInterval` |
| | | `EnablePolling` | `true` | none. False only where a signal reaches each loop, for example in one process (D102, D103). |
| `ChannelDownOptions` (named: the channel name) | `MsgHatch:ChannelDown:{Name}` | `ErrorThreshold` | 5 | `[Range(1, 1000)]` |
| | | `CheckInterval` | 30 seconds | `[Range(typeof(TimeSpan), "00:00:01", "01:00:00")]` |
| `TestDeliveryOptions` (named: the channel name) | `MsgHatch:TestDelivery:{Name}` | `Mode` | `Suppress` | `TestDeliveryMode.Suppress`, `Deliver`, or `Redirect` |
| | | `RedirectTo` | none | `[MaxLength(256)]`. Required when `Mode` is `Redirect`. |
| `SubscriptionCacheOptions` | `MsgHatch:SubscriptionCache` | `Expiration` | 1 minute | `[Range(typeof(TimeSpan), "00:00:01", "1.00:00:00")]` |
| | | `KeyPrefix` | `"msghatch:sub"` | `[Required]`, `[RegularExpression("^[A-Za-z0-9:_-]+$")]` |
| `FileStoreOptions` | `MsgHatch:FileStores` | `RootPath` | none | `[Required]` |
| `SqliteStoreOptions` | `MsgHatch:Sqlite` | `ConnectionString` | none | `[Required]`. It must not contain `{tenant}`, because one database for each tenant has its own setup method (D125). |
| `SqliteTenantStoreOptions` | `MsgHatch:SqliteTenants` | `TenantConnectionString` | none | `[Required]`. It must contain `{tenant}` (D125). |
| | | `SystemConnectionString` | none | `[Required]`. It must not contain `{tenant}`. It names the database of the system storage, for the key `TenantKey.None`. |
| `EntityFrameworkStoreOptions` | `MsgHatch:EntityFramework` | `Schema` | `"msghatch"` | `[Required]`, `[RegularExpression("^[A-Za-z_][A-Za-z0-9_]*$")]` |
| `MongoDbStoreOptions` | `MsgHatch:MongoDb` | `ConnectionString` | none | `[Required]` |
| | | `DatabaseName` | `"msghatch"` | `[Required]`, `[RegularExpression("^[A-Za-z0-9_-]+$")]` |
| `RegistryOptions` | `MsgHatch:Registry` | `RefreshInterval` | 30 seconds | `[Range(typeof(TimeSpan), "00:00:01", "01:00:00")]` (D118) |
| `FileTemplateOptions` | `MsgHatch:FileTemplates` | `RootPath` | none | `[Required]` |
| `AmazonSesOptions` | `MsgHatch:AmazonSes` | `SenderEmail` | none | `[Required]`, `[EmailAddress]` |
| | | `SenderName` | none | `[MaxLength(100)]` |
| | | `Region` | none, so that the AWS SDK selects the region from its configuration | `[RegularExpression("^[a-z]{2}(-[a-z]+)+-[0-9]$")]` when it has a value |

Each setup method registers its options in this order:
1. `services.AddOptions<DispatchOptions>(name).BindConfiguration(...)` with the `SectionName` of the options class, so that a value from the configuration applies first.
2. `.Configure(configure)` with the `Action<TOptions>` of the setup method, so that a value from code wins.
3. `.ValidateOnStart()`, and the generated validator as `IValidateOptions<TOptions>` with `TryAddEnumerable`.

The code reads a setting only through `IOptions<TOptions>`, `IOptionsSnapshot<TOptions>`, or `IOptionsMonitor<TOptions>`.

Retry options of a channel registration (D87). Each channel registration of a message type registers a named `RetryOptions` with its content type as the name, for example `Email:OrderShippedEmail`. It repeats the steps of the channel options, and then adds its own steps:
1. `.Configure(...)` sets the default values of the channel, from the `retry` parameter of `AsChannel` (section 4.7).
2. `.BindConfiguration("MsgHatch:Retry:{Channel}")` applies the section of the channel.
3. `.BindConfiguration("MsgHatch:Retry:{ContentType}")` changes only the values that the section of the registration has. This section is inside the section of the channel, and the binding of the channel options ignores it.
4. `.Configure(...)` applies the action of `ChannelBuilder.WithRetry`, so that a value from code wins.
5. `.ValidateOnStart()`, with the same generated validator.

The steps do not read the channel options through `IOptionsMonitor<RetryOptions>`, because a configure step of `RetryOptions` that needs `IOptionsMonitor<RetryOptions>` is a circular dependency in the DI container.

The pipeline and the dispatcher read the retry options of a message by its content type, never by its channel. For example:

```csharp
.AsEmail<PasswordResetEmail, PasswordResetEmailLoader>(
    "PasswordResetEmail",
    p => p.WithRetry(o => { o.MaxTryCount = 3; o.Expiry = TimeSpan.FromMinutes(15); }))
```

```json
{ "MsgHatch": { "Retry": { "Email": { "MaxTryCount": 8, "WeeklyReportEmail": { "Expiry": "1.00:00:00" } } } } }
```

Each subscription store package uses the options class of its core data package, for example `SqliteStoreOptions`, so one setting serves the outbox and the subscriptions (D59).

`AmazonSesOptions` gives the sender address `"{SenderName} <{SenderEmail}>"`. It gives only `SenderEmail` when `SenderName` is empty, or when `SenderEmail` already contains `<` and `>`.

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
| NoSQL database, for example MongoDB | One BSON document for each message, with class maps from the content type registry, and an index on `channel`, `status`, and `nextTryAtUtc`. `MsgHatch.Data.MongoDb` ships `MongoDbOutboxStore` (D121). | An embedded array. The store adds a try with `$push` in the same update as the delivery state. | An atomic conditional update of each document, for example `findOneAndUpdate`. |
| SQLite | Columns for the delivery state, the content type, and the versions. A JSON text column for the channel envelope, written with `MsgHatchJson`. `MsgHatch.Data.Sqlite` ships `SqliteOutboxStore` (D55). | A JSON array column `Tries` of the message row. One `UPDATE` adds the try and changes the delivery state (D27). | An `UPDATE ... RETURNING` of the first due rows. SQLite allows one writer at a time, so the claim is atomic. |
| SQL Server | Columns for the delivery state, the content type, and the versions. A JSON column for the channel envelope, written with `MsgHatchJson`. `MsgHatch.Data.EntityFrameworkCore` ships `EntityFrameworkOutboxStore` (D120). | A JSON array column `Tries` of the message row. One `UPDATE` adds the try and changes the delivery state (D27). | A CTE that selects the first due rows with `ORDER BY NextTryAtUtc` and the hints `ROWLOCK, READPAST, UPDLOCK`, and an `UPDATE` of the CTE with an `OUTPUT` clause (D120). |

The inbox uses the same patterns, with no tries (D29).

The outbox dispatch store also keeps the down state of each channel (D106). A SQL store has one row for each down channel, a NoSQL store one document, and the file store one file `{channel}/down.json`. Each change of a down state is one atomic operation, and each claim of a channel reads the down state of that channel in the same operation.

In-memory stores (D119). `InMemoryInboxStore` and `InMemoryOutboxStore` in `MsgHatch.Domain` keep the messages, the tries, and the down states in dictionaries, with one lock for each store. They follow each rule of section 4.6 and pass the contract base classes, so they are also the reference for the other stores. `UseInMemoryStores()` selects them. They have the comment `ponytail: not durable and for one process, use a storage package for production`. `InMemoryOutboxStore.GetMessages()` gives a copy of the stored messages of all tenants, in the order of their insert, for example for a test.

Store parts (D127, D130). Each library store class has the two parts of one storage:
- The class implements the dispatch store, `IInboxDispatchStore` or `IOutboxDispatchStore`, for all tenants. Its setup method registers it as a thread-safe singleton.
- `For(TenantKey key)` of the class gives the tenant store of one key, `IInboxStore` or `IOutboxStore`. It is a small view that keeps the key, and reads and writes only the messages of its tenant.
- The setup method registers the tenant store with `KeyedService.AnyKey` through `For(key)`, and the unkeyed tenant store for `TenantKey.None` (D126).
- So all parts share one storage, one lock or connection pool, and one schema setup. A consumer store can use the same form, or two classes on one storage.

Tenants (D114). Each store keeps the tenant of each message and each subscription in its own field: a column, a document field, or a JSON property. A SQL store writes an empty text for no tenant, so that a unique index treats "no tenant" as one value. The tenant store of a key reads and writes only the data of its tenant. The dispatch store claims the messages of all tenants, and each claimed message keeps its tenant. The key of a received message is its tenant and its id, because each caller gives the message id, and two tenants can give the same id. The id of an outgoing message is unique, because `DeterministicId` uses the tenant (section 4.8).

A store for each tenant (D125). A storage package can keep one storage for each tenant through its own setup method. The setup methods of one shared storage do not change. For SQLite:
- `UseSqliteTenantStores<TTenantSource>` makes the file of each tenant from `TenantConnectionString`, for example `Data Source=data/msghatch-{tenant}.db`. It replaces `{tenant}` with the tenant id, escaped with `Uri.EscapeDataString`. The key `TenantKey.None` uses `SystemConnectionString`, for example `Data Source=data/msghatch.db`.
- It registers the store class as a keyed singleton with `KeyedService.AnyKey`, one instance for each key, on the file of that key. The tenant store of a key is `For(key)` of that instance, so the tenant stores need no composite.
- It calls the core method `UseStorageForEachTenant<TTenantSource>` (section 4.7) with the four parts of the instance of each key. This method registers the tenant stores, and the library composites `TenantInboxDispatchStore` and `TenantOutboxDispatchStore` as the dispatch stores.
- `UseSqliteTenantSubscriptionStore` of `MsgHatch.Subscriptions.Data.Sqlite` registers the keyed subscription store of each key on the same file, with the same options. The subscription store has no claim, so it needs no composite.

Each dispatch composite gets a function that gives the dispatch store of one key, and keeps one instance for each key:
- The down states of the channels are only in the store of `TenantKey.None`, because a channel is global (D106). So an outbox claim first claims from that store. If that claim returns a down state, the composite claims nothing more, and returns that state.
- Then a claim asks the dispatch stores of the tenants of `ITenantSource` in turn, from the store after the last one that gave messages, until it has `maxCount` messages. `NextDueAtUtc` is the earliest value of all the stores.
- The composite keeps the next due time of each tenant and channel from each claim and each recorded try. A claim skips a tenant whose next due time is later than now. A tenant store of the same process clears the next due time of its tenant after a successful `AddAsync` or `RetryAsync`, through the internal wrapper that `UseStorageForEachTenant` puts around it. Each `MaxPollInterval`, one claim asks all tenants, so a message that another process adds waits at most that interval, as with polling.
- `RecordTryAsync`, `CompleteAsync`, and `FailAsync` go to the store of the tenant of the message.
- `CountAsync` sums the counts of all stores. `DeleteTestDataAsync` and `PurgeAsync` call each store, and `PurgeAsync` stops at `maxCount` messages in total.
- The composite has the comment `ponytail: a sweep asks each tenant store, a shared claim index or a dispatcher for each group of tenants if there are many tenants`.

Test data (D82). Each inbox and outbox record keeps its `MessageFlags`:
- SQLite: the column `Flags` (text, for example `Test`) in `MsgHatchInbox` and `MsgHatchOutbox`, with an index. `DeleteTestDataAsync` deletes the rows with the test flag, with their publish tries in the same rows.
- JSON files: a test message is stored under `{RootPath}/test/inbox` or `{RootPath}/test/outbox`, with the same folder layout. `DeleteTestDataAsync` deletes the `test` folder of its store.
- A consumer store for another storage keeps the flags in a queryable field, so that analytics can leave test data out.

The subscription stores of `MsgHatch.Subscriptions.Data.Sqlite` and `MsgHatch.Subscriptions.Data.FileSystem` (D78, D97) keep each subscription with its fields as columns, and its channel settings as JSON:
- SQLite: the table `MsgHatchSubscription` with `Id TEXT PRIMARY KEY`, `Revision`, `TenantId`, `Level`, `CompanyId`, `UserId`, `MessageType`, `Kind`, `Channel`, `SettingsType`, `SettingsVersion`, `Settings TEXT` (the channel settings as JSON), `Recipients` (the recipient kind as text), `NamedUsers TEXT` (a JSON array of user ids, or null), `Mandatory INTEGER`, `CreatedAtUtc`, and `LastModifiedAtUtc`. The subscription store of a key reads and writes only the rows of its tenant. A unique index on `(TenantId, MessageType, Level, CompanyId, UserId, Channel, Recipients)` stops a duplicate. The store writes an empty text for a missing tenant, company, user, or channel, because SQLite treats each NULL as a different value in a unique index. An index on `(TenantId, MessageType, Level, CompanyId, UserId)` serves `FindForDeliveryAsync`. `UpdateAsync` uses `UPDATE ... WHERE Id = @id AND Revision = @expected`, and a changed row count of 0 gives `Errors.Conflict`. The store can share a database file with the core SQLite stores, so it keeps its schema version in its own table `MsgHatchSubscriptionSchema(Version INTEGER)`.
- JSON files: one file `{RootPath}/subscriptions/{tenantFolder}/{MessageType}/{level}/{ownerId}/{id}.json` for each subscription. `{tenantFolder}` is `no-tenant` for the key `TenantKey.None`, and `tenants/{tenantId}` for a tenant, so the two forms never collide. `default` is the owner folder of the default level. Each id in a folder name is escaped with `Uri.EscapeDataString`, because an id such as `auth0|abc` has characters that a file name cannot have. `FindForDeliveryAsync` reads only the folders of the requested owners. A file lock on the owner folder serializes the changes of one owner. The store has the comment `ponytail: one file for each subscription and a folder lock suit small subscription sets, use a database store for many owners`.
- Each channel package registers its settings type with `MsgHatchBuilder.AddChannelType`, with the content type `Settings:{Channel}` and a version, so that the stores can read old settings versions (sections 4.7 and 4.13).
- The EF Core and MongoDB subscription stores follow the same rules (D120, D121). A consumer store for another storage also follows them, and passes `SubscriptionStoreTestsBase`.

Route traces (D98). Each router package registers its trace type with `MsgHatchBuilder.AddTraceType`, with the content type `Trace:{Name}` and a version, in the same way as the channel settings. The subscription router registers `ResolutionTrace` as `Trace:Subscription`. A router with no trace type registers nothing.

SQLite schema (D55, D56, D57). The table and column names are the C# property names:
- `MsgHatchInbox`: `TenantId` (an empty text for no tenant) and `Id`, with `PRIMARY KEY (TenantId, Id)`, the columns `Status`, `TryCount`, `MaxTryCount`, `CreatedAtUtc`, `LastTryAtUtc`, `NextTryAtUtc`, `LeaseUntilUtc`, `ExpiresAtUtc`, and `LastError` of `DeliveryState`, then `SchemaVersion`, `ContentType`, `ContentVersion`, `TraceParent` (the trace context, or null), and `Content TEXT` (the event as JSON).
- `MsgHatchOutbox`: `Id TEXT PRIMARY KEY` and the same other columns, plus `SourceMessageId`, `MessageType`, `Channel`, `TargetKind`, `TargetId`, `TraceType` (the discriminator of the route trace, for example `Trace:Subscription@1`, or null), `Trace TEXT` (the route trace as JSON, or null), and `Tries TEXT` (the publish tries as a JSON array, D27). `Content` holds the channel envelope as JSON.
- `MsgHatchChannelDown`: `Channel TEXT PRIMARY KEY`, `Reason`, `SinceUtc`, `NextCheckAtUtc`, `ErrorCode`, `ErrorName`, and `Note`, one row for each down channel (D106).
- The partial index `(NextTryAtUtc) WHERE Status = 'Pending'` in `MsgHatchInbox`, and `(Channel, NextTryAtUtc) WHERE Status = 'Pending'` in `MsgHatchOutbox` (D104). So a claim index holds only the backlog and the claimed messages.
- From phase 16: `MsgHatchChannelEntry` and `MsgHatchMessageTypeEntry`, the registry settings (section 4.20).
- For the delivery management (section 4.19): an index on `(TenantId, CreatedAtUtc, Id)` in each message table, and on `(TenantId, TargetKind, TargetId, CreatedAtUtc)` and `(SourceMessageId)` in `MsgHatchOutbox`.
- `MsgHatchSchema(Version INTEGER)` holds the schema version 1.
- Enum values are text. Times are ISO 8601 text in UTC, for example `2026-10-31T08:00:00.0000000Z` (D57).

SQLite store rules:
- Each operation opens its own connection. `Microsoft.Data.Sqlite` pools the connections.
- The store is a singleton (D130). Its first operation sets `PRAGMA journal_mode=WAL`, and creates the tables with `CREATE TABLE IF NOT EXISTS`, one time for each connection string in the process.
- A busy database waits through the command timeout: `Microsoft.Data.Sqlite` tries a locked database again until the command timeout ends. Phase 8 checks this in the pinned version. A `PRAGMA busy_timeout` is not used, because it applies to one connection only.
- `AddAsync` uses `INSERT OR IGNORE` in one transaction for the batch.
- `ClaimDueAsync` first reads, with no write lock, `MIN(NextTryAtUtc)` of the pending rows, of its channel for the outbox. The outbox claim also reads the row of its channel in `MsgHatchChannelDown`. With a down row, it claims nothing, and returns the row as `ClaimResult.Down` (D106). With no due row, it returns the minimum as `NextDueAtUtc`, and takes no write lock.
- Else, in a `BEGIN IMMEDIATE` transaction, one statement claims the rows: `UPDATE ... SET NextTryAtUtc = @leaseUntil, LeaseUntilUtc = @leaseUntil WHERE Id IN (SELECT Id ... WHERE Status = 'Pending' AND NextTryAtUtc <= @now ORDER BY NextTryAtUtc LIMIT @max) RETURNING ...`. The outbox claim also has `AND Channel = @channel` (D104). So the end of the lease is the next due time of a claimed row (D103).
- When the claim returns fewer than `maxCount` rows, a second statement in the same transaction reads `MIN(NextTryAtUtc)` of the pending rows as `NextDueAtUtc`. When it returns `maxCount` rows, `NextDueAtUtc` is now, because more rows can be due.
- `SetChannelDownAsync` with the reason `Errors` uses `INSERT OR IGNORE`, so the first loop wins. With the reason `Operator`, it uses `INSERT OR REPLACE`. `SetChannelUpAsync` uses `DELETE ... WHERE Channel = @channel`, with `AND Reason = @reason` when a reason is given. `TryStartChannelCheckAsync` uses `UPDATE ... SET NextCheckAtUtc = @next WHERE Channel = @channel AND Reason = 'Errors' AND NextCheckAtUtc <= @now`, and a changed row count of 1 means that this caller runs the check.
- `RecordTryAsync` is one statement: `UPDATE MsgHatchOutbox SET Tries = json_insert(Tries, '$[#]', json(@try)), TryCount = @tryNumber, ... WHERE Id = @id AND TryCount = @tryNumber - 1`. A changed row count of 0 means that the try is already recorded (D27).
- The database file must be on a local disk. SQLite does not support network file systems.

The library gives each store the type information that it needs (`MsgHatch.Domain.Persistence`):

```csharp
public interface IContentTypeRegistry
{
    IReadOnlyList<ContentTypeEntry> Entries { get; }
    bool TryFind(string contentType, int version, out ContentTypeEntry entry);
}

public sealed record ContentTypeEntry(
    string ContentType, int Version, string Discriminator, Type ClrType, JsonTypeInfo ContentJsonTypeInfo);
```

- The registry is read-only for the stores. Its entries come from the registrations of the message types and the channels, and from `MsgHatchBuilder.AddChannelType` and `AddTraceType` (section 4.7).
- Each registration adds one entry for each version: the current version, and each version that has an upgrade. `ClrType` is the closed type, for example `ReceivedMessage<OrderShippedV1>` for version 1.
- `Discriminator` is `{ContentType}@{Version}` (D30). A MongoDB store registers one class map for each entry, with this discriminator.
- An event content type is the registered message type name, for example `OrderShipped`. An envelope content type is `{Channel}:{Name}`, for example `Email:OrderShippedEmail`. The channel registration methods add the channel prefix. A trace content type is `Trace:{Name}`, for example `Trace:Subscription` (D98).
- `MsgHatchJson` writes a whole `ReceivedMessage` or `OutgoingMessage` as one JSON document, and reads it back as the correct closed type. It can also write and read only the event, the envelope, or the route trace, for a SQL JSON column. It uses the registered `JsonTypeInfo`, camelCase names, enum values as strings (D23), and it writes null values.
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

    // Down state of a channel (D106)
    ValueTask<ErrorOr<IReadOnlyList<ChannelDown>>> GetDownChannelsAsync(CancellationToken ct);
    ValueTask<ErrorOr<bool>> SetChannelDownAsync(ChannelDown down, CancellationToken ct);
    ValueTask<ErrorOr<bool>> SetChannelUpAsync(ChannelId channel, ChannelDownReason? reason, CancellationToken ct);
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

public interface IDeliveryManager
{
    ValueTask<ErrorOr<IReadOnlyList<ChannelStatus>>> GetChannelsAsync(CancellationToken ct);
    ValueTask<Error?> PauseChannelAsync(ChannelId channel, string? note, CancellationToken ct);
    ValueTask<Error?> ResumeChannelAsync(ChannelId channel, CancellationToken ct);
    ValueTask<ErrorOr<TrafficReport>> GetTrafficAsync(TrafficQuery query, CancellationToken ct);
    ValueTask<ErrorOr<MessagePage<ReceivedMessageSummary>>> GetReceivedMessagesAsync(InboxQuery query, CancellationToken ct);
    ValueTask<ErrorOr<MessagePage<OutgoingMessageSummary>>> GetOutgoingMessagesAsync(OutboxQuery query, CancellationToken ct);
    ValueTask<ErrorOr<OutgoingMessageDetail>> GetOutgoingMessageAsync(Guid id, CancellationToken ct);
    ValueTask<ErrorOr<ReceivedMessage>> GetReceivedContentAsync(Guid id, CancellationToken ct);
    ValueTask<ErrorOr<OutgoingMessage>> GetOutgoingContentAsync(Guid id, CancellationToken ct);
    ValueTask<ErrorOr<DeliveryActionResult>> RetryReceivedAsync(InboxQuery query, CancellationToken ct);
    ValueTask<ErrorOr<DeliveryActionResult>> CancelReceivedAsync(InboxQuery query, CancellationToken ct);
    ValueTask<ErrorOr<DeliveryActionResult>> RetryOutgoingAsync(OutboxQuery query, CancellationToken ct);
    ValueTask<ErrorOr<DeliveryActionResult>> CancelOutgoingAsync(OutboxQuery query, CancellationToken ct);
    ValueTask<ErrorOr<DeletedMessages>> DeleteTestDataAsync(CancellationToken ct);
    ValueTask<ErrorOr<DeletedMessages>> PurgeAsync(DateTime finishedBeforeUtc, int maxCount, CancellationToken ct);
}

// MsgHatch.Email
public interface IEmailClient
{
    ValueTask<ErrorOr<PublishReceipt>> SendAsync(Guid messageId, EmailMessage message, CancellationToken ct);
    ValueTask<Error?> CheckHealthAsync(CancellationToken ct);
}

public readonly record struct EmailTemplateRequest(MessageType Type, string Language, string? Scope);

public interface IEmailTemplateEngine<TPayload>
{
    ValueTask<ErrorOr<IEmailTemplate<TPayload>>> PrepareAsync(EmailTemplateRequest request, CancellationToken ct);
}

public interface IEmailTemplate<TPayload>
{
    ValueTask<ErrorOr<RenderedEmail>> RenderAsync(
        TPayload payload, MessageRoute route, CultureInfo culture, CancellationToken ct);
}

// MsgHatch.Broker
public readonly record struct BrokerPublish(
    Guid MessageId, TenantId? Tenant, object Payload, IReadOnlyDictionary<string, string> Headers);

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

public interface ISubscriptionManager
{
    ValueTask<ErrorOr<IReadOnlyList<ChannelDescriptor>>> GetChannelsAsync(CancellationToken ct);
    ValueTask<ErrorOr<IReadOnlyList<MessageTypeState>>> GetMessageTypesAsync(MessageTypeQuery query, CancellationToken ct);
    ValueTask<ErrorOr<IReadOnlyList<Subscription>>> GetSubscriptionsAsync(SubscriptionQuery query, CancellationToken ct);
    ValueTask<ErrorOr<Subscription>> GetSubscriptionAsync(Guid id, CancellationToken ct);
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

Tenant services (D126). These services are tenant services: each router, payload loader, composer, and template engine, the template store of a tenant, `IInboxStore`, `IOutboxStore`, `IDeliveryManager`, and, in `MsgHatch.Subscriptions`, `ISubscriptionStore`, `IRecipientDirectory`, `ISubscriptionCache`, `ISubscriptionManager`, and `IRoutingVerifier`. The other services exist one time for the whole system, for example the dispatch stores, the transports, `IMessageRegistry`, and `IDispatchSignal`.

Contract rules of the core:
- Each store, router, loader, composer, engine, and transport method returns an `Error` for a failure, and does not throw (section 4.12).
- A store never reads the clock. The library gives it each time that it needs (section 4.14).
- A tenant service of a key reads and changes only the data of its tenant, and each tenant service that it uses has the same key (D126, section 4.7). So a message id or a subscription id of another tenant finds nothing. A dispatch store works for all tenants (D127).
- `IMessageRouter.RouteAsync` returns the routes of one event. An empty list sends nothing, and is not an error. An error fails the event, and the inbox or the broker retries it. A router stores nothing and sends nothing, because a preview calls it too.
- `IMessageRegistry` gives each registered channel and each registered message type with its channels, from a frozen snapshot (D117). A lookup of an unknown channel or message type gives null. A read completes at once, does not allocate, and has no error result.
- `IRegistryWriter` reads, saves, and resets the registry settings (D118, section 4.20). The `Revision` of a saved entry is the expected revision, and 0 means a new entry. Only an administration host needs it. A setup in code registers no writer.
- `IRegistrySettingsStore` keeps the registry settings. `Save…EntryAsync` writes the entry only when the stored revision is `entry.Revision - 1`, and a missing entry has the revision 0. Else it gives `Errors.Conflict`. A delete of an unknown entry succeeds.
- `ITenantSource.GetTenantsAsync` gives each tenant that has its own storage (D125). Only the dispatch composites call it.
- `IEnvelopeComposer.ComposeAsync` returns one inner result for each route, in the same order. An inner error fails only that message. An outer error, for example from a payload loader, fails the event, and the inbox or the broker retries it.
- `IPayloadLoader.LoadAsync` returns one payload for each route, in the same order. It can return the same instance for many routes when the data does not depend on the target.
- `INotificationService.PreviewAsync` runs steps 3 to 5 of section 4.8 as a test message, for the given routes, or for the routes of the router of the message type when `routes` is null. It stores nothing and sends nothing. It returns one `RouteResult` for each route, in the same order (D84, D100).
- `AddAsync` of a tenant store ignores a message whose id is already stored. A partial batch is safe, because a retry writes the same ids. A message with another tenant than the key of the store gives `Errors.ValidationFailed`, and the store adds nothing of the batch.
- `ClaimDueAsync` with a `maxCount` less than 1 returns `Errors.ValidationFailed`. `DispatchPendingAsync`, `ProcessPendingAsync`, and `PurgeAsync` do the same for a count less than 1.
- `ClaimDueAsync` claims only pending messages with `NextTryAtUtc` at or before `nowUtc`. It sets `NextTryAtUtc` and `LeaseUntilUtc` to `nowUtc + lease`, so no other claim takes the message before the end of the lease (D103). It is atomic between concurrent dispatchers. It returns each message as its registered closed type, with its tenant, and without its publish tries.
- `IOutboxDispatchStore.ClaimDueAsync` claims only messages of the given channel, and leaves the messages of the other channels unchanged (D104).
- `ClaimResult.NextDueAtUtc` is the earliest `NextTryAtUtc` of the pending messages after the claim, which includes the lease ends of the claimed messages: of the given channel for the outbox, or of the inbox. It is null when no pending message exists. A value at or before `nowUtc` means that more messages are due now. A claim that returns `maxCount` messages can return `nowUtc` with no read (D103).
- `IOutboxDispatchStore.ClaimDueAsync` claims nothing for a channel with a down state, and returns that state as `ClaimResult.Down` (D106).
- `SetChannelDownAsync` with the reason `Errors` writes only when the channel has no down state, and returns true when it wrote. So the first loop that finds the errors wins, and the others change nothing. With the reason `Operator`, it always writes, and replaces a down state with the reason `Errors`.
- `SetChannelUpAsync` removes the down state of the channel, and returns true when it removed one. With a reason, it removes only a down state with that reason, so a health check never ends a pause of a person.
- `TryStartChannelCheckAsync` sets `NextCheckAtUtc` to `nextCheckAtUtc`, and returns true, only when the channel is down with the reason `Errors` and its `NextCheckAtUtc` is at or before `nowUtc`. It is atomic between concurrent publisher instances, so one publisher instance at a time runs the health check.
- `GetDownChannelsAsync` returns the down state of each down channel.
- `IDispatchSignal.NotifyAsync` returns at once, and never waits for a reader. `WaitAsync` returns null when a notification for the topic arrived after the last wait for that topic, also before the first wait. It returns `Errors.OperationCancelled` when the token is cancelled, and another `Error` when the signal does not work, for example a broken pipe. A loop then waits for the poll delay (D102).
- `ITransport.Channel` is the channel of the transport, and one transport exists for each channel (D129). `SendAsync` sends `message.Content`. With a `redirectTo` value, it sends to that test address or test group in place of the target of the message (section 4.8).
- `ITransport.CheckHealthAsync` returns null when the channel can send. A transport with nothing to check returns null.
- `IBrokerClient.PublishAsync` publishes `Payload` with its run-time type and all `Headers`. The broker transport builds the headers (phase 6), so a client adds no header of its own. It can also give `MessageId` and `Tenant` to the matching fields of its library (D134).
- `IOutboxDispatchStore.RecordTryAsync` changes the stored message of `message` in one atomic operation:
  1. It adds the publish try.
  2. It sets `TryCount` to the try number, `LastTryAtUtc` to the finish time, and `LastError` to `"{ErrorName}: {ErrorMessage}"`, or to null for a success.
  3. It sets the status: `Done` for `Succeeded` and `Suppressed`, `Failed` for `PermanentError`, `Expired`, or a `TransientError` with a null `nextTryAtUtc`, and `Pending` with the new `NextTryAtUtc` for a `TransientError` with a next try time. The outcome `Cancelled` comes only from `CancelAsync` (section 4.19).
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
            .AsEmail<OrderShippedEmail, OrderShippedEmailLoader>("OrderShippedEmail")
            .AsSignalR<OrderShippedUiUpdate, OrderShippedUiLoader>(
                "OrderShippedUiUpdate", 1, AppJson.Default.OrderShippedUiUpdate))
    .AddMessage<PasswordReset>("PasswordReset", 1, AppJson.Default.PasswordReset,
        m => m
            .WithRouter<PasswordResetRouter>()
            .AsEmail<PasswordResetEmail, PasswordResetEmailLoader>("PasswordResetEmail"));
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
- For each key, it creates the implementation with `ActivatorUtilities.CreateInstance`, and puts the wrappers around it. It gives `ActivatorUtilities` a small internal service provider for that key: this provider resolves each tenant service with the same key, and each other service as it is. So a router gets the store and the directory of its own tenant, and a composer the loader and the engine of its own tenant.
- An implementation that needs its tenant takes a `TenantKey` parameter in its constructor, and gets the key.
- It adds an unkeyed registration that resolves the service of `TenantKey.None`. So a host with one tenant injects each service with no key. The template store of a tenant is the one exception: it has no unkeyed registration, because the unkeyed `ITemplateStore` is the shared template store (section 4.10).
- A singleton tenant service has one instance for each key. A scoped tenant service has one instance for each key in each DI scope.
- Phase 1 checks these premises in the pinned version of `Microsoft.Extensions.DependencyInjection`: an `AnyKey` singleton gives one instance for each requested key, and a keyed resolution with an unknown key uses the `AnyKey` registration.

Names (D61): a method that selects an implementation starts with `Use`. A method that registers a message type starts with `Add`. A method that registers a form of a message type on one channel starts with `As`, for example `AsEmail`, because it describes the form of the message, not a destination. A method that adds an option to a message type or a channel registration starts with `With`.

Domain methods (`MsgHatch.Domain.DependencyInjection`):

| Method | Effect |
|---|---|
| `AddMsgHatch(Action<MsgHatchOptions>?)` | Registers the core services: the in-memory `IMessageRegistry` from the registrations (D117), `InProcessDispatchSignal` as the `IDispatchSignal`, `IDeliveryManager` as a tenant service with its logging wrapper, and `TimeProvider.System` with `TryAddSingleton`. Returns `MsgHatchBuilder`. |
| `AddChannelType<TSettings>(ChannelDescriptor<TSettings>, int settingsVersion, JsonTypeInfo<TSettings> settingsJson, IChannelSettingsValidator)` | Registers a channel: its descriptor, the content type `Settings:{Channel}` of its settings type with the version, and the settings validator. A second call for the same channel id changes nothing. The first `As<Channel>` registration of a channel package calls it (D77, H5). |
| `AddTraceType<TTrace>(string name, int version, JsonTypeInfo<TTrace>)` | Registers a route trace type with the content type `Trace:{name}` and the version. `TTrace` derives from `RouteTrace`. The router packages call it (D98). |
| `UseRouter(Type routerType)` | Selects an open generic router, for example `typeof(SubscriptionRouter<>)`, for each message type with no router of its own (D99). The router packages call it. The type has one type parameter `T` and implements `IMessageRouter<T>` for it. Another type gives an `ArgumentException`. The startup validation reports a message type that breaks the constraint of `T`. |
| `UseInMemoryStores()` | Selects `InMemoryInboxStore` and `InMemoryOutboxStore`, with their two parts and their logging wrappers, for a host with no database (D119). From phase 16, it also registers `InMemoryRegistrySettingsStore`. |
| `UseStorageForEachTenant<TTenantSource>(Func<IServiceProvider, TenantKey, IInboxStore> inbox, Func<IServiceProvider, TenantKey, IInboxDispatchStore> inboxDispatch, Func<IServiceProvider, TenantKey, IOutboxStore> outbox, Func<IServiceProvider, TenantKey, IOutboxDispatchStore> outboxDispatch)` | Registers the consumer's tenant source with its logging wrapper. Registers the tenant stores of each key from the functions, each with its logging wrapper and an internal wrapper that clears the next due time of its tenant in the dispatch composite after a successful `AddAsync` or `RetryAsync`. Registers `TenantInboxDispatchStore` and `TenantOutboxDispatchStore`, with their logging wrappers, as the dispatch stores (D125). The storage packages call it. |
| `UseStoredRegistry()` | Selects `StoredRegistry` as `IMessageRegistry` and `IRegistryWriter`, on the registered `IRegistrySettingsStore` (D118, section 4.20). |
| `UseRegistrySettingsStore<TStore>()` | Registers a registry settings store with its logging wrapper. The storage packages call it. |
| `UseInboxStore<TStore>()`, `UseOutboxStore<TStore>()` | Register a tenant store as a tenant service, with its logging wrapper. |
| `UseInboxDispatchStore<TStore>()`, `UseOutboxDispatchStore<TStore>()` | Register a dispatch store as a singleton, with its logging wrapper. The storage packages call these methods and the tenant store methods, with the factory overloads. A host calls them for its own store, or to use two different technologies for the inbox and the outbox. |
| `UseDispatchSignal<TSignal>()` | Selects another `IDispatchSignal`, for example one between processes (D102). A signal package calls it. |
| `ConfigureInboxDispatch(Action<DispatchOptions>)`, `ConfigureOutboxDispatch(Action<DispatchOptions>)` | Change the dispatch options. |
| `AddMessage<TEvent>(name, version, JsonTypeInfo<TEvent>, configure)` | Registers one message type. `configure` receives a `MessageBuilder<TEvent>`. |
| `MessageBuilder<TEvent>.WithRouter<TRouter>()` | Selects the router of this message type. It wins over the open generic router of `UseRouter` (D99). |
| `MessageBuilder<TEvent>.UsePayloadLoader<TPayload, TLoader>()` | Registers the payload loader of one payload type of this message type as a tenant service, with its logging wrapper. The channel packages call it for the loader `TLoader` of each channel registration. A second call with the same loader type adds nothing. A call with another loader type for the same payload type fails the startup validation (D135). |
| `MessageBuilder<TEvent>.WithUpgrade<TOld>(version, JsonTypeInfo<TOld>, Func<TOld, TEvent>)` | Registers an old event version (section 4.13). |
| `ConfigureTestDelivery(ChannelId, Action<TestDeliveryOptions>)` | Changes the test delivery of one channel (D83). The options also bind from `MsgHatch:TestDelivery:{Channel}`. |
| `ConfigureChannelDown(ChannelId, Action<ChannelDownOptions>)` | Changes the error threshold and the check interval of one channel (D86). The options also bind from `MsgHatch:ChannelDown:{Channel}`. |
| `MessageBuilder<TEvent>.AsChannel<TEnvelope, TComposer>(channel, name, version, JsonTypeInfo<TEnvelope>, retry, configure)` | The generic channel registration. The channel packages call it, after `AddChannelType`. `TComposer` is the `IEnvelopeComposer<TEvent, TEnvelope>` of the channel, a tenant service (D96). `retry` gives the default `RetryOptions` values of the channel, for example 3 tries and an expiry of 5 minutes for SignalR. `configure` receives a `ChannelBuilder<TEnvelope>` with `WithUpgrade<TOldEnvelope>` and `WithRetry(Action<RetryOptions>)` (D87). |

`AddMessage` registers `IMessageRouter<TEvent>` of its message type as a tenant service, with the logging wrapper `MessageRouterLoggingWrapper<TEvent>`. For each key, the registration creates the router of `WithRouter`. A message type with no `WithRouter` gets the open generic router type of `UseRouter`, closed for `TEvent`. So `WithRouter` wins over `UseRouter`, and the DI container has no open generic router registration. Phase 1 tests this for a key and for no key.

Setup methods of `MsgHatch.Subscriptions` and its packages (D97):

| Method | Package | One call registers |
|---|---|---|
| `UseSubscriptions()` | Subscriptions | `SubscriptionRouter<>` as the open generic router with `UseRouter`, `AddTraceType` for `ResolutionTrace`, and `ISubscriptionManager` and `IRoutingVerifier` as tenant services, each with its logging wrapper. |
| `UseSubscriptionStore<TStore>()` | Subscriptions | A subscription store as a tenant service, with its logging wrapper. The store packages call it with the factory overload. A host calls it only for its own store. |
| `UseInMemorySubscriptionStore()` | Subscriptions | `InMemorySubscriptionStore`, with its logging wrapper, for a host with no database. Each key has its own instance. |
| `UseRecipientDirectory<TDirectory>()` | Subscriptions | The consumer's recipient directory as a tenant service, with its logging wrapper (D76). |
| `UseSubscriptionCache<TBackend>(Action<SubscriptionCacheOptions>?)` | Subscriptions | The cache backend, `ISubscriptionCache` as a tenant service, and the caching wrapper between the logging wrapper and the store (D40). The cache packages call it. A host calls it only for its own backend. |
| `MessageBuilder<TEvent>.WithScope(Func<TEvent, NotificationScope>)` | Subscriptions | The scope function of the message type (D16). The subscription router needs it. |
| `MessageBuilder<TEvent>.WithTestEvent(testCase, Func<UserId, TEvent>)` | Subscriptions | One test case of the message type for the routing verification (section 4.18). |
| `UseFileSubscriptionStore(Action<FileStoreOptions>?)`, `UseFileSubscriptionStore(string rootPath)` | Subscriptions.Data.FileSystem | `FileStoreOptions` of the core package, and `FileSystemSubscriptionStore` under `{RootPath}/subscriptions` with its logging wrapper. A value in code follows the rule of the shared options below. |
| `UseSqliteSubscriptionStore(Action<SqliteStoreOptions>?)`, `UseSqliteSubscriptionStore(string connectionString)` | Subscriptions.Data.Sqlite | `SqliteStoreOptions` of the core package, and `SqliteSubscriptionStore` with its logging wrapper. The store creates its tables one time for each connection string. |
| `UseSqliteTenantSubscriptionStore(Action<SqliteTenantStoreOptions>?)` | Subscriptions.Data.Sqlite | `SqliteTenantStoreOptions` of the core package, and one `SqliteSubscriptionStore` for each key, on the file of that key: `TenantConnectionString` for a tenant, and `SystemConnectionString` for `TenantKey.None` (D125). |
| `UseEntityFrameworkSubscriptionStore(Action<DbContextOptionsBuilder> configure)` | Subscriptions.Data.EntityFrameworkCore | The EF Core subscription store on a pooled `IDbContextFactory` of its own `DbContext`, with `EntityFrameworkStoreOptions` of the core package and its logging wrapper. |
| `UseMongoDbSubscriptionStore(Action<MongoDbStoreOptions>?)` | Subscriptions.Data.MongoDb | The MongoDB subscription store, with its logging wrapper. It shares `MongoDbStoreOptions` and the `IMongoClient` with the core MongoDB stores. A value in code follows the rule of the shared options below. |
| `UseHybridSubscriptionCache(Action<SubscriptionCacheOptions>?, Action<HybridCacheOptions>?)` | Subscriptions.Caching.Hybrid | `HybridCacheSubscriptionBackend`, the caching wrapper, and HybridCache with `AddHybridCache`. A HybridCache that the host already registered stays, with the host's settings. |
| `UseFusionSubscriptionCache(Action<SubscriptionCacheOptions>?, Action<IFusionCacheBuilder>?)` | Subscriptions.Caching.FusionCache | `FusionCacheSubscriptionBackend`, the caching wrapper, and the named FusionCache `MsgHatch.Subscriptions`. The second callback configures that cache, for example a distributed level or a backplane. The host's own FusionCache instances do not change. |

Shared options (D59). A subscription store package uses the options class of its core package, for example `SqliteStoreOptions`. Its setup method with a value in code sets the same options as the core setup method. If both methods give a value in code, the startup validation fails, and its message names both methods (D62). So the outbox and the subscriptions of one technology always use one storage, and a value never changes the other one with no warning.

`MsgHatch.Subscriptions.AspNetCore` needs no setup method, because its endpoints need only `ISubscriptionManager` and `IRoutingVerifier`. `IEndpointRouteBuilder.MapMsgHatchSubscriptions(prefix, configure?)` maps the HTTP endpoints of section 4.17 on the web application.

`MsgHatch.AspNetCore` needs no setup method either, because its endpoints need only `IDeliveryManager`, `IMessageRegistry`, and `IRegistryWriter`. `MapMsgHatchDelivery(prefix)` and `MapMsgHatchDeliveryContent(prefix)` map the HTTP endpoints of section 4.19, and `MapMsgHatchRegistry(prefix)` the endpoints of section 4.20.

Setup methods of the other implementation packages (D60):

| Method | Package | One call registers |
|---|---|---|
| `UseFileStores(Action<FileStoreOptions>?)`, `UseFileStores(string rootPath)` | Data.FileSystem | `FileStoreOptions`, and `FileSystemInboxStore` under `{RootPath}/inbox` and `FileSystemOutboxStore` under `{RootPath}/outbox`, with their two parts and their logging wrappers. From phase 16, also the file registry settings store under `{RootPath}/registry`. |
| `UseSqliteStores(Action<SqliteStoreOptions>?)`, `UseSqliteStores(string connectionString)` | Data.Sqlite | `SqliteStoreOptions`, and `SqliteInboxStore` and `SqliteOutboxStore` on one database, with their two parts and their logging wrappers. From phase 16, also the SQLite registry settings store on the same database. The stores create their tables one time for each connection string. |
| `UseSqliteTenantStores<TTenantSource>(Action<SqliteTenantStoreOptions>?)` | Data.Sqlite | `SqliteTenantStoreOptions`, one `SqliteInboxStore` and `SqliteOutboxStore` for each key on the file of that key, as the tenant stores, and the dispatch composites, through `UseStorageForEachTenant<TTenantSource>`. The key `TenantKey.None` and, from phase 16, the registry settings store use `SystemConnectionString` (D125). |
| `UseEntityFrameworkStores(Action<DbContextOptionsBuilder> configure, Action<EntityFrameworkStoreOptions>?)` | Data.EntityFrameworkCore | `EntityFrameworkStoreOptions`, a pooled `IDbContextFactory<MsgHatchDbContext>` with the options of `configure`, for example `o => o.UseSqlServer(connectionString)`, and the EF Core inbox, outbox, and registry settings stores, with their logging wrappers (D120, D130). |
| `UseMongoDbStores(Action<MongoDbStoreOptions>?)` | Data.MongoDb | `MongoDbStoreOptions`, an `IMongoClient` if the host has not registered one, and the MongoDB inbox, outbox, and registry settings stores, with their logging wrappers (D121). |
| `UseEmbeddedTemplates(Assembly, string prefix)`, `UseFileTemplates(Action<FileTemplateOptions>?)`, `UseFileTemplates(string rootPath)`, `UseTemplateStore<TStore>()` | Email | The shared template store of the placeholder engine, with its logging wrapper, and the template selector. |
| `UseTenantTemplateStore<TStore>()` | Email | The template store of each tenant, as a keyed `ITemplateStore` with no unkeyed registration, with its logging wrapper. The shared template store is the unkeyed `ITemplateStore`, so the two never collide (D62). The template selector tries the store of the tenant first (D123). |
| `UseEmailClient<TClient>()` | Email | A custom `IEmailClient` and the email transport, with their logging wrappers. |
| `UseAmazonSes(Action<AmazonSesOptions>?)` | Email.AmazonSES | The options, `AmazonSimpleEmailServiceClient` as the `IEmailClient` with its logging wrapper, the email transport, and an `IAmazonSimpleEmailServiceV2` client for `AmazonSesOptions.Region`, if the host has not registered one. The AWS SDK finds the credentials. |
| `UseSignalR<THub>()` | SignalR | `SignalRTransport<THub>` as the one `ITransport` of the SignalR channel, with its logging wrapper, and the SignalR server services with `AddSignalR` (D129). |
| `UseBrokerClient<TClient>()` | Broker | A custom `IBrokerClient` and the broker transport, with their logging wrappers. One transport serves each payload type (D129). |
| `UseWolverineBroker()` | Broker.Wolverine | `WolverineBrokerClient` as the `IBrokerClient` with its logging wrapper, and the broker transport. It uses the Wolverine of the host, because the host selects the transport of Wolverine, for example RabbitMQ or Amazon SQS (D134). |

A transport registration makes the host a publisher of its channel. The outbox dispatcher of a host sends exactly the channels whose transport the host registered (D101).

Message channel registrations of the channel packages. Each one calls `AddChannelType` for its channel, registers the loader `TLoader` with `UsePayloadLoader`, and then calls `AsChannel` with the library composer of its channel. `AsChannel` registers the composer as a tenant service:

| Method | Package | Effect |
|---|---|---|
| `MessageBuilder<TEvent>.AsEmail<TPayload, TLoader>(name, configure?)` | Email | Registers email with `EmailComposer<TEvent, TPayload>` and the placeholder engine. Requires `TPayload : IPlaceholderPayload<TPayload>`. The channel envelope is the library type `EmailMessage`, so the registration needs no version and no JSON type information (D17). |
| `MessageBuilder<TEvent>.AsEmail<TPayload, TLoader, TEngine>(name, configure?)` | Email | Registers email with another engine. |
| `MessageBuilder<TEvent>.AsSignalR<TPayload, TLoader>(name, version, JsonTypeInfo<SignalREnvelope<TPayload>>, configure?)` | SignalR | Registers a SignalR message. The channel envelope is `SignalREnvelope<TPayload>`. |
| `MessageBuilder<TEvent>.AsBroker<TPayload, TLoader>(name, version, JsonTypeInfo<BrokerEnvelope<TPayload>>, configure?)` | Broker | Registers a broker message. The channel envelope is `BrokerEnvelope<TPayload>`. |

`MsgHatchBuilder` records each selection of an implementation. If two setup methods select an implementation for the same interface, for example `UseFileStores` and `UseSqliteStores`, the startup validation fails, and its message names both methods (D62).

Lifetimes (D130):
- Singleton: the options, the content type registry, `IMessageRegistry`, the library stores, the transports, the template stores with their caches, the cache backends, the dispatch signal, the dispatchers, and `TimeProvider`. Each one is thread-safe.
- Scoped: the routers, the payload loaders, the composers, the template engines, the recipient directory, `INotificationService`, `IDeliveryManager`, `ISubscriptionManager`, `IRoutingVerifier`, and the pipeline.
- Only a received message gets a DI scope, because the routers and the payload loaders of the consumer run in it. The inbox processor makes one DI scope for each claimed message. A caller of `ProcessAsync`, `AcceptAsync`, or `PreviewAsync` resolves `INotificationService` in its own scope, for example the scope of an HTTP request or of a broker message. The outbox dispatcher makes no DI scope. It uses the singleton dispatch store and the singleton transport of its channel.
- A store of the consumer must be thread-safe too, for example an EF Core store on an `IDbContextFactory` that makes one `DbContext` for each operation.

Roles (D105). `MsgHatchOptions.Roles` tells the startup validation what the host must have. The DI registrations do not change with the role, so all processes can call the same registration code:
- `Consumer`: the host routes and composes. It runs the inbox processor, or calls `ProcessAsync`.
- `Publisher`: the host sends. It runs the outbox dispatcher for the channels of its transports.
- The default is both, for a host with one process.

Startup validation, with `ValidateOnStart`:
- An outbox tenant store and an outbox dispatch store are registered.
- No interface has two selected implementations (D62). Each channel has at most one transport (D129).
- With `UseStoredRegistry`, a registry settings store is registered.
- At most one setup method gives a value in code to one shared options class (section 4.7, shared options).
- With the role `Consumer`, each registered message type resolves an `IMessageRouter<TEvent>` for `TenantKey.None` in a DI scope (D95). The subscription router resolves only for a message type with a scope function from `WithScope`.
- With the role `Consumer` and `UseSubscriptions`, a subscription store and a recipient directory are registered.
- With the role `Consumer`, each email registration with the placeholder engine has a template store.
- With the role `Publisher`, the host registered at least one transport.
- With `UseWolverineBroker`, the host registered Wolverine, so that `IMessageBus` resolves.
- Each content type and version is unique.
- Each message type name is unique. Two registrations with one name and two versions have two different content types, so the check above does not find them.
- Each message type has at most one registration for each channel, and its channel registrations have different envelope types (D135).
- Each payload type of a message type has one loader type (D135).
- Each registered options class passes its attributes and its `IValidatableObject` rules (D64). For example, a missing `AmazonSesOptions.SenderEmail` or a `LeaseSafetyMargin` that is not less than `Lease` stops the start.

A host runs this validation when it starts. Without a host, the consumer calls `IStartupValidator.Validate()`.

### 4.8 Processing

Processing of one domain event:

1. `AcceptAsync` builds a `ReceivedMessage<TEvent>` with the message id, the tenant, and the flags of the `MessageContext` (D115), calls `AddAsync` of the inbox tenant store of `TenantKey.For(context.Tenant)`, and notifies the inbox topic of the dispatch signal. Later, the inbox processor claims it and runs steps 2 to 6. `ProcessAsync` builds the `ReceivedMessage<TEvent>` in memory and runs steps 2 to 6 directly. An unregistered event type gives `Errors.NotFound`.
2. **Route.** The pipeline reads the descriptor of the message type from `IMessageRegistry` one time. If the message type is disabled, the pipeline stores nothing, logs it at the Information level, and returns a result with no stored message (D118). Otherwise it resolves `IMessageRouter<TEvent>` with the tenant key of the message, and calls `RouteAsync` one time (D95, D126). Each later step resolves its tenant services with the same key. Section 4.17 describes the routing procedure of the subscription router.
3. **Check.** The pipeline checks each route, and skips or fails only that route:
   - A route for a channel that `IMessageRegistry` gives as disabled, or that the message type has in `DisabledChannels`, is skipped. It gives no outgoing message and no error. `NotificationResult.Skipped` counts it, and the pipeline logs it at the Debug level (D118).
   - A route for a channel that the message type has not registered gives `Errors.NotFound`.
   - A route with settings of another type than the settings type of the channel gives `Errors.ValidationFailed`.
   - A second route for the same target and channel gives `Errors.Conflict`, because its outgoing message id is the id of the first route (step 5).
4. **Compose.** `RouteGrouping.ByChannel` splits the routes by channel. For each channel registration of `TEvent`, the pipeline calls `IEnvelopeComposer<TEvent, TEnvelope>.ComposeAsync` one time, with all routes of that channel (D96). The library composers do these steps:
   - Each library composer first calls `IPayloadLoader<TEvent, TPayload>.LoadAsync` one time, with all routes of its channel. A loader error is the outer error of the composer.
   - The email composer groups the routes by language and template scope with `RouteGrouping.ByLanguageAndScope`. It calls `IEmailTemplateEngine.PrepareAsync` one time for each group, and `IEmailTemplate.RenderAsync` for each route, with the culture of the language. It takes `To` from the address, and fails a route with no address with `Errors.ValidationFailed`. The format `PlainText` sends only the text part.
   - The SignalR composer puts the payload into a `SignalREnvelope<TPayload>`. The transport takes the target from the outgoing message, and the method name is the message type name.
   - The broker composer puts the payload as it is, and the headers of the channel settings, into a `BrokerEnvelope<TPayload>`.
5. **Outgoing messages.** The pipeline builds an `OutgoingMessage<TEnvelope>` for each envelope:
   - `DeterministicId` derives the id from the message id, the tenant, the delivery target, and the channel id.
   - `Tenant` is the tenant of the context (D115). `Type` is the message type of the event, and `Target` is the target of the route (D113).
   - `MaxTryCount` and `ExpiresAtUtc` come from the retry options of the channel registration, which have the content type as their name (D87). `ExpiresAtUtc` is `CreatedAtUtc` plus `Expiry`, or null when `Expiry` is null (D94).
   - `ContentType` and `ContentVersion` come from the registration. `SchemaVersion` is 1.
   - `Trace` is the trace of the route (D98).
6. **Store.** The pipeline calls `AddAsync` of the outbox tenant store of the tenant key one time for the event. Then it notifies the topic of each channel of the stored messages through the dispatch signal (section 4.9). Each outgoing message has the flags of the received message.

A failure in step 3, an inner error in step 4, and a failure in step 5 fail only the affected messages. A failure in step 2 or 6, and an outer error in step 4, fail the event, and the inbox or the broker retries it.

`PreviewAsync` builds the `ReceivedMessage<TEvent>` in memory from the context, and adds `MessageFlags.Test` to its flags. With routes from the caller, it runs steps 3 to 5 with these routes. With null routes, it runs steps 2 to 5 with the routes of the router, and a disabled message type gives an empty list. It stores nothing, notifies no signal, and returns one `RouteResult` for each route (D84, D100).

Publishing of one claimed outgoing message:

1. If the channel of the message is down, the dispatcher does not send, and records no try. The lease ends by itself, and no loop claims the messages of the channel until the channel is up again (section 4.9).
2. If the message is expired, the dispatcher records a try with the outcome `Expired`, and does not send.
3. If the message has `MessageFlags.Test`, the dispatcher reads the `TestDeliveryOptions` of the channel (D83):
   - `Suppress`, the default: it records a try with the outcome `Suppressed`, and does not send.
   - `Redirect`: it sends with `RedirectTo` as the `redirectTo` argument. Each transport applies it to its own envelope: the email transport replaces `To` and removes `Cc` and `Bcc`, the SignalR transport sends to the SignalR group `RedirectTo`, and the broker transport adds the header `msghatch-redirect`.
   - `Deliver`: it sends as for a normal message. A broker message gets the header `msghatch-test: true`, so that a receiver can see the test flag.
4. The dispatcher reads the start time from `TimeProvider` and calls `SendAsync` of the `ITransport` of the channel (D129).
5. It reads the finish time from `TimeProvider` and builds a `PublishTry` with the next try number:
   - A receipt gives `Succeeded` with the provider message id.
   - A transient error gives `TransientError`. If the try number is less than `MaxTryCount`, the dispatcher also calculates the next try time with the retry options of the channel registration of the message (section 4.14, D87).
   - A permanent error gives `PermanentError`.
6. It calls `IOutboxDispatchStore.RecordTryAsync`. If this call fails, it repeats the call one time. If the call fails again, the lease ends, and a later claim sends the message again. After a recorded try with a next try time, the worker gives that time to the claim loop of its channel (D103).
7. It updates the error count of the channel with the outcome of the try (section 4.9).

A cancelled send records no try. The lease of that message ends by itself.

Processing of one claimed received message: the inbox processor makes one DI scope for the message, upgrades an old version (section 4.13), runs steps 2 to 6 with the tenant key of the message, and then calls `CompleteAsync` of the inbox dispatch store. A failure calls `FailAsync` with the next try time, or with no next try time for a permanent error or the last try. After a `FailAsync` with a next try time, the worker gives that time to the claim loop (D103).

`DeterministicId.Create(Guid messageId, TenantId? tenant, DeliveryTarget target, ChannelId channel)` is a UUID version 5:
1. The namespace is the fixed id `77b46874-bae1-4fa7-a7d9-baec996f4fc6`. This value never changes.
2. The name is the 16 bytes of `messageId` in big-endian order. With a tenant, the byte `t`, the UTF-8 bytes of the tenant id, and one `0x00` byte follow. With no tenant, nothing follows, so a setup with no tenant keeps the same ids. Then come one byte for the target kind (`u` for a user, `c` for a company), then the UTF-8 bytes of the target id, then one `0x00` byte, then the UTF-8 bytes of the channel name. A `stackalloc` buffer holds it, or a pooled array for a long input.
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
   - The default `InProcessDispatchSignal` holds one `Channel<bool>` with capacity 1 and `BoundedChannelFullMode.DropWrite` for each topic. It reaches only the loops of the same process.
   - `UseDispatchSignal<TSignal>()` selects another implementation, for example one with a named pipe or a message broker between processes. Such a package is out of scope now (section 7). The contract is ready for it.
4. **The down state of a channel (D86, D106).** The outbox dispatch store keeps one down state for each channel, for all publisher instances of the channel. Each outbox loop keeps only the error count of its channel in its process:
   - A `TransientError` try adds 1 to the count. A `Succeeded` or `PermanentError` try sets the count to 0, because the provider answered. An `Expired` or `Suppressed` try sends nothing, so it does not change the count. A cancelled send records no try and does not change it.
   - When the count reaches `ErrorThreshold` of the `ChannelDownOptions` of the channel, the loop calls `SetChannelDownAsync` with the reason `Errors`, the error of the last try, and a next check time of `CheckInterval` later. The loop whose call wrote the state logs it at the Error level, with the channel and the error code. The other loops change nothing, because the first loop wins.
   - A claim of a down channel claims nothing, and returns the down state. A worker does not send a message that it claimed before the channel went down, and records no try for it. Its lease ends by itself.
   - While the reason is `Errors`, the loop waits until `NextCheckAtUtc`, and then calls `TryStartChannelCheckAsync` with a next check time of `CheckInterval` later. Only the loop that gets true calls `CheckHealthAsync` of its transport, so one publisher instance at a time checks the channel.
   - A check with no error calls `SetChannelUpAsync` with the reason `Errors`. The loop logs the end at the Information level, with the down time. A check with an error changes nothing more. The next check comes at the moved `NextCheckAtUtc`.
   - While the reason is `Operator`, no loop checks the channel. Only a person ends this state, through `IDeliveryManager.ResumeChannelAsync` (section 4.19). A resume also notifies the topic of the channel, so that a waiting loop claims again at once.
   - When a loop finds its channel up again after a down state, its count starts at `ErrorThreshold` minus 1, so the first transient error marks the channel down again.
   - A message of a down channel keeps its try count, because no loop records a try for it. The expiry still applies: after the channel is up, an expired message gets an `Expired` try, and is not sent.
   - A transport with nothing to check, for example SignalR or the Wolverine broker client, returns null from `CheckHealthAsync`. Its down state then ends after one `CheckInterval`, so a long outage of such a channel uses at most one try for each `CheckInterval`.
   - The down state is in the store, so each process and each run sees it, also a host that makes a new dispatcher for each run, for example a Lambda function. The dispatcher has the comment `ponytail: fixed check interval, a growing interval if long outages need it`.

Hosting (D101, D102). The same registration code serves each of these layouts:
- **One process.** One host runs the event consumer and all publishers, for example with one `BackgroundService` that starts `IInboxProcessor.RunAsync` and `IOutboxDispatcher.RunAsync`. `MsgHatchOptions.Roles` keeps its default, both roles. The in-process signal wakes each loop. With `EnablePolling` false, no loop polls a store: each loop waits only for a signal and for the next due time, which includes the next try times that its workers give to it.
- **Separate processes.** The event consumer app has the role `Consumer`, registers no transport, and runs the inbox processor, or calls `ProcessAsync` from a broker. Each publisher app has the role `Publisher`, registers the transport of its channel, for example with `UseAmazonSes`, and runs `IOutboxDispatcher.RunAsync`. All apps call the same registration code of the message types, and use the same storage. A publisher then finds new messages by polling and by the next due time, or by an `IDispatchSignal` between processes.
- A stopped publisher stops only its channel. The messages of the channel wait in the outbox. They are sent after the restart, or they get an `Expired` try.
- Each process must reach the same stores. SQLite works only for processes on one machine, and the file stores need a folder with atomic rename (section 6).

`DispatchPendingAsync` claims one batch of at most `batchSize` messages for each published channel, and `ProcessPendingAsync` claims one batch for the inbox. Each one processes its batches with `Parallel.ForEachAsync` and `MaxDegreeOfParallelism`, and returns one `DispatchSummary`. They use the same function for each message as `RunAsync`.

Shutdown: when the token is cancelled, each claim loop stops and completes the writer of its work queue. Each worker finishes or cancels its current send, and then stops. A cancelled send records no try. The messages that are still in a work queue are not sent. They become due again when their lease ends.

The validation of `DispatchOptions` at startup (section 4.4) requires `LeaseSafetyMargin` to be less than `Lease`, and `QueueCapacity` to be at least `MaxDegreeOfParallelism`.

### 4.10 Email templates and engines

**Confirmed rule (D44):** an email registration renders its emails through `IEmailTemplateEngine<TPayload>`. The engine has two steps, so that the email composer can find and prepare a template one time for each group of routes:
1. `PrepareAsync` finds and prepares the template for one message type, language, and scope. It returns `Errors.NotFound` when no template exists.
2. `IEmailTemplate.RenderAsync` renders the prepared template for one payload and culture. It also receives the route, so that an engine can read its trace, for example the `ResolutionTrace` of the subscription router, to log the source of the language or to show it in a test email. A placeholder template completes synchronously, so this call does not allocate a task.

The first engine is `PlaceholderEmailEngine<TPayload> where TPayload : IPlaceholderPayload<TPayload>` (D45):
- **Template store.** `ITemplateStore.FindAsync(TemplateLookup lookup, CancellationToken ct)` returns `ValueTask<ErrorOr<TemplateSource?>>`. A null value means that no template matches. This is not an error.
- **Fallback (D123).** The internal template selector tries these lookups in this order, and the first hit wins:
  1. The template store of the tenant, with the language and the scope.
  2. The template store of the tenant, with the language and no scope.
  3. The shared template store, with the language and the scope.
  4. The shared template store, with the language and no scope.
  5. to 8. The same four lookups with the default language.
  The selector gets the template store of the tenant with its own key. It skips the lookups in that store for the key `TenantKey.None`, or when no tenant template store is registered. It skips a lookup with a scope when the request has no scope. So a setup with no tenant tries the shared store with the scope and then with no scope, and the company templates of D80 apply.
- **Parser.** A placeholder is `{{PropertyName}}`. The name contains only letters, digits, and `_`. A `}}` outside a placeholder is literal text. An unclosed `{{`, or a name with other characters, gives `Errors.TemplateInvalid`. The parser makes an array of literal and placeholder segments.
- **Binding.** `ParsedTemplateCache<TPayload>` parses a template one time for each `TemplateKey`, and binds each placeholder to a property index with `IPlaceholderWriter.IndexOf`. An unknown name gives `Errors.TemplateInvalid` (D5). The cache is a static `ConcurrentDictionary<TemplateKey, ParsedTemplate>` for each payload type. It is marked with `ponytail: unbounded cache, add a size limit if a store produces many versions`.
- **Renderer.** `PlaceholderRenderer` writes each segment into an internal `PooledCharBufferWriter` on `ArrayPool<char>.Shared`. In an `html` part, it encodes each value with `HtmlEncoder.Default` through the `OperationStatus` API. In a `subject` or `text` part, it does not encode.

Template types (`MsgHatch.Email.Templates`):

```csharp
public readonly record struct TemplateKey(string Id, long Version);
public readonly record struct TemplateLookup(MessageType Type, string Language, string? Scope);
public enum TemplatePartKind { PlainText, Html }
public sealed record TemplatePart(string Name, TemplatePartKind Kind, string Text);
public sealed record TemplateSource(TemplateKey Key, IReadOnlyList<TemplatePart> Parts);
```

The part names are `subject` (`PlainText`, required), `html` (`Html`, required), and `text` (`PlainText`, optional).

Template stores:
- `FileSystemTemplateStore(string rootPath)` reads the files `{MessageType}.{Language}[.{Scope}].subject.txt`, `.body.html`, and `.body.txt`, for example `OrderShipped.en.subject.txt`. The template id is the full path of the template with no part suffix, so two folders never share an id. The version is the latest last write time of the parts, in ticks.
- A host can use `FileSystemTemplateStore` as the template store of each tenant, with the factory overload of `UseTenantTemplateStore`, for example on the folder `tenants/{tenantId}`, with the tenant id escaped by `Uri.EscapeDataString`.
- `EmbeddedResourceTemplateStore(Assembly assembly, string prefix)` reads the resources `{prefix}.{file name}`, with the same file names. The template id is `{assembly name}:{resource name}` with no part suffix. The version is 0, because an embedded resource does not change while the process runs. It is a shared template store only, because the resources are the same for all tenants.
- Each library template store caches its sources and its misses (R10). The embedded store keeps them for the life of the process. On each lookup, the file store reads only the last write times of the part files, and reads the files again only when a time changed. A missing file stays a cached miss until it exists.

Placeholder payloads (`MsgHatch.Email.Placeholders`):

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

**Razor as a later engine (D45).** Razor components can render HTML outside of a web request through `HtmlRenderer` from `Microsoft.AspNetCore.Components.Web`. They give compile-time type checks, loops, conditions, layouts, and HTML encoding. But they do not fit as the first engine:
- The Razor SDK compiles components into the consumer's assembly. A component cannot load from a file or a database at run time, so a Razor template is not editable after deployment. Requirement 2 needs embedded resources, external files, and other storage.
- Razor encodes each value for HTML. A subject or a text part then gets HTML entities, for example `&amp;` for `&`. So a Razor engine needs another source for the subject and the text body.
- `HtmlRenderer` needs a dispatcher, a parameter dictionary, and a render tree for each email. This allocates more than the placeholder renderer.

A future `MsgHatch.Email.Razor` package implements `IEmailTemplateEngine<TPayload>` with one component type for each message type and language. It needs no change in the other packages.

### 4.11 Allocation budget

The target is zero allocation in the MsgHatch hot path. Some allocations stay, because they are outside MsgHatch or because they are the result itself:

| Scope | Allocates | Does not allocate |
|---|---|---|
| Each event | The received message, the route list, the payload lists, the composer results, the outgoing message list | Registry lookups, ids, `ErrorOr` results |
| Each message | The payload (from the consumer's loader), the rendered email strings, the channel envelope, the outgoing message | Template parsing on a cache hit, writing a value-type property, HTML encoding, `DeterministicId` |
| Each send | The publish try, the objects that the store deserializes, and the request objects of the AWS SDK or the broker library | The dispatcher loop |
| Each dispatch loop | One enumerator for each worker, and one timeout for each wait of the claim loop | Each wait for the next message. The reader of the work queue uses pooled value-task sources. |
| Each wrapper call | A log entry, only when its level is enabled | The call itself. The wrappers use `PoolingAsyncValueTaskMethodBuilder`, and the source-generated log methods do not box. |
| Each cached subscription lookup | The string cache key, and the work of the cache backend | The store call that a cache hit replaces |
| Each span and measurement | The activity and its tags, only when a listener samples the span | A span with no listener, and a metric measurement with up to 8 tags in a `TagList` (section 4.21) |

The library does not serialize in the pipeline. Each store serializes one time, with its own serializer.

Unit tests prove the "does not allocate" claims for code with no dependencies with `GC.GetAllocatedBytesForCurrentThread()`. This covers `ErrorOr`, the generated writers, and `DeterministicId`. NSubstitute allocates when it records a call, so code with a substitute in its path cannot get an allocation test. Phase 13 measures the template cache, rendering, the dispatcher loop, and the full budget with BenchmarkDotNet.

### 4.12 Error handling

**Confirmed rule:** MsgHatch defines no custom exception types. Each expected failure is a value, because `ErrorOr<T>` and `Error?` cost less than exceptions and make each failure case explicit:
- An operation with a result returns `ErrorOr<T>`.
- An operation with no result returns `Error?`.
- `ErrorType.IsTransient` tells the dispatcher to retry or to stop.

Exceptions stay at the edges:
- Each adapter that calls external code catches the exceptions of that code and returns an `Error`. External code is the AWS SDK, the broker library, SignalR, `System.Text.Json`, the file system, and embedded resources.
- The pipeline and the dispatchers catch an unexpected exception from consumer code one time, at their outer boundary. They convert it into `Errors.Exception`, so a defective store cannot stop the dispatcher loop.
- Cancellation becomes `Errors.OperationCancelled`.
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
| `IDeliveryManager` | Library | `DeliveryManagerLoggingWrapper`. It logs each change at the Information level (section 4.19). |
| `IInboxProcessor` and `IOutboxDispatcher` | Library | `InboxProcessorLoggingWrapper` and `OutboxDispatcherLoggingWrapper` |
| `ITransport` | Library or consumer | `TransportLoggingWrapper` |
| `IInboxStore`, `IOutboxStore`, `IInboxDispatchStore`, and `IOutboxDispatchStore` | Library or consumer | `InboxStoreLoggingWrapper`, `OutboxStoreLoggingWrapper`, `InboxDispatchStoreLoggingWrapper`, and `OutboxDispatchStoreLoggingWrapper` |
| `ISubscriptionStore` | `MsgHatch.Subscriptions` or consumer | `SubscriptionStoreLoggingWrapper` |
| `IRecipientDirectory` | Consumer | `RecipientDirectoryLoggingWrapper` |
| `ISubscriptionManager` | `MsgHatch.Subscriptions` | `SubscriptionManagerLoggingWrapper` |
| `IRoutingVerifier` | `MsgHatch.Subscriptions` | `RoutingVerifierLoggingWrapper` |
| `IPayloadLoader<TEvent, TPayload>` | Consumer | `PayloadLoaderLoggingWrapper<TEvent, TPayload>` |
| `IEmailClient` | Library (SES) or consumer | `EmailClientLoggingWrapper` |
| `IEmailTemplateEngine<TPayload>` | Library or consumer | `EmailTemplateEngineLoggingWrapper<TPayload>` |
| `ITemplateStore` | Library or consumer | `TemplateStoreLoggingWrapper` |
| `IBrokerClient` | Library (Wolverine) or consumer | `BrokerClientLoggingWrapper` |

Pure in-memory components get no wrapper: `IMessageRegistry`, the template selector, the parser, the renderer, the composers, `RouteGrouping`, `SubscriptionResolver`, `DeterministicId`, and the in-process dispatch signal. They do no I/O, and a wrapper call for each message costs time in the hot path. A composer does its I/O through the loader and the template engine, which have wrappers. The cache backend gets no logging wrapper, because the caching wrapper logs each hit, each miss, and each backend error.

What a logging wrapper logs:

| Result | Level | Fields |
|---|---|---|
| Success | Debug | The operation, the elapsed time, and counts, for example the number of routes |
| `Errors.OperationCancelled` | Information | The operation and the elapsed time |
| Transient error | Warning | The operation, the elapsed time, and the error code, name, and message |
| Permanent error | Error | The same fields as for a transient error |

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
- The wrapper is a tenant service around the subscription store of its key (D126). It caches only `FindForDeliveryAsync`. It caches the subscriptions of one message type and one owner in one entry, with the key `{KeyPrefix}:{Tenant}:{MessageType}:d` for the defaults, `{KeyPrefix}:{Tenant}:{MessageType}:c:{CompanyId}` for a company, and `{KeyPrefix}:{Tenant}:{MessageType}:u:{UserId}` for a user. `{Tenant}` comes from its key: `n` for `TenantKey.None` and `t:{TenantId}` for a tenant, so the two forms never collide (D114).
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
- `FusionCacheSubscriptionBackend` in `MsgHatch.Subscriptions.Caching.FusionCache`. It calls `TryGetAsync`, `SetAsync` with the tags, and `RemoveByTagAsync` of `IFusionCache`. With a FusionCache backplane, an invalidation also reaches the other instances.
- Each backend catches the exceptions of its cache library and returns an `Error` (section 4.12).
- A distributed second level needs a serializer for `Subscription[]`. The consumer configures it in the cache library.

### 4.16 Log events

Each namespace has one `internal static partial class Log` with `[LoggerMessage]` methods. Structured property names are PascalCase, for example `{MessageType}`. Each area has its own event id range:

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
| MongoDB stores | 2800 to 2899 |
| Registry administration and its HTTP endpoints | 2900 to 2999 |

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
   - The language is the language of the channel settings. If it is null, the language is the UI language of the user, or the default language of the company for a company target. If that is also missing, it is `MsgHatchOptions.DefaultLanguage`.
   - The address comes from `UserProfile.Addresses` for a user target, and from `CompanyProfile.Addresses` for a company target on a channel without `GroupDelivery`. When the profile has no address for the channel, the route has no address, and the address source is `None`. The email composer then fails that message (section 4.8).
   - The template scope is the text of the company of the target user, or of the target company (D80).
   - The resolver records a `ResolutionTrace` for each effective subscription, and an `OptOutDecision` for each opt-out that applies (section 4.4, D92).
5. **Routes.** It returns the effective subscriptions as the routes, each with its `ResolutionTrace` as the route trace (D98). It logs each opt-out decision at the Debug level, because an opt-out gives no route.

A store error or a directory error fails the routing, and so the event (section 4.8).

Resolution of the subscription example (section 1.1) for one event of the type B with the users Client 1 and Client 2:
- Client 1 has a user subscription for B and the channel Email, so the company opt-out of Acme does not apply to Client 1. Client 1 gets an email in English.
- Client 2 has no user subscription for B. Acme has an opt-out for B, so Client 2 gets nothing.
- Acme has a fixed subscription for B for the company. The event concerns Acme, so `admin@acme.example` gets one email in Hungarian. The opt-out of Acme does not stop it, because the opt-out is for the scope users only.

**Confirmed rule (D53):** `ISubscriptionManager` is the API that lists and changes the subscriptions of all three levels. `MsgHatch.Subscriptions` implements it on its contract `ISubscriptionStore` (D78).

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

Validation. `CreateAsync` and `UpdateAsync` validate the draft first, and check the conflicts only for a valid draft. So an invalid draft never gives `Errors.Conflict`. They return `Errors.ValidationFailed`, and make no store change, in these cases:
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
| POST | `{prefix}` | The draft: `level`, `companyId`, `userId`, `messageType`, `kind`, `channel`, `settings`, `recipients` (`kind` and `users`), `mandatory` | 201 with the subscription and a `Location` header |
| PUT | `{prefix}/{id}` | The draft and `revision` | 200 with the subscription |
| DELETE | `{prefix}/{id}` | none | 204 |

- Each route takes the optional query parameter `tenantId`. The endpoint resolves `ISubscriptionManager` and `IRoutingVerifier` with `TenantKey.For(tenantId)`, and with `TenantKey.None` when the parameter is missing (D126).
- The `settings` object is read with the `JsonTypeInfo` of the settings type of the given channel, from the content type registry. For example, `{ "channel": "Email", "settings": { "language": "hu", "format": "PlainText" } }`.
- The consumer adds the authorization on the returned group, for example `.RequireAuthorization("SubscriptionAdmin")`. The library adds no authorization.
- With `o => o.CurrentUser = user => ...` and, for several tenants, `o.CurrentTenant = user => ...`, the same endpoints also exist under `{prefix}/me`. They take the user id from the signed-in user, and the tenant from `CurrentTenant` in place of the query parameter. They use only user-level subscriptions of that user. A subscription of another owner gives 404 there.
- `Errors.ValidationFailed` gives 400, `Errors.NotFound` gives 404, `Errors.Conflict` gives 409, and each other error gives 500. The body is a `ProblemDetails` with the error name and message.
- The JSON of the endpoints uses camelCase names and enum values as strings, through a source-generated `JsonSerializerContext`.
- `POST {prefix}/verification` with a `RoutingVerificationRequest` runs the routing verification of section 4.18, and gives 200 with the report.

### 4.18 Test messages and routing verification

**Confirmed rule (D82):** an incoming message can be a test message. The caller gives `MessageFlags.Test` to `AcceptAsync` or `ProcessAsync`, for example when a test client or a header of the broker message marks the event as a test. The flag goes to each outgoing message, to the stores, and to each log record:
- `IDeliveryManager.DeleteTestDataAsync` calls `DeleteTestDataAsync` of the inbox dispatch store and of the outbox dispatch store, and returns the two counts. It deletes the test data of all tenants. A test run calls it at its end. Before phase 15, the tests call the dispatch stores directly.
- Analytics can leave test data out with the `Flags` field of the stores and the `MessageFlags` property of the log records.
- A channel handles a test message as its `TestDeliveryOptions` say (section 4.8, D83). The default `Suppress` records a try and sends nothing, so that a test never reaches a real recipient by mistake.

**Confirmed rule (D85):** `IRoutingVerifier` of `MsgHatch.Subscriptions` runs a full routing test of the subscription router. It resolves and generates the messages of all message types with a scope function, for all stored subscriptions, all test users, and all languages, and reports each result. It generates the messages with `INotificationService.PreviewAsync`, so it stores nothing and sends nothing (D100).

Test events. `MessageBuilder<TEvent>.WithTestEvent(string testCase, Func<UserId, TEvent> create)` of `MsgHatch.Subscriptions` registers one test case of a message type. The function makes a test event for one test user, so that the payload loader loads the real data of that user. Several test cases of one message type give the variations of the test messages, for example a long name, accented characters, or optional fields that are null.

Procedure of `VerifyAsync`. The verifier is a tenant service. Its store and its directory are those of its key, so a verification checks one tenant (D116, D126):
1. It calls `ISubscriptionStore.QueryAsync` one time, for all subscriptions, or for the subscriptions of `request.Type`.
2. It calls `IRecipientDirectory.GetUsersAsync` one time for `request.TestUsers`.
3. For each message type with no test event, it adds one `Skipped` row.
4. For each test case, each test user, and each language of `request.Languages`:
   1. It copies the profile of the test user, with that language as the UI language.
   2. It makes the test event with the function of the test case.
   3. It runs the routing procedure of section 4.17 with two changes: the profile copy is the directory result for the test user, and the subscriptions of step 1 replace the store call. The directory gives the other profiles that the fixed subscriptions need, for example a company address or a named user.
   4. It calls `INotificationService.PreviewAsync` one time with the test event and the routes (section 4.8).
   5. It adds one row for each route: `Passed` when the `RouteResult` has a message, or `Failed` with the error code and name, for example `NotFound` for a missing template in that language. Each row has the owner of the subscription that applied, its target, and its resolution trace. The target of a fixed subscription can be the company or a named user, not the test user.
   6. It adds one `Passed` row with no channel when the resolution gives no delivery, for example for an opt-out. The owner of the row is the owner of the opt-out of the `OptOutDecision`, and the row has no trace. A row with no delivery and no opt-out has no owner.
5. It adds one `Skipped` row for each stored subscription that no row used, for example a subscription of a company with no test user. A row uses a subscription when that subscription applied, or when the trace of the row lists it as skipped. This shows the gaps of the test users.

The verifier stores nothing and sends nothing. A test job can fail when `RoutingVerificationReport.Failed` is greater than 0. `docs/subscription-domain.md` has the examples X1 to X7.

### 4.19 Delivery management

**Confirmed rule (D107):** `IDeliveryManager` is the API for a UI of the consumer. The UI shows the traffic and the history of the inbox and the outbox, and repairs stuck messages and down channels. The library implements it on the tenant stores and the dispatch stores (D108, D127). It is a tenant service: its message members use the tenant stores of its key. Its channel members, `DeleteTestDataAsync`, and `PurgeAsync` act on all tenants through the dispatch stores, because a channel is global (D106). So only the manager of `TenantKey.None` changes this global state: `PauseChannelAsync`, `ResumeChannelAsync`, `DeleteTestDataAsync`, and `PurgeAsync` of a manager of a tenant give `Errors.ValidationFailed`. `GetChannelsAsync` works for each key.

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
    OutgoingMessageSummary Summary, RouteTrace? Trace, IReadOnlyList<PublishTry> Tries);

public sealed record TrafficQuery(
    DateTime? CreatedFromUtc = null, DateTime? CreatedToUtc = null, DeliveryStatus? Status = null,
    bool IncludeTest = false);

public readonly record struct MessageCount(
    MessageType Type, ChannelId? Channel, DeliveryStatus Status, int Count, DateTime? OldestDueAtUtc);

public sealed record TrafficReport(IReadOnlyList<MessageCount> Inbox, IReadOnlyList<MessageCount> Outbox);

public sealed record ChannelStatus(
    ChannelDescriptor Descriptor, ChannelDown? Down, int Pending, DateTime? OldestDueAtUtc);

public readonly record struct DeliveryActionResult(int Matched, int Changed);
```

Rules of the types:
- A query matches the messages of the tenant of the store that match all given filters. A null filter matches each value. With `IncludeTest` false, a query leaves out the messages with `MessageFlags.Test`.
- `DueBeforeUtc` matches the pending messages whose `NextTryAtUtc` is before it. With the current time minus some minutes, it finds the stuck messages: due, but not sent.
- A page has the messages in the order newest first: `CreatedAtUtc` descending, then `Id` descending. `After` is the cursor of the last message of the previous page, and the next page starts after that message. `Next` is the cursor of the last message of the page, or null when the page has fewer messages than `Limit`. This paging stays correct while the messages change (D112).
- A summary has no content: no event, no envelope, no address, and no payload value. `DeliveryState.LastError` can contain the error message of a provider.
- `OutgoingMessageHistory` is the store result of `GetAsync`: the whole message as its closed type, and all its publish tries in the order of their numbers.
- `MessageCount` counts the messages of one message type, one channel, and one status. The channel is null for the inbox. `OldestDueAtUtc` is the earliest `NextTryAtUtc` of the counted messages, only for the status `Pending`.
- `ChannelStatus` joins the descriptor of a registered channel, its down state from the store, and the number and the oldest due time of its pending messages in all tenants, because a channel is global (D106).
- `DeliveryActionResult.Matched` counts the messages that the query found. `Changed` counts the messages that the action changed. The difference is the messages that the rules of the action skip.

Actions:
- `GetChannelsAsync` returns one `ChannelStatus` for each registered channel, from `IMessageRegistry`. It calls `GetDownChannelsAsync`, and `CountAsync` of the outbox dispatch store with the status `Pending`, one time each.
- `PauseChannelAsync(channel, note)` calls `SetChannelDownAsync` with the reason `Operator`, the time from `TimeProvider`, and the note. Each publisher instance of the channel stops at its next claim (D106).
- `ResumeChannelAsync(channel)` calls `SetChannelUpAsync` with no reason, so it ends a down state of each reason. Then it notifies the topic of the channel. If the provider still fails, the first transient error marks the channel down again (section 4.9).
- `GetTrafficAsync(query)` calls `CountAsync` of the inbox tenant store and of the outbox tenant store, one time each.
- `GetReceivedMessagesAsync` and `GetOutgoingMessagesAsync` call `QueryAsync` of their tenant store.
- `GetOutgoingMessageAsync(id)` calls `IOutboxStore.GetAsync`, and returns the summary, the route trace, and all publish tries, with no envelope. An unknown id gives `Errors.NotFound`.
- `GetReceivedContentAsync(id)` and `GetOutgoingContentAsync(id)` return the whole message, with its event or its envelope (D109). An unknown id gives `Errors.NotFound`.
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

Validation. The manager returns `Errors.ValidationFailed`, and calls no store, in these cases:
- `Limit` is less than 1 or more than 500.
- `Ids` is empty, or has more than 1,000 ids.
- `CreatedFromUtc` is not before `CreatedToUtc`.
- A bulk action has a query with no filter. One call then cannot retry or cancel the whole store. `Limit`, `After`, and `IncludeTest` are not filters for this rule.
- The note of `PauseChannelAsync` has more than 500 characters.
- A manager of a tenant gets a call of `PauseChannelAsync`, `ResumeChannelAsync`, `DeleteTestDataAsync`, or `PurgeAsync`.
- `PurgeAsync` has a `maxCount` less than 1, or a `finishedBeforeUtc` after the time from `TimeProvider`.

`PauseChannelAsync` and `ResumeChannelAsync` with a channel that is not registered give `Errors.NotFound`.

The library has no user concept for an operator. `DeliveryManagerLoggingWrapper` logs each change at the Information level: the action, the channel, the filters of the query, and the counts. The host adds who did the action, for example in its HTTP log.

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
| Content | GET | `{prefix}/inbox/{id}/content` | none | 200 with the received message and its event |
| Content | GET | `{prefix}/outbox/{id}/content` | none | 200 with the outgoing message and its envelope |

- `ids` can repeat in a query string. Each route takes the optional query parameter `tenantId`. It selects the `IDeliveryManager` of `TenantKey.For(tenantId)`, or of `TenantKey.None` with no parameter (D126). The pause, resume, and purge routes act on all tenants, so they take no `tenantId`, and a `tenantId` gives 400. A cursor is the text `{CreatedAtUtc}_{Id}`, with the time in the ISO 8601 format of D57.
- The JSON of the endpoints uses camelCase names and enum values as strings, through a source-generated `JsonSerializerContext`. The event, the envelope, and the route trace use `MsgHatchJson` with the content type registry.
- `Errors.ValidationFailed` gives 400, `Errors.NotFound` gives 404, and each other error gives 500. The body is a `ProblemDetails` with the error name and message.

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
- `DisplayName` has at most 200 characters, and `Description` at most 2,000 characters. Null means the value of the code.
- `ChannelEntry.DisplayName` replaces `ChannelDescriptor.Name` in the reader. `MessageTypeEntry.DisplayName` replaces `MessageTypeDescriptor.DisplayName`.
- `DisabledChannels` has only channels that the message type registered, each one time.
- A channel or a message type with no entry has the values of the code, and is enabled.
- The registry settings belong to the whole system, not to a tenant (D114).

`StoredRegistry`, selected with `UseStoredRegistry()`, is one singleton that implements `IMessageRegistry` and `IRegistryWriter`:
- It keeps a frozen snapshot: the descriptors of the code, merged with the entries of `IRegistrySettingsStore`. The entry wins for each value that it has. A read uses the snapshot, with no store call.
- A timer of `TimeProvider` reads the store again after each `RegistryOptions.RefreshInterval`, and the registry swaps the snapshot after each read. A write in its own process also swaps the snapshot. So another process sees a write within one `RefreshInterval`. It has the comment `ponytail: a refresh interval in each process, a shared cache or a change signal if a change must reach all processes at once`.
- Before the first successful read, the snapshot has the values of the code. A store error on a read keeps the last snapshot, and the registry logs it at the Warning level.
- `GetEntriesAsync` reads the entries of the store, with their revisions.
- `SaveChannelAsync` and `SaveMessageTypeAsync` validate the entry. The `Revision` of the entry is the expected revision, and 0 means a new entry. They save the entry with the next revision and the time from `TimeProvider` as `LastModifiedAtUtc`, and return the saved entry. An entry of an unregistered channel or message type gives `Errors.NotFound`. An invalid entry gives `Errors.ValidationFailed`. An expected revision that is not the stored revision gives `Errors.Conflict` (D79).
- `ResetChannelAsync` and `ResetMessageTypeAsync` delete the entry, so the values of the code apply again.

Effect on the delivery (D118, section 4.8, steps 2 and 3):
- A disabled message type gives no outgoing message. The pipeline does not call its router.
- A route on a disabled channel, or on a channel in `DisabledChannels` of its message type, is skipped.
- A message that is already in the outbox is still sent, because the settings act in the pipeline. To hold the messages of a channel, a person pauses the channel (D106).
- The settings do not change the stored subscriptions.

Registry settings stores:
- `InMemoryRegistrySettingsStore`, which `UseInMemoryStores()` also registers. It is not durable.
- The file store keeps one file for each entry: `{RootPath}/registry/channels/{channel}.json` and `{RootPath}/registry/message-types/{type}.json`.
- SQLite has the tables `MsgHatchChannelEntry` and `MsgHatchMessageTypeEntry`, with the entry fields as columns, `DisabledChannels` as a JSON array, and `Revision` for the update with the expected revision.
- The EF Core and MongoDB packages add their settings stores in their phases.
- Each store passes `RegistrySettingsStoreTestsBase` (section 5.5).

HTTP endpoints. `MapMsgHatchRegistry(this IEndpointRouteBuilder routes, string prefix = "/registry")` in `MsgHatch.AspNetCore` maps these endpoints, and returns the `RouteGroupBuilder`. The host adds the authorization.

| Method | Path | Body | Success |
|---|---|---|---|
| GET | `{prefix}/channels` | none | 200 with each channel: its id, name, description, features, enabled flag, and entry revision |
| PUT | `{prefix}/channels/{channel}` | `displayName`, `description`, `enabled`, and `revision` | 200 with the saved entry |
| DELETE | `{prefix}/channels/{channel}` | none | 204 |
| GET | `{prefix}/message-types` | none | 200 with each message type: its name, display name, description, channels, enabled flag, disabled channels, and entry revision |
| PUT | `{prefix}/message-types/{type}` | `displayName`, `description`, `enabled`, `disabledChannels`, and `revision` | 200 with the saved entry |
| DELETE | `{prefix}/message-types/{type}` | none | 204 |

- The GET routes join `IMessageRegistry` with `IRegistryWriter.GetEntriesAsync`, so they show the revision of each entry, or 0 for no entry.
- `Errors.ValidationFailed` gives 400, `Errors.NotFound` gives 404, `Errors.Conflict` gives 409, and each other error gives 500. The body is a `ProblemDetails` with the error name and message.
- With the in-memory reader, `MapMsgHatchRegistry` maps only the GET routes, because a setup in code has no writer. Each revision is then 0.

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
- The broker client publishes inside the `send` span. A broker library with its own tracing makes its own span as a child of `Activity.Current`, and writes its trace context into its own message headers. It does this only when its `ActivitySource` has a listener, so a host that wants the trace across the broker also adds the source of the library, for example `AddSource("Wolverine")`. Phase 6 records the header names of Wolverine in the checked facts below. For MassTransit, the headers are `MT-Activity-Id` and `MT-Activity-Correlation-Context`, not `traceparent`.

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
| MassTransit makes its send span from its `ActivitySource` `MassTransit`, as a child of `Activity.Current`. It writes the context into `MT-Activity-Id` and `MT-Activity-Correlation-Context`. With no listener, it makes no span. | [LogContextActivityExtensions.cs](https://github.com/masstransit/masstransit/blob/develop/src/MassTransit/Logging/Diagnostics/LogContextActivityExtensions.cs), [Discussion #5418](https://github.com/MassTransit/MassTransit/discussions/5418) |

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
├── MsgHatch.slnx
├── docs/
│   ├── design.md
│   ├── roadmap.md
│   ├── subscription-domain.md
│   └── stores.md                                       (phase 13)
├── src/
│   ├── GeneratedCodeSuppressions.cs                    (phase 1)
│   ├── MsgHatch.Domain/                                (phase 1)
│   ├── MsgHatch.SourceGeneration/                      (phase 3)
│   ├── MsgHatch.Email/                                 (phase 3)
│   ├── MsgHatch.Email.AmazonSES/                       (phase 4)
│   ├── MsgHatch.SignalR/                               (phase 5)
│   ├── MsgHatch.Broker/                                (phase 6)
│   ├── MsgHatch.Broker.Wolverine/                      (phase 6)
│   ├── MsgHatch.Data.FileSystem/                       (phase 7)
│   ├── MsgHatch.Data.Sqlite/                           (phase 8)
│   ├── MsgHatch.Subscriptions/                         (phase 9)
│   ├── MsgHatch.Subscriptions.Data.FileSystem/         (phase 10)
│   ├── MsgHatch.Subscriptions.Data.Sqlite/             (phase 10)
│   ├── MsgHatch.Subscriptions.Caching.Hybrid/          (phase 11)
│   ├── MsgHatch.Subscriptions.Caching.FusionCache/     (phase 11)
│   ├── MsgHatch.Subscriptions.AspNetCore/              (phase 12)
│   ├── MsgHatch.AspNetCore/                            (phase 15)
│   ├── MsgHatch.Data.EntityFrameworkCore/              (phase 18)
│   ├── MsgHatch.Subscriptions.Data.EntityFrameworkCore/ (phase 18)
│   ├── MsgHatch.Data.MongoDb/                          (phase 19)
│   └── MsgHatch.Subscriptions.Data.MongoDb/            (phase 19)
├── tests/                                                                   (section 5.5)
│   ├── MsgHatch.Domain.Tests.Unit/                                          (phase 1)
│   ├── MsgHatch.SourceGeneration.Tests.Unit/                                (phase 3)
│   ├── MsgHatch.Email.Tests.Unit/                                           (phase 3)
│   ├── MsgHatch.Email.AmazonSES.Tests.Unit/                                 (phase 4)
│   ├── MsgHatch.SignalR.Tests.Unit/                                         (phase 5)
│   ├── MsgHatch.Broker.Tests.Unit/                                          (phase 6)
│   ├── MsgHatch.Broker.Wolverine.Tests.Unit/                                (phase 6)
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
│   ├── MsgHatch.Email.Tests.Performance/                                    (phase 13)
│   ├── MsgHatch.Subscriptions.Tests.Performance/                            (phase 13)
│   ├── MsgHatch.AspNetCore.Tests.Unit/                                      (phase 15)
│   ├── MsgHatch.Data.EntityFrameworkCore.Tests.Integration/                 (phase 18)
│   ├── MsgHatch.Subscriptions.Data.EntityFrameworkCore.Tests.Integration/   (phase 18)
│   ├── MsgHatch.Data.MongoDb.Tests.Integration/                             (phase 19)
│   └── MsgHatch.Subscriptions.Data.MongoDb.Tests.Integration/               (phase 19)
└── examples/
    └── ExampleApp/                                     (phase 13)
```

A phase creates a project only when it puts code into it. Each project is added to `MsgHatch.slnx` with `dotnet sln add`.

Feature folders of `MsgHatch.Domain` (D50): `Failures`, `Identity`, `Routing`, `Delivery`, `Persistence`, `Pipeline`, `Inbox`, `Outbox`, `Dispatch`, `Management`, `Telemetry`, `Time`, and `DependencyInjection`. Feature folders of `MsgHatch.Email`: the root for the channel types, `Templates`, `Placeholders`, `Engines`, and `DependencyInjection`. Feature folders of `MsgHatch.Subscriptions`: the root for the subscription types, `Resolution`, `Management`, `Caching`, `Verification`, and `DependencyInjection`.

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
- `EnforceCodeStyleInBuild` `true`, so that the rules of `.editorconfig` stop the build.
- A `PackageReference` to `Microsoft.CodeAnalysis.BannedApiAnalyzers` with `PrivateAssets="all"`, and `<AdditionalFiles Include="$(MSBuildThisFileDirectory)BannedSymbols.txt" />` (D32 and D63).
- For the projects under `src/`:
  - `TreatWarningsAsErrors` `true` (D51).
  - `GenerateDocumentationFile` `true`.
  - `EnableConfigurationBindingGenerator` `true` (D65).
  - A compile link to `src/GeneratedCodeSuppressions.cs` (D32).
  - `IsPackable` `true`.
  - The package metadata: `Authors` `Tamás Bilik`, `RepositoryUrl` `https://github.com/biliktamas79/MsgHatch`, and `PackageLicenseExpression` `Apache-2.0`.
- For each project whose name ends with `.Tests.Unit`, `.Tests.Integration`, or `.Tests.Performance`:
  - `OutputType` `Exe`, and `IsTestingPlatformApplication` `false` (D52).
  - `NoWarn` for `CA1707`, because the test names have underscores.
  - `NoWarn` for `CA2012`, because the setup of a substitute calls a method that returns a `ValueTask` and does not await it.
- The unit and integration test projects also get the test packages of section 5.5, and `RunSettingsFilePath` `coverage.runsettings` (D70).
- For all other projects, `IsPackable` `false`.

`Directory.Packages.props` sets `ManagePackageVersionsCentrally` `true` (D48). It lists one `PackageVersion` for each package in section 4.2, with the latest stable version at the start of phase 1. The one exception is `Microsoft.CodeAnalysis.BannedApiAnalyzers` 3.3.4 (D32). A later phase adds the packages of its projects. A package version changes only in its own commit.

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

### 5.3 Naming and code conventions

These conventions apply to all code:
- Projects and namespaces are `MsgHatch.<Area>[.<Provider>]`, for example `MsgHatch.Email.AmazonSES`. The folder path is the namespace (D50).
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

The tests follow the `core:automated-tests` skill of the Transcend agent tooling.

**Test projects (D66).** One test project exists for each production project and each level. A phase creates a test project when it adds the first test for that project.

| Test project | Level | Real dependency | Phase |
|---|---|---|---|
| `MsgHatch.Domain.Tests.Unit` | Unit | none | 1 |
| `MsgHatch.SourceGeneration.Tests.Unit` | Unit | none | 3 |
| `MsgHatch.Email.Tests.Unit` | Unit | A temporary folder for the file template store | 3 |
| `MsgHatch.Email.AmazonSES.Tests.Unit` | Unit | none. The AWS client is a substitute. | 4 |
| `MsgHatch.SignalR.Tests.Unit` | Unit | none. The hub context is a substitute. | 5 |
| `MsgHatch.Broker.Tests.Unit` | Unit | none. The broker client is a substitute. | 6 |
| `MsgHatch.Broker.Wolverine.Tests.Unit` | Unit | none. The Wolverine message bus is a substitute. | 6 |
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
| `MsgHatch.Email.Tests.Performance` | Performance | none | 13 |
| `MsgHatch.Subscriptions.Tests.Performance` | Performance | none | 13 |
| `MsgHatch.AspNetCore.Tests.Unit` | Unit | none. A `TestServer` runs the endpoints in memory, with doubles. | 15 |
| `MsgHatch.Data.EntityFrameworkCore.Tests.Integration` | Integration | SQL Server in a container, through Testcontainers (D124) | 18 |
| `MsgHatch.Subscriptions.Data.EntityFrameworkCore.Tests.Integration` | Integration | SQL Server in a container, through Testcontainers (D124) | 18 |
| `MsgHatch.Data.MongoDb.Tests.Integration` | Integration | MongoDB in a container, through Testcontainers (D124) | 19 |
| `MsgHatch.Subscriptions.Data.MongoDb.Tests.Integration` | Integration | MongoDB in a container, through Testcontainers (D124) | 19 |

Packages:
- `Directory.Build.props` gives each project whose name ends with `.Tests.Unit`, `.Tests.Integration`, or `.Tests.Performance` the property `OutputType` `Exe`. It gives each unit and integration test project references to `xunit.v3`, `Microsoft.NET.Test.Sdk`, `xunit.runner.visualstudio`, `NSubstitute`, `NSubstitute.Analyzers.CSharp`, `Microsoft.Extensions.TimeProvider.Testing`, `Microsoft.Extensions.Diagnostics.Testing`, and `coverlet.collector` (D70).
- `MsgHatch.Domain.Tests.Unit` also references `Microsoft.Extensions.DependencyInjection` and `Microsoft.Extensions.Configuration`, for the DI container and the in-memory configuration of its tests. `MsgHatch.SourceGeneration.Tests.Unit` also references `Microsoft.CodeAnalysis.CSharp`. `ExampleApp.Tests.Unit` also references `OpenTelemetry.Exporter.InMemory`. The EF Core integration test projects also reference `Testcontainers.MsSql`, and the MongoDB integration test projects `Testcontainers.MongoDb`. `MsgHatch.AspNetCore.Tests.Unit` and `MsgHatch.Subscriptions.AspNetCore.Tests.Unit` also reference `Microsoft.AspNetCore.TestHost`. The performance projects reference `BenchmarkDotNet`.
- Each test project references the production project under test. A project with subclasses of a contract base class also references the test project that holds the base class.
- Before a phase adds a package, it reads the license of the pinned version from the package metadata. Only MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, and ISC are allowed. A dual license is allowed when one of its licenses is allowed. No license is taken from memory.
- Assertions use the `Assert` class of xUnit. Fakes of the clock and the logger come from the Microsoft testing packages: `FakeTimeProvider` and `FakeLogger`. All other doubles are NSubstitute substitutes (D14).

Stages. Each level runs in its own stage, and one glob on the path selects it:

| Level | Glob | Stage |
|---|---|---|
| Unit | `**/*.Tests.Unit/*` | The validation of each pull request |
| Integration | `**/*.Tests.Integration/*` | After the merge, on an agent with a container runtime. The EF Core and MongoDB phases add the first projects of this level (D124). |
| Performance | `**/*.Tests.Performance/*` | A manual or nightly run on a dedicated machine |

The pipeline itself is out of scope. A unit test never needs a database server or a service, so the unit stage needs no infrastructure. The SQLite tests need only the native SQLite library, which the `Microsoft.Data.Sqlite` package contains, and a temporary folder (D71).

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
| `BrokerClientTestsBase<TClient>` | `MsgHatch.Broker.Tests.Unit` | `IBrokerClientTestDriver` | `WolverineBrokerClientUnitTests` (6) |
| `RegistrySettingsStoreTestsBase<TStore>` | `MsgHatch.Domain.Tests.Unit` | `IRegistrySettingsStoreTestData` | `InMemoryRegistrySettingsStoreUnitTests` (16), `FileSystemRegistrySettingsStoreUnitTests` (16), `SqliteRegistrySettingsStoreUnitTests` (16), `EntityFrameworkRegistrySettingsStoreIntegTests` (18), `MongoDbRegistrySettingsStoreIntegTests` (19) |
| `TemplateStoreTestsBase<TStore>` | `MsgHatch.Email.Tests.Unit` | `ITemplateStoreTestData` | `FileSystemTemplateStoreUnitTests` (3), `EmbeddedResourceTemplateStoreUnitTests` (3) |
| `EmailTemplateEngineTestsBase<TEngine>` | `MsgHatch.Email.Tests.Unit` | `IEmailTemplateTestData` | `PlaceholderEmailEngineUnitTests` (3) |
| `EmailClientTestsBase<TClient>` | `MsgHatch.Email.Tests.Unit` | `IEmailClientTestDriver` | `AmazonSimpleEmailServiceClientUnitTests` (4) |
| `SubscriptionStoreTestsBase<TStore>` | `MsgHatch.Subscriptions.Tests.Unit` | `ISubscriptionStoreTestData` | `InMemorySubscriptionStoreUnitTests` (9), `FileSystemSubscriptionStoreUnitTests` (10), `SqliteSubscriptionStoreUnitTests` (10), `EntityFrameworkSubscriptionStoreIntegTests` (18), `MongoDbSubscriptionStoreIntegTests` (19) |
| `SubscriptionCacheBackendTestsBase<TBackend>` | `MsgHatch.Subscriptions.Tests.Unit` | `ISubscriptionCacheBackendTestData` | `HybridCacheSubscriptionBackendUnitTests` (11), `FusionCacheSubscriptionBackendUnitTests` (11) |
| `RecipientDirectoryTestsBase<TDirectory>` | `MsgHatch.Subscriptions.Tests.Unit` | `IRecipientDirectoryTestData` | `ExampleRecipientDirectoryUnitTests` (13) |

Rules for a base class:
1. The type parameter is constrained to the interface, for example `where TStore : class, IOutboxDispatchStore`. A store base class also takes a function that gives the tenant store of a key, so that it tests both parts of the store (D127).
2. The constructor takes the implementation under test and its test data abstraction. The base class never names a concrete type.
3. The test data abstraction does what the interface cannot do. It resets the storage, seeds a scenario, and reads the stored state back, for example the publish tries of a message. A driver abstraction makes the provider behind a client or a transport succeed, fail, or wait.
4. A real difference between implementations is a constructor parameter, not a reason to copy the tests.
5. The base class is `partial`, with one file for each operation of the interface.
6. The base class owns a `FakeTimeProvider` that starts at `2030-01-01T00:00:00Z`, and gives each time to the store (section 4.14).
7. The base class holds the shared factories and assertions as protected members. Each test method passes in each value that it asserts.
8. The base class resets the data through `IAsyncLifetime`: before and after each test for a test that writes, and one time for a group that only reads one seeded scenario.

Rules for a subclass:
1. A subclass has a class declaration and one constructor. The constructor builds the implementation and its test data abstraction, and gives them to the base class.
2. A subclass adds a test method only for a behavior that is unique to its implementation, for example the version from the last write time of a template file.
3. A subclass that needs an xUnit fixture declares it with `IClassFixture<T>` or `[Collection]`. Without the declaration, xUnit cannot construct the subclass.
4. A store outside this repository, for example a SQL Server store of a consumer, gets the same coverage with one subclass. A package that publishes the base classes is out of scope.

Use cases of the store base classes. The phases add the use cases of the other base classes:
- `AddAsync`: an empty batch, one message, several messages, a duplicate id inside one batch, and an id that is already stored. A duplicate id is stored one time.
- The tenants (D126, D127): the tenant store of one key does not see the messages of another key, and a message with another tenant gives `Errors.ValidationFailed`. For the inbox, the same id with two tenants gives two messages.
- `ClaimDueAsync`: `maxCount` 0 gives `Errors.ValidationFailed`. With `maxCount` 1 and 2, the claim returns at most that number. A message that is not due yet is not claimed. The lease boundary has three rows: a claim at T + 29, T + 30, and T + 31 seconds after a claim with a lease of 30 seconds.
- `ClaimDueAsync` returns each message as its registered closed type, with its tenant, and without its tries. It claims the messages of all tenants. Two parallel claims get different messages.
- `ClaimDueAsync` gives `NextDueAtUtc`: null with no pending message, and else the earliest `NextTryAtUtc` of the pending messages after the claim, which includes the lease end of a claimed message. For the outbox, a message of another channel does not count.
- `IOutboxDispatchStore.ClaimDueAsync` claims only messages of its channel.
- The down state of a channel (D106). `SetChannelDownAsync` with the reason `Errors` writes one time, and a second call returns false and keeps the first state. The reason `Operator` replaces it. `SetChannelUpAsync` with the reason `Errors` does not end the reason `Operator`, and with no reason it ends each reason. Of two parallel `TryStartChannelCheckAsync` calls, exactly one returns true. A claim of a down channel returns no message, and returns the down state.
- From phase 15, the management members (section 4.19): each filter of `QueryAsync` and a combination of filters, the order newest first, a cursor over three pages while a message changes its status, `IncludeTest`, `DueBeforeUtc`, the counts of `CountAsync` with `OldestDueAtUtc`, `GetAsync` with all tries in order, and the rules of `RetryAsync` and `CancelAsync` for each status, for an active lease, and for an expired message. `CountAsync` of a tenant store counts only its tenant, and of the outbox dispatch store all tenants. `PurgeAsync` deletes only finished messages before the given time, at most `maxCount`, and keeps each pending message. A cancelled received message with no try is purged by its `CreatedAtUtc`.
- `RecordTryAsync`: each `PublishOutcome` gives the status of section 4.6. All tries stay in order, and a try number that is not the stored `TryCount` plus 1 changes nothing.
- Each stored text round-trips unchanged, also with accented characters, for example Hungarian `ő` and `ű`, and with special characters, for example `"`, `\`, `<`, and `&`.
- `MessageFlags` round-trip unchanged. `DeleteTestDataAsync` deletes the test messages of all tenants and their tries, keeps each message without the flag, and returns the number of deleted messages. A second call returns 0.

**Test design rules:**
- A unit test is preferred. An integration test exists only for a real out-of-process dependency: SQL Server for the EF Core stores, and MongoDB for the MongoDB stores (D124). SQLite runs in process, so its tests are unit tests (D71).
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
- Each SQLite test uses its own database file in its own temporary folder, so the SQLite tests can run in parallel with the other unit tests. The test deletes its folder at the end. The connection string comes from a configuration layer of the test, bound to `SqliteStoreOptions` (D65), not from code. A file, not an in-memory database, is necessary, because the tests open two connections and two store instances on the same database.
- A run that reports 0 tests is a failure. VSTest can report an empty xUnit v3 run as a pass, so each run's test count is read.
- Each new test fails first (section 5.3).

## 6. Risks

- **Duplicate emails.** Delivery is at-least-once. If the process stops after SES accepts an email and before `RecordTryAsync`, the email is sent again. SES has no idempotency key, so this risk stays. The second success is a second publish try, so the history shows the duplicate.
- **Lease and buffer.** A message can wait in the work queue before its send. If its lease ends, another instance can claim and send it too. Both senders then record try number 1, the store ignores the second record, and the history shows one try for two sends. The lease safety margin (section 4.9) prevents this in normal operation. Keep `Lease` much longer than the slowest send.
- **Contract discipline.** The golden-file tests find a breaking change only in a type that has samples. A consumer type with no samples can still break the reading of stored records. Such a failure gives `Errors.SerializationFailed`, a permanent error. `docs/stores.md` and the example app show how to add samples.
- **Storage growth.** Finished messages and their publish tries stay in the store until `IDeliveryManager.PurgeAsync` deletes them (D128). The host calls it from its own timer with its retention, and a host that never calls it keeps each message. The retention is also the window of the duplicate detection of the inbox. `MaxTryCount` limits the number of tries for each message.
- **Stale subscriptions.** With the cache on, a change that does not go through the subscription store, for example a direct database update, takes effect only after `InvalidateAsync` or after the expiration. With many instances, an invalidation reaches the other instances only through a FusionCache backplane. With HybridCache, the local level of another instance keeps its entry until its local expiration. Keep the expiration short.
- **SQLite limits.** SQLite allows one writer at a time, so the claims and the try records of all dispatchers wait for each other. This suits one machine and a moderate volume. The database file must be on a local disk.
- **Management authorization.** The HTTP endpoints accept each company id and user id in their filters and drafts. The consumer must protect them with an authorization policy, or give end users only the `/me` routes.
- **SignalR group membership.** The host's hub must add each connection to the group of the company of its user. A connection that is in no group gets no company notification.
- **Large company scopes.** A company scope on a channel without group delivery, for example email, sends one message to each user of the company. A very large company gives a large fan-out in one event (D43).
- **Claim atomicity.** The consumer's store must claim messages atomically. The example app and the file stores show correct implementations. The library cannot check a consumer's store.
- **File store limits.** The file stores depend on an atomic rename. Some network file systems do not guarantee it. Use a database store there.
- **Separate publisher processes.** The in-process signal does not reach another process. A publisher in its own process then finds new messages at its next poll, at most `MaxPollInterval` later, until a signal between processes exists (D102). Keep `MaxPollInterval` short for the SignalR publisher. All processes need the same stores: SQLite works only on one machine, and the file stores need a folder with atomic rename.
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
- **Generator scope.** The generator supports top-level properties only. A template cannot read a nested property, for example `{{Customer.Name}}`.

## 7. Out of scope

- The Razor template engine `MsgHatch.Email.Razor` (D45).
- Loops, conditions, and nested properties in placeholder templates.
- A user interface for subscription management or for delivery management. The APIs serve a UI of the consumer.
- A library recipient directory. The company, the UI language, and the addresses of a user are data of the consumer (D76).
- A resend of a delivered message. A retry skips a `Done` message (D111).
- A time series of the traffic. A UI calls `GetTrafficAsync` for each time window.
- A retention timer in the library. The host calls `IDeliveryManager.PurgeAsync` from its own timer (D128).
- A dispatch signal between processes or instances, for example a named pipe, a message broker, MongoDB change streams, or SQL Server query notifications. A later package implements `IDispatchSignal` for it (D102).
- Streams for very large fan-outs, for example 100,000 recipients for one event. The benchmarks show if chunks are necessary (D43).
- SMS and push channels.
- Database schema migrations for the consumer's stores.
- An analyzer that checks the versioning rules at compile time. The golden-file tests check them instead.
- EF Core providers other than SQL Server. A later phase adds PostgreSQL (roadmap, later phases).
- Registry settings and transports for each tenant, for example one SES account for each tenant. The registry and the channels belong to the whole system (D114).
- The publication of NuGet packages.
- A `MsgHatch.OpenTelemetry` extension package. The host uses the names of `MsgHatchTelemetry` (D131).
- Broker frameworks with their own outbox, for example Brighter and CAP (DotNetCore.CAP). They duplicate the MsgHatch outbox, its retries, and its history (D134).
- The tenant as a metric tag. An opt-in option can come later (D133).
- Spans for store calls, template rendering, or each route.
