# MsgHatch roadmap

The roadmap holds the order of the phases of MsgHatch, and each phase with its items, tests, and commits. The design `docs/design.md` holds the requirements, terms, decisions, architecture, and conventions. In this document, a section number refers to a section of the design, for example "section 4.5", unless the text names another document. A decision number refers to a decision of the design, for example D122.

## Order

Each phase builds on the phases of its row. Phases with no dependency between them can run in any order, but the numbers give the default order:

| Phase | Needs |
|---|---|
| 1 Core to the outbox | 0 |
| 2 Dispatch | 1 |
| 3 Email composition | 1 |
| 4 Email delivery with Amazon SES and Resend | 2, 3 |
| 5 SignalR channel | 2, 4 |
| 6 Broker channel with Wolverine | 2, 4 |
| 7 JSON file stores | 2 |
| 8 SQLite stores | 2 |
| 9 Subscription router | 1, 3, 5, 6 |
| 10 Subscription stores | 7, 8, 9 |
| 11 Caching | 9 |
| 12 Subscription HTTP endpoints | 9 |
| 13 Example, measurements, and documentation | 4, 5, 6, 8, 10, 11, 12 |
| 14 Routing verification | 13 |
| 15 Delivery management | 7, 8, 13 |
| 16 Registry administration | 15 |
| 17 A store for each tenant | 10, 13, 15, 16 |
| 18 EF Core stores | 15, 16 |
| 19 MongoDB stores | 15, 16 |
| 20 Amazon SES against a real account | 4 |
| 21 Dispatch signal between processes | 2, 13 |
| 22 Razor email templates | 3 |
| 23 Liquid email templates | 3, 22 |
| 24 Template library | 23 |
| 25 HTTP API of the templates | 13, 24 |
| 26 Query and command interfaces | 12, 14, 25 |
| 27 HTTP API clients | 26 |
| 28 Admin UIs | 27 |
| 29 Template preview | 28 |
| 30 Layout of the admin pages | 29 |
| 31 Registry queries and pages | 16, 30 |
| 32 Channel state and page order | 31 |
| 33 Channel check | 32 |
| 34 Open allocations | 13, 33 |
| 35 Registry commands | 33 |
| 36 Delivery API | 27, 31 |
| 37 Delivery pages | 35, 36 |
| 38 Amazon SNS broker client | 6, 20 |
| 39 MongoDB change stream signal | 19, 21 |
| 40 RabbitMQ dispatch signal | 21 |
| 41 Versions and NuGet packages | 39, 40 |
| 42 Package READMEs | 41 |

The core comes first and works with no database (D122). Each later phase adds one channel, one storage technology, the caching, or one administration API, and ends in a tested and working state.

## Phases

Each phase lists its items, its tests, its commits, and its done criteria. No phase creates a type, a file, or a project that a later phase removes or renames. A later phase only adds code, or adds members to existing types.

### Phase 0: repository start

Goal: `main` contains the plan, the README, and the agent guidance.

1. Commit "Add design, roadmap, subscription domain, and project README" with `docs/design.md`, `docs/roadmap.md`, `docs/subscription-domain.md`, and `README.md`.
2. Commit "Add agent guidance" with `AGENTS.md`.
3. Push `main`, and add the annotated tag `phase-0`.

### Phase 1: core to the outbox

Goal: an event goes in through `ProcessAsync`, its router gives the routes, the composers build the envelopes, and the outgoing messages are in the in-memory outbox (D122). No database, no dispatcher, and no channel package. The tests use a test router, and a test channel with the envelope `TestEnvelope(string Text)` and its composer.

1. Add `global.json`, `Directory.Build.props`, `Directory.Packages.props`, `.editorconfig`, `BannedSymbols.txt`, `nuget.config`, and `MsgHatch.slnx` (section 5.2). Add `tests/MsgHatch.Domain.Tests.Unit` (section 5.5). Add `coverage.ps1`, `coverage.runsettings`, and `dotnet-tools.json` with ReportGenerator (D70).
2. Add `src/MsgHatch.Domain` with `Failures/ErrorType.cs`, `Failures/Error.cs`, `Failures/ErrorOr.cs`, and `Failures/Errors.cs` (section 4.4).
3. Add `Identity/MessageType.cs`, `ChannelId.cs`, `UserId.cs`, `CompanyId.cs`, `TenantId.cs`, and `TenantKey.cs`, with the name validation of section 4.4 (D114, D126). Add the internal time helper `Time/TimeProviderExtensions.cs` (section 4.14).
4. Add the routing types of section 4.4 in `Routing/`: `DeliveryTargetKind`, `DeliveryTarget`, `ChannelSettings`, `ChannelFeatures`, `ChannelDescriptor` with `ChannelDescriptor<TSettings>`, `RouteTrace`, `MessageRoute`, and `MessageTypeDescriptor`. Add `IChannelSettingsValidator` (section 4.6), and `RouteGrouping` with `ByChannel` and `ByLanguageAndScope`.
5. Add `IMessageRegistry` (D117). Item 9 adds the internal in-memory reader, because only the registrations fill it. The writer comes in phase 16.
6. Add `Delivery/MessageFlags.cs`, `MessageContext.cs`, `DeliveryStatus.cs` with `Pending`, `Done`, and `Failed`, `DeliveryState.cs`, `ReceivedMessage.cs`, `ReceivedMessageOfT.cs`, `OutgoingMessage.cs` with `Content`, and `OutgoingMessageOfT.cs` (section 4.4). The publish tries come in phase 2. Add `Routing/IMessageRouter.cs` (section 4.6), because `RouteAsync` takes a `ReceivedMessage<TEvent>`.
7. Add `Identity/DeterministicId.cs` (section 4.8), and `Delivery/RetryOptions.cs` with `RetryOptionsValidator`. Phase 1 uses `MaxTryCount` and `Expiry` for the outgoing messages. Phase 2 uses the delays.
8. Add `Persistence/IContentTypeRegistry.cs`, `ContentTypeEntry.cs`, and `MsgHatchJson`, with the discriminator of a route trace (section 4.5). Item 9 adds the internal `ContentTypeRegistry`, because only the registrations fill it.
9. Add `DependencyInjection/MsgHatchOptions.cs` with `MsgHatchOptionsValidator`, `ServiceCollectionExtensions.cs` with `AddMsgHatch`, and `MsgHatchBuilder` with `Services`, `AddChannelType`, `AddTraceType`, `UseRouter`, and `AddMessage`. Add `MessageBuilder<TEvent>` with `Services`, `WithRouter`, `WithUpgrade`, and `AsChannel`, and `ChannelBuilder<TEnvelope>` with `WithUpgrade` and `WithRetry` (section 4.7):
   - Add the internal registration of a tenant service with `KeyedService.AnyKey`, its wrappers, and the unkeyed registration for `TenantKey.None` (D126). Each later tenant service uses it.
   - Add the internal `IMessageHandler`, `MessageRegistration<TEvent>`, `IChannelRegistration<TEvent>`, and `ChannelRegistration<TEvent, TEnvelope>` (section 4.3).
   - Add `Pipeline/IEnvelopeComposer.cs` (section 4.6), because `AsChannel` takes the composer.
   - Add the internal `ContentTypeRegistry`, and the internal in-memory registry reader with no logging wrapper (D117). The registrations fill them, and register the named `RetryOptions` of each channel registration (D87). `MsgHatchBuilder` records each selection of an implementation (D62).
   - Add `src/GeneratedCodeSuppressions.cs` (D32), because the binding generator of `MsgHatchOptions` reads the banned indexer.
10. Add `Outbox/IOutboxStore.cs` with `AddAsync`, and `Outbox/IOutboxDispatchStore.cs` with `DeleteTestDataAsync` (section 4.6). Add `OutboxStoreLoggingWrapper`, `OutboxDispatchStoreLoggingWrapper`, `UseOutboxStore` and `UseOutboxDispatchStore` with their factory overloads, `InMemoryOutboxStore` with its two parts and `GetMessages` (section 4.5), and `UseInMemoryStores` (D119, D127). Add the `Log` class of the outbox area (section 4.16). Add the contract base class `OutboxStoreTestsBase<TStore>` with `IOutboxStoreTestData` in `tests/MsgHatch.Domain.Tests.Unit/Contracts/`, and its subclass `InMemoryOutboxStoreUnitTests`. Phases 2 and 15 add the other members.
11. Add `Pipeline/IPayloadLoader.cs` with `MessageBuilder<TEvent>.UsePayloadLoader`, `NotificationResult.cs` with no `Skipped`, which phase 16 adds, `RouteResult.cs`, and `INotificationService.cs` with `ProcessAsync` and `PreviewAsync` (section 4.6). Add `MessageRouterLoggingWrapper<TEvent>`, `PayloadLoaderLoggingWrapper<TEvent, TPayload>`, `NotificationServiceLoggingWrapper`, and the `Log` class of the pipeline area (section 4.16). Add the internal `NotificationPipeline` with steps 2 to 6 of section 4.8, with no dispatch signal and no registry check, and `NotificationService`. Phase 2 adds the signal of step 6 and `AcceptAsync`. Phase 16 adds the registry checks of steps 2 and 3.
12. Add the test messages and the preview (sections 4.8 and 4.18).
13. Add the startup validation of section 4.7 for the role of an event consumer: the outbox stores, D62, a router for each message type, unique content types, unique message type names, and the options.
14. Add `Telemetry/MsgHatchTelemetry.cs`, the internal `ActivitySource`, and the internal `MsgHatchMetrics` with the instruments `msghatch.messages.received`, `msghatch.messages.stored`, `msghatch.routes.failed`, and `msghatch.process.duration` (section 4.21). `AddMsgHatch` calls `AddMetrics`. The pipeline starts the `process` span, records the measurements, and gives each outgoing message its `TraceParent` (D131, D132).

Tests:
- `ErrorOr<T>` conversions and `ThenIfSuccess` work for a value and for an error. `default(ErrorOr<T>)` is an error with `Errors.Uninitialized`. An `ErrorOr<T>` value result allocates 0 bytes.
- Each `Errors` field has the code and the transient flag of section 4.4.
- The identity types have value equality, and the name validation rejects an invalid name.
- `UserId`, `CompanyId`, and `TenantId` in one data-driven test, with the rows of the examples I1 to I6 of `docs/subscription-domain.md`:
  - `From` gives `"42"` for the `int` 42, `"-5"` for the `int` -5, `"9000000000"` for the `long` 9000000000, and the lowercase `D` format for a `Guid`.
  - `From` gives the same text when the current culture of the thread is `hu-HU` or `ar-SA`.
  - `TryAs<int>` reads `"42"` back, and returns false for `"abc"`.
  - `Create` returns `Errors.ValidationFailed` for null, an empty string, a string of 257 characters, and a string with a control character. It accepts 256 characters and `"auth0|abc"`. The constructor throws `ArgumentException` for the same invalid values.
  - Two ids with the texts `"A1"` and `"a1"` are not equal.
- `TenantKey.For(null)` is equal to `TenantKey.None`. Two keys with equal tenants are equal, and keys with two tenants are not equal.
- The premises of the tenant services (D126), in the pinned version of `Microsoft.Extensions.DependencyInjection`:
  - An `AnyKey` singleton gives one instance for each key, and the same instance for two resolutions with the same key.
  - An unkeyed resolution gives the instance of `TenantKey.None`.
  - The implementation gets the requested key as its `TenantKey` constructor argument.
  - A tenant service with a constructor parameter of another tenant service gets the instance of the same key. A parameter of a service that is not a tenant service gets the unkeyed instance.
- Check the analyzer one time. A temporary `DateTime.UtcNow` in `MsgHatch.Domain`, and a temporary `IConfiguration` indexer read in `MsgHatch.Domain.Tests.Unit`, must each give the build error `RS0030`. Then remove them.
- Check the test count one time. A run of `dotnet test` reports more than 0 tests for `MsgHatch.Domain.Tests.Unit`.
- `ByChannel` splits a list of routes by channel. `ByLanguageAndScope` groups the email routes by language and template scope. An empty route list gives no groups.
- `IMessageRegistry` gives each registered channel and message type, and null for an unknown one. A read allocates 0 bytes.
- `AddChannelType` adds the channel and the content type `Settings:{Channel}`. A second call for the same channel changes nothing. `AddTraceType` adds the content type `Trace:{name}`.
- `DeterministicId` gives the same value for the same inputs, and a different value for a different channel or a different tenant. The first run fixes the expected value of one golden case with no tenant and one with a tenant. It allocates 0 bytes.
- The options tests of `MsgHatchOptions` and `RetryOptions`, with the rows of section 5.5. For example, `DefaultLanguage` gets null, empty, only whitespace, `"en"`, `"hu-HU"`, and an unknown culture name. `MaxTryCount` gets the rows 0, 1, 2, 99, 100, and 101, and `BaseDelay` greater than `MaxDelay` fails.
- `MsgHatchJson` reads a written message back as the same closed type, with equal values and with its tenant. It writes camelCase names and enum values as strings (D23), and it does not write `Content`. It writes the route trace of a test trace type with its `$type` discriminator, and reads it back as that type. An unknown discriminator gives `Errors.SerializationFailed`. A message with no trace round-trips with no trace.
- A message with an old content version is read as its old closed type. A message with a newer version gives `Errors.UnsupportedVersion`.
- Golden files: a version 1 sample of `ReceivedMessage` and of `OutgoingMessage` with a route trace deserializes with the code under test.
- The standard logging wrapper tests. Each later logging wrapper gets the same tests. The test resolves the interface from a container built with `AddMsgHatch()` and the `Use…` method, with a substitute through the factory overload, and `FakeLogger` as the logger:
  - The wrapper returns the result of the inner implementation unchanged.
  - A transient error is logged at the Warning level, and a permanent error at the Error level, each with its event id (section 4.16).
  - The wrapper of a tenant service logs the tenant of its key.
  - No log record contains the channel address in the envelope of a test message.
- The standard logging wrapper tests for the outbox store, outbox dispatch store, router, payload loader, and notification service wrappers. With `EnableLogging` false, no logging wrapper is registered.
- The use cases of `OutboxStoreTestsBase` for `AddAsync` and `DeleteTestDataAsync` (section 5.5), run by `InMemoryOutboxStoreUnitTests`. They include the tenant rules: the tenant store of each key stores its messages with its own tenant, and a message of another tenant gives `Errors.ValidationFailed`. The tenant store has no read member in phase 1, so phase 15 adds the rule that the tenant store of one key does not see the messages of another key.
- Pipeline tests, with substitutes through the factory overloads, and all times from a `FakeTimeProvider` that starts at `2030-01-01T00:00:00Z`:
  - `ProcessAsync` calls the router of the message type one time with the received message. It calls each composer one time, with all routes of its channel, in the order of the router.
  - With a tenant in the context, the pipeline uses the router, the composers, and the outbox tenant store of `TenantKey.For(tenant)`. With no tenant, it uses those of `TenantKey.None` (D126).
  - `WithRouter` of one message type wins over the open generic router of `UseRouter`, for a key and for no key. A second message type with no `WithRouter` uses the open generic router.
  - The route checks of section 4.8, step 3, as rows of one data-driven test: a route for an unregistered channel gives `Errors.NotFound`, a route with settings of another type gives `Errors.ValidationFailed`, and a second route for the same target and channel gives `Errors.Conflict`. In each row, the other routes are stored, and `NotificationResult` counts the failed route.
  - An empty route list stores nothing, and `NotificationResult.Stored` is 0.
  - Each outgoing message has the tenant of the context, the message type, the target, and the trace of its route, unchanged.
  - `ProcessAsync` two times with the same context calls `AddAsync` with the same outgoing message ids both times. The same message id with another tenant gives other ids.
  - `ProcessAsync` with an unregistered event type gives `Errors.NotFound`.
  - An `Error` from the router or from `AddAsync` fails the event, and the pipeline returns that error.
  - An inner composer error for one route fails only that message, and `NotificationResult` counts it. An outer composer error fails the event.
- Retry options for each channel registration (D87):
  - A registration with `WithRetry(o => o.Expiry = TimeSpan.FromMinutes(15))` gives outgoing messages with `ExpiresAtUtc` 15 minutes after the fake time. A second registration on the same channel with no `WithRetry` keeps the expiry of the channel.
  - A value in the section `MsgHatch:Retry:{ContentType}` of an in-memory configuration changes only that value. A value from `WithRetry` wins over it.
  - `WithRetry(o => o.MaxTryCount = 0)` fails the startup validation with an `OptionsValidationException`.
- Two selections of an implementation for the same interface fail the startup validation with an `OptionsValidationException` (D62). A message type with no router fails it too.
- Test messages and previews (D84):
  - `MessageFlags.Test` of the context goes to each outgoing message.
  - `PreviewAsync` with routes stores nothing, calls no router, and returns one `RouteResult` for each route, in the same order. Each result has the same outgoing message that `ProcessAsync` stores for that route, with the test flag. A route that fails a check of step 3 gives a result with its error.
  - `PreviewAsync` with null routes calls the router of the message type one time, and returns its routes.
  - `DeleteTestDataAsync` of the outbox dispatch store deletes only the test messages, of all tenants, and returns their count.
- Pipeline telemetry (D131 to D133), with an `ActivityListener` for `MsgHatchTelemetry.SourceName`, and `MetricCollector<T>` on the `IMeterFactory` of the test container:
  - `ProcessAsync` gives one `process {MessageType}` span of the kind Consumer, as a child of the current activity of the test, with the attributes of section 4.21. A message with a tenant gives the attribute `msghatch.tenant`.
  - Each outgoing message has the id of the `process` span as its `TraceParent`. With no listener, no span starts, and each outgoing message has the id of the current activity of the test.
  - A failed event gives the span status `Error` and the attribute `error.type`, and no error message.
  - `msghatch.messages.received` gives 1, and `msghatch.messages.stored` gives 2, with the channel of each message. `msghatch.routes.failed` counts a failed route with its `error.type`. `msghatch.process.duration` records the elapsed time of the fake clock.
  - No metric has the tag `msghatch.tenant`. No span attribute and no metric tag contains the channel address of a test route.
- End to end, with `UseInMemoryStores()`, a test router of two routes with a tenant, and the test composer: `ProcessAsync` stores two messages, and the in-memory outbox has them with their tenant, type, target, and trace. This test shows the working state of the phase.

Commits:
1. "Add .NET 10 build infrastructure and solution"
2. "Add error values"
3. "Add message, channel, user, company, and tenant ids, and the tenant key"
4. "Add the routing contract, route grouping, and the registry contract"
5. "Add delivery state and message envelopes"
6. "Add deterministic outgoing message ids and retry options"
7. "Add the content type registry and MsgHatchJson"
8. "Add the MsgHatch builder, tenant services, and message registrations"
9. "Add the outbox store contracts and the in-memory outbox store"
10. "Add the notification pipeline"
11. "Add test messages and previews"
12. "Add startup validation"
13. "Add pipeline telemetry"

The review of phase 1 added 10 commits before the tag `phase-1`: more tests, the coverage report (D70), the generic channel descriptor (D77), the check of unique message type names, the type check of `UseRouter`, and smaller fixes.

Done when: the build has no errors and no warnings, all tests pass, and the end-to-end test finds the messages in the in-memory outbox.

### Phase 2: dispatch

Goal: the outbox dispatcher sends the messages of the outbox through any transport, and the inbox processor processes the accepted events. The phase runs on the in-memory stores with a test transport, in one process and with two DI containers that act as separate processes.

1. Add `Delivery/ITransport.cs` and `TransportLoggingWrapper` (D129). Add `PublishOutcome.cs` with no `Cancelled`, `PublishTry.cs`, and `PublishReceipt.cs` (section 4.4).
2. Add `Dispatch/ClaimResult.cs`, `ChannelDownReason.cs`, and `ChannelDown.cs`. Add the members `ClaimDueAsync`, `RecordTryAsync`, and the members of the down state of a channel (D106) to `IOutboxDispatchStore`. Add them to `InMemoryOutboxStore`, and their use cases to `OutboxStoreTestsBase` (section 5.5).
3. Add `Inbox/IInboxStore.cs` with `AddAsync`, and `Inbox/IInboxDispatchStore.cs` with `ClaimDueAsync`, `CompleteAsync`, `FailAsync`, and `DeleteTestDataAsync`. Add `InboxStoreLoggingWrapper`, `InboxDispatchStoreLoggingWrapper`, `UseInboxStore`, `UseInboxDispatchStore`, and `InMemoryInboxStore` with its two parts, which `UseInMemoryStores` now also registers. Add `INotificationService.AcceptAsync` (section 4.8, step 1). Add the contract base class `InboxStoreTestsBase<TStore>` with `IInboxStoreTestData`, and its subclass `InMemoryInboxStoreUnitTests`.
4. Add `Dispatch/DispatchOptions.cs` with `DispatchOptionsValidator`, `DispatchSummary.cs`, `DispatchTopic.cs`, `IDispatchSignal.cs`, `InProcessDispatchSignal`, `UseDispatchSignal`, and the notify of step 6 of section 4.8. Add the contract base class `DispatchSignalTestsBase<TSignal>`, and its subclass `InProcessDispatchSignalUnitTests`. A later signal between processes gets the same tests with one subclass (D102).
5. Add `IInboxProcessor`, `IOutboxDispatcher`, the internal `InboxProcessor` and `OutboxDispatcher` with one dispatch loop for each published channel (D101), their logging wrappers, `ConfigureInboxDispatch`, `ConfigureOutboxDispatch`, and `UseTransport<TTransport>` with its factory overload. Add the internal dispatch loop with its work queue (section 4.9), and the internal `RetrySchedule` with the next try time of section 4.14. The tests reach these internal types only through `IInboxProcessor` and `IOutboxDispatcher`, so they come in this item.
6. Add `Dispatch/ChannelDownOptions.cs` with `ChannelDownOptionsValidator`, `ConfigureChannelDown`, and the down state of the channels in the outbox loops (section 4.9, D86, D106).
7. Add `TestDeliveryOptions` with its validator, `ConfigureTestDelivery`, and the test delivery of section 4.8. Add `MsgHatchOptions.Roles` with `MsgHatchRoles`, and the startup validation of the roles (D105).
8. Add the `send` span, the `process` span of the inbox processor with its link, the `TraceParent` of `AcceptAsync`, and the instruments `msghatch.publish.tries`, `msghatch.messages.failed`, `msghatch.send.duration`, `msghatch.delivery.delay`, `msghatch.queue.length`, and `msghatch.channel.down` (section 4.21).
9. Add the startup checks of D135 to the startup validation (section 4.7): at most one registration for each channel of a message type, different envelope types for the channel registrations of a message type, and one loader type for each payload type of a message type. A second `UsePayloadLoader` with the same loader type adds nothing.
10. Add the upgrade of an old envelope version after the claim and before the send (section 4.8, publish step 4, and section 4.13).

Tests. All times come from a `FakeTimeProvider`. The stores are the in-memory stores, or substitutes through the factory overloads where a test checks the calls:
- The use cases of `OutboxStoreTestsBase` for the claim, the tries, and the down state, and the use cases of `InboxStoreTestsBase` (section 5.5), run on the in-memory stores.
- `AcceptAsync` sets `CreatedAtUtc` and `NextTryAtUtc` to the fake time, and keeps the tenant and the flags of the context. Two calls with the same context give the same inbox id. The same id with two tenants gives two inbox messages.
- `ProcessPendingAsync` runs the pipeline for a claimed `ReceivedMessage<TEvent>` with the tenant services of its tenant, and then calls `CompleteAsync` with the message. An old version is upgraded with the registered function first.
- Three sends with the results transient error, transient error, and success give three `RecordTryAsync` calls with the try numbers 1, 2, and 3, each with the claimed message. The last call has the outcome `Succeeded` and the provider message id of the receipt.
- After a transient error, the next try time in the `RecordTryAsync` call is the exact value of section 4.14. The rows are the tries 1, 2, and 8 of a fixed message id. The delay of try 8 is at most `MaxDelay` multiplied by 1.2. A registration with `BaseDelay` 1 second gives the value with that delay.
- With `AutoAdvanceAmount` set to 1 second, the start and finish times of a try are 1 second apart.
- A transient error on the last allowed try and a permanent error both call `RecordTryAsync` with no next try time.
- A message with an expiry of 5 minutes is sent while the fake time does not move. After the fake time moves 6 minutes, the same message gets an `Expired` try and no send.
- A cancelled token stops the run, and `RecordTryAsync` receives no call. A failed `RecordTryAsync` is repeated one time with the same try. A store that throws gives `Errors.Exception`, and the dispatcher continues with the next item.
- A cancelled token during `ProcessPendingAsync` calls neither `CompleteAsync` nor `FailAsync`, so the `TryCount` of the received message does not change. A later claim gets the message again when its lease ends.
- The dispatcher gives the fake time to `ClaimDueAsync` as `nowUtc`. The inbox processor creates a new DI scope for each received message. The outbox dispatcher creates no DI scope (D130).
- Dispatch loop tests. They wait for an observable call, for example a `TaskCompletionSource` that a substitute completes, and never use `Thread.Sleep`:
  - With `EnablePolling` true and an empty store, the next claim comes only after the fake time moves 1 second. After more empty claims, the delay doubles to a maximum of 30 seconds.
  - With `EnablePolling` false and an empty store, the loop makes no second claim while the fake time moves 1 hour. A signal for its topic then causes a claim.
  - A claim result with a next due time 10 seconds later causes the next claim when the fake time moves 10 seconds, also with `EnablePolling` false (D103).
  - With `EnablePolling` false and one message, a transient error with a next try time 5 seconds later causes the next claim when the fake time moves 5 seconds, with no signal. A `FailAsync` with a next try time does the same in the inbox loop (D103).
  - A signal from `ProcessAsync` causes a claim in the loop of each channel of the stored messages while the fake time does not move. The loop of another channel makes no claim.
  - A claim that fills the free capacity causes the next claim at once. With all workers busy in a send that does not complete, `ClaimDueAsync` gets the free capacity as `maxCount`, and never more.
  - A message whose lease ends within the safety margin is not sent, and no try is recorded for it. The loop claims again when the lease ends, also with `EnablePolling` false.
  - A claim `Error` does not stop the loop. The loop claims again after the poll delay, also with `EnablePolling` false.
  - After cancellation, `RunAsync` returns, and the messages that are still in a work queue are not sent. `RunAsync` and `DispatchPendingAsync` give the same `RecordTryAsync` calls for the same claimed messages.
- The use cases of `DispatchSignalTestsBase`: a notification before a wait completes the wait. Two notifications before one wait complete one wait, and the next wait does not complete. A notification of another topic does not complete the wait. A cancelled token gives `Errors.OperationCancelled`.
- Hosting tests (D101, D105):
  - With transports for two test channels, `RunAsync` claims each channel in its own loop. A send of the first channel that does not complete does not delay the sends of the second channel.
  - With a transport for only the first test channel, the dispatcher claims only the first channel. A second transport for the same channel fails the startup validation (D129).
  - The role `Consumer` with no transport passes the startup validation, and the role `Publisher` with no transport fails it with an `OptionsValidationException`. The role `Publisher` with a message type that has no router passes it, and the role `Consumer` fails it.
  - Two DI containers share one in-memory outbox store instance through the factory overloads. The first has the role `Consumer` and no transport. The second has the role `Publisher`, the test transport, and polling. An event that the first container processes reaches the transport of the second. The two containers share no signal, so this test proves the poll path between processes.
- Startup checks of D135, as rows of one data-driven test. Each row fails the startup validation with an `OptionsValidationException`:
  - A second registration of one message type on the same channel.
  - Two channel registrations of one message type with the same envelope type.
  - Two loader types for one payload type of one message type.
- Two channel registrations of one message type with one payload type and one loader type pass the startup validation. In one DI scope, their composers get the same loader instance (D135).
- Envelope upgrade tests (section 4.13):
  - An outgoing message of an old envelope version reaches the transport as the upgraded envelope with the current version. The store records the try of the stored message, and does not write the upgraded record back.
  - A version that is newer than the code gives a `TransientError` try with `Errors.UnsupportedVersion`, and the transport gets no `SendAsync` call. This try does not count toward the down state.
- Down state tests (D86, D106), with `ErrorThreshold` 3, `CheckInterval` 30 seconds, and two test channels. A substitute outbox dispatch store records the calls:
  - Three transient errors in a row on the first test channel call `SetChannelDownAsync` one time, with the reason `Errors`, the error code of the last try, and a next check time 30 seconds later. Two transient errors and then a success give no call.
  - A permanent error sets the count to 0. A `Suppressed` try and an `Expired` try do not change it.
  - A claim result with a down state of the reason `Errors` makes the loop call `TryStartChannelCheckAsync` when the fake time reaches `NextCheckAtUtc`, and not before. `CheckHealthAsync` gets a call only when `TryStartChannelCheckAsync` returns true. A check with no error calls `SetChannelUpAsync` with the reason `Errors`.
  - A claim result with a down state of the reason `Operator` gives no `TryStartChannelCheckAsync` call while the fake time moves 1 hour. A signal for the topic of the channel then causes a claim at once.
  - After the loop finds its channel up again, one transient error calls `SetChannelDownAsync` again.
  - A message of the down channel with an expiry of 5 minutes stays pending while the fake time moves 2 hours. After the channel is up, the message gets an `Expired` try, and the transport gets no `SendAsync` call for it (D94).
  - A message that waits in the work queue while its channel goes down gets no send and no `RecordTryAsync` call. The down state of the first test channel does not stop the second one.
  - The loop whose `SetChannelDownAsync` call returns true logs at the Error level, and a loop whose call returns false logs nothing. The loop that marks the channel up logs at the Information level.
  - `DispatchPendingAsync` runs a due check of a down channel before its claim.
- Test delivery tests:
  - `AcceptAsync` with `MessageFlags.Test` stores the flag in the inbox, and `ProcessPendingAsync` gives the flag to each outgoing message.
  - A test message with `Suppress` gets a try with the outcome `Suppressed`, and the transport receives no call. With `Redirect`, the transport gets `RedirectTo` as its `redirectTo` argument. With `Deliver`, the transport gets no `redirectTo`.
  - A message with no test flag is sent normally while the test delivery of its channel is `Suppress`.
  - The log records of a test message have `MessageFlags` `Test`. `DeleteTestDataAsync` of the inbox dispatch store deletes only the test messages.
- The options tests of `DispatchOptions`, `ChannelDownOptions`, and `TestDeliveryOptions`, with the rows of section 5.5. They include the rules across properties: a safety margin that is not less than the lease fails, a queue capacity that is less than the parallelism fails, `ErrorThreshold` gets 0, 1, 1000, and 1001, and `Redirect` with no `RedirectTo` fails.
- `DispatchPendingAsync` with a `batchSize` of 0 gives `Errors.ValidationFailed`. A `batchSize` of 1 and of 2 claims at most that number for each channel.
- The standard logging wrapper tests for the transport, inbox store, inbox dispatch store, and dispatcher wrappers.
- Dispatch telemetry (D131 to D133), with an `ActivityListener` and `MetricCollector<T>`:
  - A try gives one `send {Channel}` span of the kind Client, in a new trace, with a link to the `TraceParent` of the message, the try number, and the outcome. A message with no `TraceParent` gives a span with no link.
  - `AcceptAsync` inside an activity of the test stores the id of that activity as `TraceParent`. The inbox processor gives a `process` span with a link to it.
  - `msghatch.publish.tries` counts each try with its outcome. A message that gets the status `Failed` adds 1 to `msghatch.messages.failed`. A successful try 90 seconds after `CreatedAtUtc` on the fake clock records 90 in `msghatch.delivery.delay`.
  - `msghatch.queue.length` gives the messages in the work queue while all workers are busy. `msghatch.channel.down` gives 1 after a claim with a down state, and 0 after the next claim with no down state.

Commits:
1. "Add the transport contract and publish tries"
2. "Add claims, tries, and channel down states to the outbox dispatch store"
3. "Add the inbox stores, the in-memory inbox, and AcceptAsync"
4. "Add the dispatch signal and the dispatch options"
5. "Add the inbox processor and the outbox dispatcher"
6. "Add the shared down state of a channel"
7. "Add test delivery and host roles"
8. "Add dispatch telemetry"
9. "Check the channel registrations and the payload loaders at startup"
10. "Upgrade an old envelope version before the send"

The review of phase 2 added 2 commits before commit 10: the wait of the dispatch loop and the health check of a channel split into small helpers, with tests of their error paths. The default roles (D105) need a transport for the role `Publisher`, so three phase 1 tests of a complete setup now register a test transport.

Done when: the build has no errors and no warnings, all tests pass, and the test with two DI containers sends an event through the test transport.

### Phase 3: email composition

Goal: flow steps 3 to 5 for email. The rendered emails are in the in-memory outbox. No email is sent yet.

1. Add `src/MsgHatch.SourceGeneration` with the generator of section 4.10, and `tests/MsgHatch.SourceGeneration.Tests.Unit`.
2. Add `src/MsgHatch.Email` with `EmailChannel`, `EmailChannelSettings`, `EmailMessage`, and `RenderedEmail` (section 4.4), and `tests/MsgHatch.Email.Tests.Unit`.
3. Add `Engines/EmailTemplateRequest.cs`, `IEmailTemplateEngine.cs`, `IEmailTemplate.cs`, and `EmailTemplateEngineLoggingWrapper<TPayload>`.
4. Add the email composer `EmailComposer<TEvent, TPayload> : IEnvelopeComposer<TEvent, EmailMessage>` (section 4.8, step 4). It calls the payload loader of its payload registration (D136) and the template engine.
5. Add `Templates/TemplateKey.cs`, `TemplateLookup.cs`, `TemplatePartKind.cs`, `TemplatePart.cs`, `TemplateSource.cs`, `ITemplateStore.cs`, the internal `TemplateSelector` with the fallback of D123, and `TemplateStoreLoggingWrapper`.
6. Add the internal `TemplateParser`, `ParsedTemplate`, and `ParsedTemplateCache<TPayload>`.
7. Add `Placeholders/PlaceholderPayloadAttribute.cs`, `IPlaceholderPayload.cs`, and `IPlaceholderWriter.cs`.
8. Add the internal `PooledCharBufferWriter`, the internal `PlaceholderRenderer`, and `PlaceholderEmailEngine<TPayload>`. Add the contract base class `EmailTemplateEngineTestsBase<TEngine>` with `IEmailTemplateTestData` in `tests/MsgHatch.Email.Tests.Unit/Contracts/`, and its subclass `PlaceholderEmailEngineUnitTests`.
9. Add `FileTemplateOptions` with its validator, `FileSystemTemplateStore`, and `EmbeddedResourceTemplateStore`, each with the cache of its sources (section 4.10). Add the contract base class `TemplateStoreTestsBase<TStore>` with `ITemplateStoreTestData`, and its subclasses `FileSystemTemplateStoreUnitTests` and `EmbeddedResourceTemplateStoreUnitTests`.
10. Add `UseTemplateStore`, `UseTenantTemplateStore`, `UseEmbeddedTemplates`, `UseFileTemplates`, and the two `AsEmail` overloads of `PayloadBuilder<TEvent, TPayload>`: the placeholder engine, and another engine from a factory (section 4.7, D136). Add the startup validation rule for the template store. Add `EmailChannelSettingsValidator`, which the first `AsEmail` registers with `AddChannelType`.
11. Add the `MsgHatch.Email` package content: the generator assembly under `analyzers/dotnet/cs`.
12. Add `DependencyInjection/PayloadBuilder.cs` and `MessageBuilder<TEvent>.WithPayload` with its factory overload (D136). The loader of a payload type is registered one time, apart from the channel registrations that use it.

Tests:
- The generated writer writes a `string`, an `int`, a `DateTime` in the culture of the language, and a null nullable property. Writing an `int` and a `DateTime` property allocates 0 bytes.
- A test runs the generator with `CSharpGeneratorDriver` on a payload type that is not `partial`, and gets `MSGHATCH001`.
- The parser rules of section 4.10, as rows of one data-driven test of `PlaceholderEmailEngine.PrepareAsync` with an `ITemplateStore` substitute.
- The fallback of D123, through `PlaceholderEmailEngine.PrepareAsync` from the DI container, with an `ITemplateStore` substitute for the tenant template store and one for the shared template store. The rows cover each of the eight lookups for a tenant key with a scope, the four lookups for `TenantKey.None` with a scope, and the lookups with no scope.
- The use cases of `EmailTemplateEngineTestsBase`: a missing template gives `Errors.NotFound`, an unknown placeholder name gives `Errors.TemplateInvalid` when the template loads, the rendered parts have the payload values in the culture of the request, an `html` part encodes a `<script>` value and the other parts do not, and accented characters, for example `Ödön Űr`, render unchanged.
- The use cases of `TemplateStoreTestsBase`: an existing template, a missing template that gives null, a template with a scope, the fallback to no scope, two lookups of one template that give the same `TemplateSource` instance, and template text with accented characters that round-trips unchanged.
- `FileSystemTemplateStoreUnitTests`, unique to the file store: a changed template file gets a new version, and a new file that was a miss before is found.
- The options tests of `FileTemplateOptions`.
- The email composer calls the payload loader one time with all routes, and calls `PrepareAsync` of the engine of its key one time for each language and scope group. It takes `To` from the route address, and uses the culture of the route language. `RenderAsync` receives the route of each email, with its trace unchanged.
- A route with no address fails with `Errors.ValidationFailed`, and the other routes get their emails. A loader error is the outer error of the composer. A missing template gives `Errors.NotFound` for the messages of that group only.
- `AsEmail` adds the content type `Email:{name}` to the registry, and two `AsEmail` registrations add the email channel one time. With no retry configuration, an email has no `ExpiresAtUtc` (D94).
- `WithPayload` with two channel registrations gives their composers one loader instance in one DI scope, also with a loader factory. Two `WithPayload` calls with two loader types for one payload type fail the startup validation (D135).
- The standard logging wrapper tests for the engine and template store wrappers. A log record never contains an email address or a subject.
- End to end, with `UseInMemoryStores()`, embedded templates, and a test router: `ProcessAsync` stores a rendered email in the in-memory outbox.

Commits:
1. "Add the placeholder payload source generator"
2. "Add the email channel types"
3. "Add the email template engine contract and the email composer"
4. "Add the template stores"
5. "Add the placeholder engine"
6. "Add email registration"
7. "Register payload loaders apart from the channels"
8. "Add the missing tests of the email composition"

The commits differ from the items: the `MsgHatch.Email` project came in commit 1, because the generated writers implement its placeholder types. The template selector and the parser came with the engine in commit 5, and the two email logging wrappers came with the registration in commit 6, because tests reach these internal types only through these items. The review of phase 3 added 3 commits after commit 8: the startup check of D62 for two engines of one payload type, the encoder of the html part that keeps accented letters, and a test of `PooledCharBufferWriter.GetMemory`.

Done when: the build has no errors and no warnings, all tests pass, and the end-to-end test finds a rendered email in the in-memory outbox.

### Phase 4: email delivery with Amazon SES and Resend

Goal: the email publisher sends the emails of the outbox through `IEmailClient`, with Amazon SES as the first client and Resend as the second.

1. Add `IEmailClient` (section 4.6), `EmailClientLoggingWrapper`, `EmailTransport : ITransport`, and `UseEmailClient<TClient>` with its factory overload (D129). With a `redirectTo` value, the email transport replaces `To` with it, and removes `Cc` and `Bcc`. Add the contract base class `TransportTestsBase<TTransport>` with `ITransportTestDriver` in `tests/MsgHatch.Domain.Tests.Unit/Contracts/`, and its first subclass `EmailTransportUnitTests`.
2. Add `src/MsgHatch.Email.AmazonSES` with `AmazonSesOptions` and its validator (section 4.4), `AmazonSimpleEmailServiceClient : IEmailClient`, and `UseAmazonSes` with its overload `UseAmazonSes(string senderEmail)`, which sets the one required option in code (section 4.7). Add `tests/MsgHatch.Email.AmazonSES.Tests.Unit`, the contract base class `EmailClientTestsBase<TClient>` with `IEmailClientTestDriver` in `tests/MsgHatch.Email.Tests.Unit/Contracts/`, and its subclass `AmazonSimpleEmailServiceClientUnitTests`:
   - `UseAmazonSes` registers `IAmazonSimpleEmailServiceV2` with `TryAddSingleton`, as an `AmazonSimpleEmailServiceV2Client` for `AmazonSesOptions.Region`, or for the region of the AWS SDK configuration when `Region` is null.
   - A send calls `SendEmailAsync` one time, and returns the `MessageId` of the response in the receipt. The client writes the outgoing message id into the email tag `msghatch-id`.
   - The client converts each exception into an `Error`, and does not throw. `TooManyRequestsException` and `LimitExceededException` give `Errors.RateLimited`. `SendingPausedException`, `AccountSuspendedException`, `HttpRequestException`, and an HTTP 5xx response give `Errors.Unavailable`. The account-level errors log at the Critical level, because an administrator must act. `MessageRejectedException`, `BadRequestException`, `MailFromDomainNotVerifiedException`, and `NotFoundException` give `Errors.Rejected`.
   - `CheckHealthAsync` calls `GetAccountAsync`. It gives `Errors.Unavailable` when the response has `SendingEnabled` false, because SES then refuses each send, and the channel must stay down (D106). First check the property name in the package version.
3. Add the startup validation rules of these packages.
4. First decide the result of a transport or an upgrade function that throws, and record the decision in the design. From phase 2, the dispatcher logs `Errors.Exception` and records no try (section 4.8). So the message is claimed again after each lease, and never reaches `Failed`. A change of the dispatcher gets its own commit, with its tests.
5. Add `tests/MsgHatch.Email.AmazonSES.Tests.Integration` with `AmazonSimpleEmailServiceClientIntegTests` (section 5.5, D124). The SES account comes from the section `SesIntegration` of `appsettings.json`, which holds no secret, and of `appsettings.Local.json`, which git ignores. The SES client of the tests uses only these credentials, and each test is skipped while its settings are not complete. The default recipient is the SES mailbox simulator `success@simulator.amazonses.com`.
6. Add `src/MsgHatch.Email.Resend` with `ResendOptions` and its validator (section 4.4), `ResendEmailClient : IEmailClient` on the REST API of Resend through `IHttpClientFactory` (D138), and `UseResend` (section 4.7). Add `tests/MsgHatch.Email.Resend.Tests.Unit` with the subclass `ResendEmailClientUnitTests` of `EmailClientTestsBase`, and a fake HTTP handler as the Resend API:
   - A send posts one JSON request to `/emails` with `from`, `to`, `subject`, `html`, `text`, `cc`, `bcc`, and the tag `msghatch-id`, and returns the `id` of the response in the receipt. It sends the API key as a Bearer token, a `User-Agent` header, which Resend requires, and the outgoing message id as the `Idempotency-Key`.
   - The client converts each error response and each exception into an `Error`, and does not throw. 429 gives `Errors.RateLimited`. A 5xx response, an `HttpRequestException`, and 409 `concurrent_idempotent_requests` give `Errors.Unavailable`. An error of the API key gives `Errors.Unavailable` and logs at the Critical level, because an administrator must act. Each other 4xx response gives `Errors.Rejected`. The error message names only the HTTP status and the error name of Resend.
   - `CheckHealthAsync` calls `GET /domains`. A 2xx response and 401 `restricted_api_key` give null, 429 gives `Errors.RateLimited`, and each other answer gives `Errors.Unavailable`.
7. Add `tests/MsgHatch.Email.Resend.Tests.Integration` with `ResendEmailClientIntegTests` (section 5.5, D124). The API key comes from the section `ResendIntegration` of `appsettings.Local.json`, which git ignores. `appsettings.json` holds the default sender `onboarding@resend.dev` and the default recipient `delivered@resend.dev`. Each test is skipped while the API key is empty.

Tests:
- The use cases of `TransportTestsBase`, run by `EmailTransportUnitTests` with an `IEmailClient` substitute as the driver: a success gives the receipt of the provider, a transient error and a permanent error come back as values, and a cancelled token gives `Errors.OperationCancelled`.
- `EmailTransport` gives the `EmailMessage` of `OutgoingMessage.Content` and the outgoing message id to `IEmailClient`. With a `redirectTo` value, it replaces `To` and removes `Cc` and `Bcc`.
- The use cases of `EmailClientTestsBase`, run by `AmazonSimpleEmailServiceClientUnitTests` with an `IAmazonSimpleEmailServiceV2` substitute as the driver.
- Tests unique to SES: one data-driven row for each SES exception class checks the `ErrorType` code. `CheckHealthAsync` gives null for `SendingEnabled` true, and `Errors.Unavailable` for `SendingEnabled` false and for an `HttpRequestException`. The sender address rule, with and without a sender name, and with an address that already contains `<` and `>`.
- The options tests of `AmazonSesOptions`, with the rows of section 5.5. `SenderEmail` gets null, empty, only whitespace, a value with no `@`, and a value with an accented character. `SenderName` gets the lengths 99, 100, and 101. `Region` gets null, `"eu-west-1"`, and `"EU-WEST-1"`.
- `UseAmazonSes` alone registers all parts: `IEmailClient` resolves as its logging wrapper around `AmazonSimpleEmailServiceClient`, and `IAmazonSimpleEmailServiceV2` resolves. An `IAmazonSimpleEmailServiceV2` that the test registered first stays.
- The standard logging wrapper tests for the email client wrapper. A log record never contains an email address or a subject.
- A publisher host with only `UseAmazonSes` sends only the email channel. The other messages of the same outbox stay pending (D101).
- `UseAmazonSes(string senderEmail)` sends from that address, and the value wins over the section `MsgHatch:AmazonSes`.
- The SES integration tests, against a real SES account: an email with accented letters gives the message id of SES, a sender name with accented letters (RFC 2047) gives the message id of SES, a sender address with an accented local part gives `Errors.Rejected`, and `CheckHealthAsync` gives null. A run with no account reports each test as skipped. Phase 20 runs these tests against a real SES account.
- The use cases of `EmailClientTestsBase`, run by `ResendEmailClientUnitTests` with the fake HTTP handler as the driver.
- Tests unique to Resend: the JSON of the request, with and without the html body and the copies. The three headers. One data-driven row for each HTTP status and error name, and for each exception. The Critical log of an API key error, with no address in it. The rows of the health check. The sender address rule.
- The options tests of `ResendOptions`. `ApiKey` gets null, empty, only whitespace, and a value. `SenderEmail` gets null, empty, only whitespace, a value with no `@`, a value, and a value with `<` and `>`. `SenderName` gets the lengths 99, 100, and 101.
- `UseResend` alone registers all parts, the options bind from the section `MsgHatch:Resend`, a value from code wins, and a missing `ApiKey` or `SenderEmail` stops the start.
- The Resend integration tests, against a real Resend account: an email with accented letters and a sender name with accented letters give the id of Resend, a second send with the same message id gives the id of the first send, a wrong API key gives `Errors.Unavailable` for a send and for the health check, and `CheckHealthAsync` gives null. A run with no API key reports each test as skipped.
- The email channel from the event to the email client, in `MsgHatch.Email.Tests.Unit`: one host with both roles, the in-memory stores, the test templates, an email registration, and an `IEmailClient` substitute. After `ProcessAsync` of an event, `DispatchPendingAsync` sends each rendered email through the email client (section 4.8, steps 1 to 7).

Commits:
1. "Add the email transport and the email client contract"
2. "Add the Amazon SES email client"
3. "Add UseAmazonSes with the sender address as a string"
4. "Add the Amazon SES integration tests"
5. "Add the Resend email client"
6. "Add the Resend integration tests"
7. "Test the email channel from the event to the email client"

Done when: the build has no errors and no warnings, all tests pass, and the Resend integration tests pass with a Resend account in `appsettings.Local.json`. With no SES account, the SES integration tests are skipped. Phase 20 runs them against a real account.

### Phase 5: SignalR channel

Goal: real-time UI updates, composed by the event consumer and sent by the SignalR publisher.

1. Add `src/MsgHatch.SignalR` with `SignalRChannel`, `SignalRChannelSettings`, `SignalRChannelSettingsValidator`, `ISignalREnvelope`, `SignalREnvelope<TPayload>` (D135), the SignalR composer, `SignalRTransport<THub> : ITransport where THub : Hub`, `UseSignalR<THub>`, and `PayloadBuilder<TEvent, TPayload>.AsSignalR` (D136). `UseSignalR` and the first `AsSignalR` register the channel with `AddChannelType`, so that a publisher host with no message type binds `MsgHatch:ChannelDown:SignalR` and `MsgHatch:TestDelivery:SignalR`. The settings validator accepts each `SignalRChannelSettings` value, because the subscription manager checks the language (section 4.17), and rejects the settings of another channel. Add `tests/MsgHatch.SignalR.Tests.Unit` with `SignalRTransportUnitTests`, a subclass of `TransportTestsBase`.
2. The composer calls the payload loader of its payload registration, and puts each payload into a `SignalREnvelope<TPayload>` (D17, D135).
3. The transport sends `ISignalREnvelope.Payload` of `OutgoingMessage.Content` with the message type name as the method name:
   - For a user target, to `Clients.User(SignalRUsers.ForUser(tenant, user))`, with the tenant of the outgoing message. The text is the user id with no tenant, and `{TenantId}:{UserId}` with a tenant. The host's `IUserIdProvider` must give the same text for each connection (D114).
   - For a company target, to `Clients.Group(SignalRGroups.ForCompany(tenant, company))`. The group name is `msghatch:company:{CompanyId}` with no tenant, and `msghatch:{TenantId}:company:{CompanyId}` with a tenant. The host's hub adds each connection to the group of the company of its user, for example in `OnConnectedAsync`.
   - An id can contain `:`, so in each id of a user name or a group name, `%` becomes `%25` and `:` becomes `%3A`. Without this rule, the tenant `a:b` with the user `c` and the tenant `a` with the user `b:c` get the same user name `a:b:c`. Then one tenant gets the notifications of the other tenant. An id with no `:` and no `%` keeps the text of the two bullets above.
   - The transport returns a receipt with no provider id. SignalR has no delivery guarantee, so "sent" means that SignalR accepted the message.
   - The transport converts each exception into an `Error`, and does not throw. A cancelled token gives `Errors.OperationCancelled`. `JsonException` and `NotSupportedException`, which a hub protocol throws for a payload that it cannot serialize, give `Errors.Rejected`. Each other exception gives `Errors.Unavailable`, because a backplane or Azure SignalR throws its own exception types for an outage, for example `RedisConnectionException`. The error message names only the exception type. Content that is not an `ISignalREnvelope`, and a target with an invalid id or kind, give `Errors.ValidationFailed`.
4. With a `redirectTo` value, the transport sends to the SignalR group `redirectTo` in place of the target.

Tests:
- The use cases of `TransportTestsBase`, run by `SignalRTransportUnitTests`.
- The transport calls `Clients.User` and `Clients.Group` with the names of item 3, with and without a tenant, each with the method name, on an `IHubContext<THub>` substitute.
- `SignalRUsers` and `SignalRGroups` give the names of item 3, as rows of one data-driven test each, also for ids with `:` and `%`.
- One data-driven row for each exception class checks the `ErrorType` code. Content of another type and an invalid target give `Errors.ValidationFailed`, and the transport sends nothing. `CheckHealthAsync` returns null.
- The SignalR composer calls the payload loader one time with all routes. It gives one envelope with the payload of each route, in the order of the routes. A loader error and a payload count that is not the route count are the outer error. A null payload fails only its route.
- The settings validator accepts each `SignalRChannelSettings` value, also an unknown language, and rejects the settings of another channel.
- `AsSignalR` adds the content type `SignalR:{name}` to the registry. With no retry configuration, a SignalR message gets 3 tries and an `ExpiresAtUtc` 5 minutes after the fake time (D94).
- With a `redirectTo` value, the transport sends to that group.
- `UseSignalR<THub>` alone registers one `ITransport` for the SignalR channel, for each payload type, and `IHubContext<THub>`, and passes the startup validation. A publisher host with only `UseSignalR` applies the section `MsgHatch:TestDelivery:SignalR`.
- Golden file: a version 1 sample of an outgoing SignalR message deserializes with the content type of a SignalR registration, and gives the envelope with its payload (section 4.13).

Commits:
1. "Add the SignalR channel"
2. "Add the golden file of the SignalR envelope"

Done when: the build has no errors and no warnings, and all tests pass.

### Phase 6: broker channel with Wolverine

Goal: broker messages, composed by the event consumer and sent by the broker publisher through Wolverine (D134). A message type can send its domain event unchanged, as its own payload (D139).

1. Add `src/MsgHatch.Broker` with `BrokerChannel`, `BrokerChannelSettings`, `BrokerChannelSettingsValidator`, `IBrokerEnvelope`, `BrokerEnvelope<TPayload>`, the broker composer, `BrokerPublish`, `IBrokerClient`, `BrokerClientLoggingWrapper`, `BrokerTransport : ITransport`, `UseBrokerClient<TClient>` with its factory overload, and `PayloadBuilder<TEvent, TPayload>.AsBroker` (D136). `UseBrokerClient` and the first `AsBroker` register the channel with `AddChannelType`, so that a publisher host with no message type binds `MsgHatch:ChannelDown:Broker` and `MsgHatch:TestDelivery:Broker`. The settings validator rejects a header with an empty name or a name of only whitespace, and a header name that starts with `msghatch-` with no difference of case, because the library uses these names. Add `tests/MsgHatch.Broker.Tests.Unit` with `BrokerTransportUnitTests`, a subclass of `TransportTestsBase`, and the contract base class `BrokerClientTestsBase<TClient>` with `IBrokerClientTestDriver` in its `Contracts/` folder.
2. The composer calls the payload loader of its payload registration. The broker transport builds the headers: the headers of the channel settings, `msghatch-id` with the outgoing message id, `msghatch-tenant` for a message of a tenant, `msghatch-test: true` for a test message, and `msghatch-redirect` with the `redirectTo` value. Then it calls `IBrokerClient.PublishAsync` (section 4.6). So each client only maps these values to its library, and a receiver can find duplicates by `msghatch-id`.
3. Add `src/MsgHatch.Broker.Wolverine` with `WolverineBrokerClient : IBrokerClient` and `UseWolverineBroker` (section 4.7). Add `tests/MsgHatch.Broker.Wolverine.Tests.Unit` with `WolverineBrokerClientUnitTests`, a subclass of `BrokerClientTestsBase`:
   - The client calls `IMessageBus.SendAsync` with the payload and a `DeliveryOptions` with the headers and the tenant id. It returns a receipt with no provider id. A payload type with no Wolverine route gives the permanent `Errors.NotFound`, because the host has no publish rule and no handler for the type. `PublishAsync` drops such a message with no error, and MsgHatch then records `Succeeded`.
   - First check that `PublishAsync` with a payload of the static type `object` routes by the run-time type of the payload, in the pinned version. If it does not, the client calls the generic method through one cached delegate for each payload type. Result for 6.61.0: `PublishAsync` and `SendAsync` route by the run-time type, so the client calls `SendAsync<object>`.
   - First check which headers Wolverine uses for the trace context, and add the result to the checked facts of section 4.21. Result for 6.61.0: `parent-id`.
   - Wolverine registers `IMessageBus` as a scoped service, and the client is a singleton. So `UseWolverineBroker` gives the client a new `MessageBus` on the `IWolverineRuntime` of the host for each publish. A token that is cancelled before the publish publishes nothing.
   - The client converts each exception into an `Error`, and does not throw. An exception that a retry cannot fix gives the permanent `Errors.Rejected`: `SendRejectedException`, `MessageTooLargeException`, `WolverineSerializationException`, `JsonException`, and `NotSupportedException`. So the outbox does not retry them. `CheckHealthAsync` returns null, because Wolverine has no general health check of a transport.
   - A receipt means that the broker accepted the message only with the Wolverine setup of D140: `SendInline()` on the route, `Durability.UseSyncRetryBlock = true`, and the publisher confirms of the transport. The XML documentation of `UseWolverineBroker` gives this setup.
4. Add the startup validation rule of `UseWolverineBroker` (section 4.7). It also fails when `Durability.UseSyncRetryBlock` of Wolverine is false (D140).
5. Add `MessageBuilder<TEvent>.WithEventPayload` and the internal `EventPayloadLoader<TEvent> : IPayloadLoader<TEvent, TEvent>` to `src/MsgHatch.Domain` (D139). `WithEventPayload` registers the loader with `UsePayloadLoader`. The loader returns the event of the received message for each route, as one instance.
6. Add `tests/MsgHatch.Broker.Wolverine.Tests.Integration` with `WolverineBrokerClientIntegTests`, against a RabbitMQ broker with the setup of D140. The broker URI comes from `appsettings.Local.json`, which git ignores, and each test is skipped with no URI.
7. Add `IExceptionClassifier<TClient>`, the extension method `ExceptionClassifiers.Classify`, and `MsgHatchBuilder.AddExceptionClassifier<TClient, TClassifier>` to `src/MsgHatch.Domain` (D141). The SES client, the Resend client, the Wolverine client, and the SignalR transport get their classifiers through an optional constructor parameter, and ask them after the cancellation check and before their built-in mapping.

Checked facts (2026-10-31) behind D134. The phase reads each source again for the pinned version:

| Fact | Source |
|---|---|
| Wolverine has the MIT license. The package is `WolverineFx`. | [Wolverine comparison](https://medium.com/@babayevqocheli/wolverine-for-net-microservices-the-best-masstransit-alternative-571f9ef08fa2), [NuGet](https://nuget.org/packages/WolverineFx/1.10.0) |
| Wolverine has transports for RabbitMQ, Azure Service Bus, Kafka, MQTT, NATS, Pulsar, Redis, Amazon SQS and SNS, and GCP Pub/Sub. | [JasperFx](https://jasperfx.net/news/one-application-many-brokers-wolverine-multi-transport-multi-tenancy) |
| `IMessageBus.PublishAsync` takes `DeliveryOptions` with headers and a `TenantId` for one message. | [Message bus](https://github.com/jasperfx/wolverine/blob/main/docs/guide/messaging/message-bus.md) |
| Wolverine has local in-process queues, and by default it routes a message type to a local queue when the application has a handler for it. | [Local queues](https://github.com/jasperfx/wolverine/blob/main/docs/guide/messaging/transports/local.md) |
| MassTransit 9 and later need a commercial license. MassTransit 8 stays under Apache-2.0, with support until 2026-12-31. | [Massient](https://massient.com/) |
| Rebus has the MIT license. | [Rebus on NuGet](https://www-0.nuget.org/packages/Rebus.AwsSnsAndSqs/6.0.8/License) |
| `AWSSDK.SimpleNotificationService` has the Apache-2.0 license. | [NuGet license](https://www.nuget.org/packages/AWSSDK.SimpleNotificationService/4.0.2.19/license) |
| SNS sends at most 10 message attributes to an SQS subscription with raw message delivery, and discards a message with more there. The attributes count to the message size, which is 256 KiB by default. | [SNS message attributes](https://docs.aws.amazon.com/sns/latest/dg/sns-message-attributes.html), [Publishing an SNS message](https://docs.aws.amazon.com/sns/latest/dg/sns-publishing.html) |
| `Azure.Messaging.ServiceBus` has the MIT license. | [NuGet](https://www.nuget.org/packages/Azure.Messaging.ServiceBus) |
| `RabbitMQ.Client` has the dual license Apache-2.0 or MPL-2.0. The license rule of section 5.5 allows it through Apache-2.0. | [Third-party licenses of NServiceBus](https://docs.particular.net/platform/third-party-licenses) |
| `Confluent.Kafka` has the Apache-2.0 license. Not read on its NuGet page: its phase checks the page. | [Package scan](https://secure.software/nuget/packages/Confluent.Kafka/dependencies/2.10.1), [Confluent .NET client](https://docs.confluent.io/clients-confluent-kafka-dotnet/current/overview.html) |
| SlimMessageBus has the Apache-2.0 license, and is a light message bus interface with transport plugins. | [SlimMessageBus](https://github.com/zarusz/SlimMessageBus) |
| Brighter (`Paramore.Brighter`) has the MIT license, and stores the messages for its external bus in its own outbox. | [NuGet](https://www.nuget.org/packages/paramore.brighter/), [Brighter outbox](https://github.com/BrighterCommand/Docs/blob/master/contents/9/BrighterOutboxSupport.md) |
| Checked 2026-11-03: `WolverineFx` 6.61.0 has the MIT license. Its dependencies `JasperFx`, `JasperFx.Events`, `JasperFx.SourceGenerator`, and `Cronos` have MIT, and `NewId` 4.0.1 has Apache-2.0. | The nuspec of each package on nuget.org |
| Checked 2026-11-03 for 6.61.0: `PublishAsync<T>` and `SendAsync<T>` route by `message.GetType()`, also for a payload of the static type `object`. A run with a local handler confirmed it for each method. With no route for the payload type, `PublishAsync` sends nothing, and returns with no error. `SendAsync` throws `IndeterminateRoutesException` for such a type, and a run confirmed it. With a route, `SendAsync` sends the same envelopes as `PublishAsync`, because `MessageRouter.RouteForSend` calls `RouteForPublish`. The run with `SendAsync` gave the headers and the tenant id of `DeliveryOptions` to the handler. | [MessageBus.cs](https://github.com/jasperfx/wolverine/blob/29806dd8a8180d165878a6dcb8f096abd782dc84/src/Wolverine/Runtime/MessageBus.cs), [MessageRouter.cs](https://github.com/jasperfx/wolverine/blob/29806dd8a8180d165878a6dcb8f096abd782dc84/src/Wolverine/Runtime/Routing/MessageRouter.cs), [EmptyMessageRouter.cs](https://github.com/jasperfx/wolverine/blob/29806dd8a8180d165878a6dcb8f096abd782dc84/src/Wolverine/Runtime/Routing/EmptyMessageRouter.cs) |
| Checked 2026-11-03 for 6.61.0: Wolverine registers `IMessageBus` as a scoped service. Its documentation tells a singleton to build `new MessageBus(runtime)` from `IWolverineRuntime`. | [HostBuilderExtensions.cs](https://github.com/jasperfx/wolverine/blob/29806dd8a8180d165878a6dcb8f096abd782dc84/src/Wolverine/HostBuilderExtensions.cs), [Message bus](https://github.com/jasperfx/wolverine/blob/29806dd8a8180d165878a6dcb8f096abd782dc84/docs/guide/messaging/message-bus.md) |
| Checked 2026-11-03 for 6.61.0, with a transport whose sender fails: the default endpoint mode is `BufferedInMemory`, and a buffered send returns before the transport sends. An inline send with the default retry block returns with no exception after the first failed send. Wolverine then tries 3 more times in the background, and drops the message. With `Durability.UseSyncRetryBlock = true`, an inline send waits, and throws after 4 failed sends. A `SendRejectedException` of the transport throws at once in inline mode. | [Endpoint.cs](https://github.com/jasperfx/wolverine/blob/29806dd8a8180d165878a6dcb8f096abd782dc84/src/Wolverine/Configuration/Endpoint.cs), [BufferedSendingAgent.cs](https://github.com/jasperfx/wolverine/blob/29806dd8a8180d165878a6dcb8f096abd782dc84/src/Wolverine/Transports/Sending/BufferedSendingAgent.cs), [InlineSendingAgent.cs](https://github.com/jasperfx/wolverine/blob/29806dd8a8180d165878a6dcb8f096abd782dc84/src/Wolverine/Transports/Sending/InlineSendingAgent.cs), [DurabilitySettings.cs](https://github.com/jasperfx/wolverine/blob/29806dd8a8180d165878a6dcb8f096abd782dc84/src/Wolverine/DurabilitySettings.cs) |
| Checked 2026-11-03 for `WolverineFx.RabbitMQ` 6.61.0, with a RabbitMQ 4.3 broker: the publisher confirms are off by default. With `PublisherConfirmationsEnabled` and `PublisherConfirmationTrackingEnabled`, `BasicPublishAsync` returns only after the confirm of the broker. A publish to an exchange that does not exist then throws. Wolverine sets `mandatory` only in the mode `ServerlessWithOutbox`. So the broker confirms a message to a queue that does not exist, and drops it. | [WolverineRabbitMqChannelOptions.cs](https://github.com/jasperfx/wolverine/blob/29806dd8a8180d165878a6dcb8f096abd782dc84/src/Transports/RabbitMQ/Wolverine.RabbitMQ/WolverineRabbitMqChannelOptions.cs), [ConnectionMonitor.cs](https://github.com/jasperfx/wolverine/blob/29806dd8a8180d165878a6dcb8f096abd782dc84/src/Transports/RabbitMQ/Wolverine.RabbitMQ/Internal/ConnectionMonitor.cs), [RabbitMqSender.cs](https://github.com/jasperfx/wolverine/blob/29806dd8a8180d165878a6dcb8f096abd782dc84/src/Transports/RabbitMQ/Wolverine.RabbitMQ/Internal/RabbitMqSender.cs) |
| Checked 2026-11-03 for 6.61.0: in inline mode, a transport that refuses a message throws `SendRejectedException`, and Wolverine does not retry it. `MessageTooLargeException` states that a retry cannot change the size of the message. The `SqsMessageTooLargeException` of the SQS package does not derive from it. `Envelope` wraps each failure of the serializer in `WolverineSerializationException`. `QueueDoesNotExistException` is only logged by the buffered agent, and never thrown to a caller. With `TelemetryEnabled`, which is the default of an endpoint, the inline agent ignores a `NotSupportedException` of the sender, so the send returns as a success. | [InlineSendingAgent.cs](https://github.com/jasperfx/wolverine/blob/29806dd8a8180d165878a6dcb8f096abd782dc84/src/Wolverine/Transports/Sending/InlineSendingAgent.cs), [MessageTooLargeException.cs](https://github.com/jasperfx/wolverine/blob/29806dd8a8180d165878a6dcb8f096abd782dc84/src/Wolverine/Transports/MessageTooLargeException.cs), [Envelope.cs](https://github.com/jasperfx/wolverine/blob/29806dd8a8180d165878a6dcb8f096abd782dc84/src/Wolverine/Envelope.cs), [SendingAgent.cs](https://github.com/jasperfx/wolverine/blob/29806dd8a8180d165878a6dcb8f096abd782dc84/src/Wolverine/Transports/Sending/SendingAgent.cs), [Endpoint.cs](https://github.com/jasperfx/wolverine/blob/29806dd8a8180d165878a6dcb8f096abd782dc84/src/Wolverine/Configuration/Endpoint.cs) |
| Checked 2026-11-03: `WolverineFx.RabbitMQ` 6.61.0 has the MIT license. Its dependency `RabbitMQ.Client` 7.2.2 has Apache-2.0 OR MPL-2.0. | The nuspec of each package on nuget.org |
| Checked 2026-11-03 for 6.61.0: a Wolverine host in `TypeLoadMode.Dynamic`, the default, does not start without `WolverineFx.RuntimeCompilation`. A host with no handler starts in `TypeLoadMode.Static` with no runtime compiler. | [WolverineRuntime.HostService.cs](https://github.com/jasperfx/wolverine/blob/29806dd8a8180d165878a6dcb8f096abd782dc84/src/Wolverine/Runtime/WolverineRuntime.HostService.cs) |
| CAP is an event bus with its own outbox. Its project has the MIT license. The license page of the core package `DotNetCore.CAP` was not read. | [CAP project](https://awesome.ecosyste.ms/projects/github.com%2Fdotnetcore%2Fcap) |

Tests:
- The use cases of `TransportTestsBase`, run by `BrokerTransportUnitTests` with an `IBrokerClient` substitute as the driver.
- The broker transport gives the headers of item 2 to the client: with and without a tenant, for a test message, and with a `redirectTo` value. A header of the library wins over a header of the settings with the same name. Two payload types go through the same transport.
- The broker composer calls the payload loader one time with all routes. It gives one envelope with the payload and the settings headers of each route, in the order of the routes. A loader error and a payload count that is not the route count are the outer error. A null payload fails only its route.
- With an `ActivityListener`, the client calls `SendAsync` while the `send` span is `Activity.Current` (section 4.21).
- The use cases of `BrokerClientTestsBase`, run by `WolverineBrokerClientUnitTests` with an `IMessageBus` substitute as the driver: the payload with its run-time type, the headers, the tenant id, and an exception that becomes an `Error`.
- Tests unique to Wolverine: one data-driven row for each exception class checks the `ErrorType` code. A payload type with no Wolverine route gives `Errors.NotFound`. A cancelled token publishes nothing, and a token that is cancelled during the publish gives `Errors.OperationCancelled`. `CheckHealthAsync` returns null.
- `AsBroker` adds the content type `Broker:{name}` to the registry. With no retry configuration, a broker message has no `ExpiresAtUtc` (D94).
- The settings validator rejects an empty header name, a name of only whitespace, and the names `msghatch-x` and `MsgHatch-X`, and accepts `x-source`.
- The standard logging wrapper tests for the broker client wrapper.
- `UseBrokerClient<TClient>` alone registers the client and one `ITransport` for the broker channel. A publisher host with only `UseBrokerClient` applies the section `MsgHatch:TestDelivery:Broker`.
- `UseWolverineBroker` alone registers the `IBrokerClient` and one `ITransport` for the broker channel. It publishes through a `MessageBus` on the `IWolverineRuntime` of the host. With no `IWolverineRuntime`, the startup validation fails. With `Durability.UseSyncRetryBlock` false, it fails too (D140).
- A real Wolverine runtime with a fake transport in memory: the payload goes through the route of its run-time type. The envelope has the headers, the tenant id, and the id of `Activity.Current` as `ParentId`. A payload type with no route gives `Errors.NotFound`. A transport that refuses the message gives `Errors.Rejected`. A `NotSupportedException` of the transport gives a receipt for a lost message, because Wolverine ignores it.
- One data-driven row for each send mode of Wolverine records the result when the broker does not accept the message. Inline with the sync retry block gives an error, and gives a receipt after a later try that succeeds. Buffered, and inline with the default retry block, give a receipt for a lost message.
- The RabbitMQ integration tests, against a real broker with the setup of D140. A route to a queue gives a receipt, and the queue holds the message with its `msghatch-id` header. A route to an exchange that does not exist gives `Errors.Unavailable`. A route to a queue that does not exist gives a receipt for a message that the broker drops. A run with no broker URI reports each test as skipped.
- The exception classifiers (D141). `Classify` gives the first result that is not null, in the order of the classifiers, and null with no classifier. `AddExceptionClassifier` registers two classifiers of one client in their order, registers the same classifier type one time, and does not give the classifier of one client to another client.
- For each of the SES client, the Resend client, the Wolverine client, and the SignalR transport: a classifier result wins over the built-in mapping, a cancelled token gives `Errors.OperationCancelled` also with a classifier, and the setup method of the package gives the registered classifiers to the client.
- With `WithEventPayload`, the resolved `IPayloadLoader<TEvent, TEvent>` returns the event of the received message for each route, as one instance. Its composers get one loader instance in one DI scope.
- `WithEventPayload` and a `WithPayload` call with an own loader for the event type fail the startup validation (D135).
- A broker message of `WithEventPayload` carries the domain event as its payload, from the event to the broker client.

Commits:
1. "Add the broker channel and the broker client contract"
2. "Add the Wolverine broker client"
3. "Add the event as its own payload"

Done when: the build has no errors and no warnings, and all tests pass.

### Phase 7: JSON file stores

Goal: durable inbox and outbox stores with no database (D26).

1. Add `src/MsgHatch.Data.FileSystem` with `FileStoreOptions` and its validator, `FileSystemInboxStore` and `FileSystemOutboxStore` with their two parts (section 4.5), and `UseFileStores`. Add `tests/MsgHatch.Data.FileSystem.Tests.Unit` with the subclasses `FileSystemInboxStoreUnitTests` and `FileSystemOutboxStoreUnitTests`. The two stores share one internal folder helper:
   - A received message is the file `{status}/{nextTryAtUtc:yyyyMMddHHmmssfff}_{sequence:D10}_{id}.json`, written with `MsgHatchJson`. A received message of a tenant has the name of its tenant after its id in the file name, so two tenants can give the same id. The name is the tenant id escaped with `Uri.EscapeDataString`, with each capital letter A to Z as `^` and the small letter, because Windows and macOS ignore the case of a name, but a tenant id is case-sensitive. An outgoing message is the file `{channel}/{status}/{nextTryAtUtc:yyyyMMddHHmmssfff}_{sequence:D10}_{id}.json`, so each channel has its own folder (D104). The status folder is `pending`, `claimed`, `done`, or `failed`. The time prefix lets a claim sort the files by name, without a read of each file. The tenant is in the JSON of the message.
   - The sequence counts the files that one store instance adds with the same time prefix, and starts at 0 for each new time prefix (owner decision). So a sort by name keeps the insert order of the messages with the same due time, which the contract tests expect. A file keeps its sequence when its time prefix changes. Two processes on one folder can give the same sequence, and the id then sets the order.
   - The time prefix rounds the due time up to the next millisecond, so a claim never takes a file before its `NextTryAtUtc`.
   - `AddAsync` writes the file into a temporary file first. Then it creates the marker file `ids/{tenantFolder}/{id}` with `FileMode.CreateNew`, and a rename gives the file its name. If the marker exists, the store deletes the temporary file and ignores the message. The marker is under `{RootPath}/inbox`, `{RootPath}/outbox`, or their test folders, and `{tenantFolder}` is `no-tenant` or `tenants/{tenant name}`. The store has the comment `ponytail: a crash between the marker and the rename loses the message, use a database store when a crash must not lose one`.
   - `ClaimDueAsync` moves each due file from `pending` to `claimed` with `File.Move`, with the lease end as its new time prefix, and writes the lease end into the file as `NextTryAtUtc` and `LeaseUntilUtc` (D103). The first dispatcher that moves a file wins, and the others skip that file. A file in `claimed` whose time prefix is at or before now has an ended lease, so the claim takes it again in the same way. `NextDueAtUtc` is the earliest time prefix in `pending` and in `claimed`, so it includes the lease ends.
   - The claims of one store instance take turns with a lock. On Windows, `File.Move` opens the source and renames through that handle. A claimer that opened the file just before the rename of the winner then renames it to the same name, and both claimers get the file (checked fact below). Between processes on Windows this stays possible, and the claim has the comment `ponytail: Windows renames through a handle, so two processes can rarely claim one file in the same lease, use a database store for exact claims`.
   - A claimed file that the code cannot read stays out of the result. A transient error, for example a newer version (`Errors.UnsupportedVersion`), keeps it in `claimed`, so a newer instance reads it after the lease (section 4.13). A permanent error, for example invalid JSON (`Errors.SerializationFailed`), moves it unchanged to `failed`. A file name with no valid time prefix is skipped.
   - A file system failure gives `Errors.Unavailable`, and a `down.json` with invalid JSON gives `Errors.SerializationFailed`. So the stores return an error, and do not throw.
   - The down state of a channel is the file `{channel}/down.json` under `{RootPath}/outbox`, with the `ChannelDown` in camelCase, the channel as its name, and the reason as a string. `SetChannelDownAsync` with the reason `Errors` creates it with `FileMode.CreateNew`, so the first loop wins. The other changes of the down state open the file with `FileShare.None` while they read and write it. `SetChannelUpAsync` deletes the file after it closes it. `TryStartChannelCheckAsync` returns false when it cannot open the file, because another caller holds it.
   - Each store has the property `RootPath`, so that a test or a second store instance can find the folder.
   - `RecordTryAsync` reads the file, and changes nothing when its `TryCount` is not the try number minus 1. Else it adds the try to the `tries` array and writes the new delivery state into a temporary file. Then it replaces the old file with `File.Move`, in the folder of the new status.
   - The tenant store of a key reads the tenant from the JSON of each message, and skips the messages of the other tenants.
   - The stores have the comment `ponytail: rename-based claims suit one machine or a shared folder with atomic rename, use a database store for more throughput`.
2. Make the inbox upgrade check the version before the CLR type, as the outbox upgrade does (D25). From phase 1, the inbox checks the CLR type first, so a newer version of the current type counts as current.
3. First decide the expiry of a received message, and record the decision in the design. `DeliveryState.ExpiresAtUtc` and the retry options `Inbox` have an expiry, but the inbox processor does not read it (section 4.8). Result (D142): the inbox has no expiry of its own. The expiry of an outgoing message counts from the `CreatedAtUtc` of its received message, and the startup validation rejects `MsgHatch:Retry:Inbox:Expiry`.
4. Give `UseInboxStore`, `UseInboxDispatchStore`, `UseOutboxStore`, and `UseOutboxDispatchStore` with a factory an optional `setupMethod`, and give `nameof(UseFileStores)` from `UseFileStores` (owner decision). Before, a second selection of a store named `UseInboxStore` and the other generic methods, because a storage package calls them, and the host code has no such call (D62).

Tests:
- The use cases of `InboxStoreTestsBase` and `OutboxStoreTestsBase` (section 5.5) run on the two file stores, each in its own temporary folder.
- The options tests of `FileStoreOptions`. Configuration binding: a value in the section `MsgHatch:FileStores` reaches `FileStoreOptions`, and a value from the `Action<FileStoreOptions>` of the setup method wins over it.
- One call of `UseFileStores` registers `IInboxStore`, `IOutboxStore`, `IInboxDispatchStore`, and `IOutboxDispatchStore`, each resolved as its logging wrapper around the store of the package. The tenant store of a key and the dispatch store use one store instance.
- Tests unique to the file stores: a permanent error moves the file to the `failed` folder of its channel, and a second store instance on the same folder reads the data of the first one, also the down state.
- More tests unique to the file stores: a test message goes to the test folder. Twenty messages with the same due time are claimed in the order of their add. A file with invalid JSON goes to `failed` in the inbox and in the outbox, and the claim takes the other message. A newer version stays in `claimed`, and its lease end is the next due time. A file whose claimed name exists already is skipped. A message of an unregistered type gives `Errors.SerializationFailed`. A root folder that is a file gives `Errors.Unavailable`. A `down.json` with invalid JSON gives `Errors.SerializationFailed`.
- With no logging, the dispatch stores are the one instance of each file store. With no `RootPath`, the validation of `FileStoreOptions` fails on `RootPath`.
- A second selection of the outbox tenant store names `UseOutboxStore` with no `setupMethod`, and the given name with one. `UseFileStores` after `UseInMemoryStores` names both methods for each of the four store interfaces.
- New use cases of the store base classes (section 5.5), so each store gets them: a finished message is not claimed again after its lease, a message with a next try time is claimed only from that time, two tenant ids that differ only in case stay apart, and a tenant id with special characters goes through an add, a claim, and a completion unchanged.
- More tests unique to the file stores, from a review of the test gaps: a due time 0.5 ms after now is not claimed, and the next due time is the next millisecond. A duplicate add leaves no temporary file. A file name with no valid time prefix stays in `pending`. Twenty messages that fail in the other order with one next try time are claimed in the order of their add. A test message of the outbox goes to `test/outbox`. A newer version in the outbox stays in `claimed`. A `down.json` that another caller holds open gives `Errors.Unavailable` for a claim, and false for `TryStartChannelCheckAsync`.
- A received message with a newer version of the current event type gives `Errors.UnsupportedVersion`, and the pipeline does not run. An old version whose type is the type of the current version runs its upgrade function (D25).
- An event that waited one hour in the inbox, with an expiry of 5 minutes, gives an outgoing message that expires 5 minutes after the accept time (D142). A value in `MsgHatch:Retry:Inbox:Expiry` fails the validation of the inbox retry options.

Checked facts (2026-11-03) for the file stores:

| Fact | Source |
|---|---|
| On Windows, with no lock, two parallel claims of one store instance did not get different messages. `MoveFileEx` opens the source and renames through that handle, so the rename of the loser can follow the file to its new name. In 5 of 5 runs, the in-place write of the lease of the winner failed with `UnauthorizedAccessException` while that handle was open, and the claim gave `Errors.Unavailable`. With a retry of that write, the rename of the loser succeeded as a rename to the same name, and both claims returned the same 5 of 10 messages in 5 of 5 runs. | Runs of `ClaimDueAsync_TwoParallelClaims_GetDifferentMessages` with no lock, and a probe that logged each failed write |

Commits:
1. "Add JSON file stores for the inbox and the outbox"
2. "Check the version first in the inbox upgrade"
3. "Count the time in the inbox against the outgoing expiry"
4. "Name the setup method of a storage package in the selection check"

Done when: the build has no errors and no warnings, and all tests pass.

### Phase 8: SQLite stores

Goal: relational inbox and outbox stores in process (D55).

1. Add `src/MsgHatch.Data.Sqlite` with `SqliteStoreOptions` and its validator, `SqliteInboxStore` and `SqliteOutboxStore` with their two parts, the internal schema helper that runs one time for each connection string, and `UseSqliteStores` (section 4.5). Add `tests/MsgHatch.Data.Sqlite.Tests.Unit` with the subclasses `SqliteInboxStoreUnitTests` and `SqliteOutboxStoreUnitTests`.
   - A store writes the whole message with `MsgHatchJson`, and `json_extract` puts the event or the envelope, the route trace, and its `$type` into the columns `Content`, `Trace`, and `TraceType`. The claim returns each row as one `json_object` document, which `MsgHatchJson` reads (owner decision, D143).
   - Each message table has the counter `Sequence INTEGER PRIMARY KEY`, and its old key is a `UNIQUE` constraint (owner decision, D144). The SQLite documentation says that `VACUUM` can change the `rowid` of a table with no `INTEGER PRIMARY KEY`.
   - The claim reads the `Sequence` of the first due rows in the order `NextTryAtUtc, Sequence`, and then leases these rows by `Sequence` (D144). It uses `Sequence`, because two tenants can store one inbox id, and an `Id IN (...)` also leases the row of the other tenant. SQLite gives the rows of `RETURNING` in an arbitrary order, so the store returns them in the order of the first read.
   - A claimed row that the code cannot read stays out of the result. A transient error, for example a newer version, keeps it pending with the lease end as its next try time (section 4.13). A permanent error, for example invalid JSON, sets its status to `Failed`, ends its lease, and writes `{ErrorName}: {message}` into `LastError`.
   - The tables have no `NOT NULL` constraint, because `INSERT OR IGNORE` also skips a row that breaks one, with no error.
   - A SQLite failure gives `Errors.Unavailable`, and a down row with an invalid value gives `Errors.SerializationFailed`. So the stores return an error, and do not throw.
   - `RecordTryAsync` reads whether the id is stored only when its update changes no row, so a message that is not stored gives `Errors.NotFound`.
   - Each store takes the connection string in its constructor, checks it with `SqliteConnectionStringBuilder`, and has the property `ConnectionString`, so that a test or a second store instance can find the database. The stores use the synchronous API of `Microsoft.Data.Sqlite`, as the file stores do, and do not create the folder of the database file.
   - The indexes of the delivery management (section 4.19) come with phase 15, which adds the members that use them.
2. First check the busy wait of the pinned `Microsoft.Data.Sqlite` version: a command on a locked database tries again until its command timeout ends (section 4.5). Result for 10.0.14: the command tries again until its command timeout ends (checked fact below).

Tests:
- The use cases of `InboxStoreTestsBase` and `OutboxStoreTestsBase` run on the two SQLite stores, each test on its own database file in the temporary folder `msghatch-tests`, with `Default Timeout=5` in its connection string (section 5.5).
- A new use case of `InboxStoreTestsBase` and `OutboxStoreTestsBase` runs on each store: of two due messages, the first added is due later, and the claim takes and returns them in the order of their due time (D144). A review of this phase found that the first SQLite claim returned them in the order of their add.
- The options tests of `SqliteStoreOptions`. A connection string with `{tenant}` fails, because one database for each tenant has its own setup method (D125).
- One call of `UseSqliteStores` registers `IInboxStore`, `IOutboxStore`, `IInboxDispatchStore`, and `IOutboxDispatchStore`, each resolved as its logging wrapper around the store of the package. The tenant store of a key and the dispatch store use one store instance.
- `SqliteOutboxStoreUnitTests` and `SqliteInboxStoreUnitTests`, unique to SQLite:
  - The stores write times as ISO 8601 text and enum values as text, and an empty text for no tenant. The same message id with two tenants gives two inbox rows. The rows of a batch get their `Sequence` numbers in the order of the batch. The down row of a channel has its reason and its times as text.
  - A second store instance on the same database file reads the data of the first one. The connection string comes from the test configuration, bound to `SqliteStoreOptions`.
  - A claim with nothing due takes no write lock: it completes while a second connection holds a write transaction (section 4.5).
  - `RecordTryAsync` with a try number that is already recorded changes no row, and the tries stay one JSON array (D27).
  - A second writer completes after the first writer commits, within the command timeout.
  - A row with invalid JSON in the inbox and in the outbox gets the status `Failed`, and the claim takes the other message. A row with a newer version stays pending, and its lease end is the next due time. A down row with an unknown reason gives `Errors.SerializationFailed`. A database in a folder that does not exist gives `Errors.Unavailable`.
  - The first operation sets the WAL journal and the schema version 1. A claim of `maxCount` rows gives now as the next due time. Twenty messages with the same due time are claimed in the order of their add. A message of an unregistered type gives `Errors.SerializationFailed`, and the outbox adds nothing of its batch.

Checked facts (2026-11-04) for the SQLite stores:

| Fact | Source |
|---|---|
| `Microsoft.Data.Sqlite` 10.0.14, with SQLite 3.53.3, tries a command on a locked database again until the command timeout ends. A new connection has `PRAGMA busy_timeout` 0, so the wait comes from the library. With `Default Timeout=3`, an `INSERT` and a `BeginTransaction(deferred: false)` against a `BEGIN IMMEDIATE` that another connection held failed with `SQLITE_BUSY` (5) after 3.1 seconds. With `CommandTimeout` 1, an `INSERT` failed after 1.1 seconds. When the first writer committed after 1 second, the second `INSERT` and the second `BEGIN IMMEDIATE` completed after 1.1 seconds. In WAL mode, a `SELECT` completed at once while another connection held `BEGIN IMMEDIATE`. Each of 3 runs gave these results. | A probe program with two connections on one database file |
| `Microsoft.Data.Sqlite` 10.0.14 and `Microsoft.Data.Sqlite.Core` 10.0.14 have the MIT license. Their dependencies `SQLitePCLRaw.bundle_e_sqlite3`, `SQLitePCLRaw.core`, `SQLitePCLRaw.lib.e_sqlite3`, and `SQLitePCLRaw.provider.e_sqlite3` 2.1.13 have Apache-2.0. | The nuspec of each package |
| In SQLite 3.53.3, `EXPLAIN QUERY PLAN` of the claim query `... WHERE Status = 'Pending' AND NextTryAtUtc <= ? ORDER BY NextTryAtUtc, Sequence LIMIT ?` gives `SEARCH ... USING COVERING INDEX IX_MsgHatchInbox_Due`, with no sort step, and the same for the outbox index with `Channel = ?`. With `CreatedAtUtc` in the `ORDER BY`, the plan adds `USE TEMP B-TREE`. The SQLite documentation says that `VACUUM` can change the `rowid` of a table with no `INTEGER PRIMARY KEY` (D144). | A probe program on the schema of the stores, and `sqlite.org/lang_vacuum.html` and `sqlite.org/rowidtable.html` |

Commits:
1. "Add SQLite stores for the inbox and the outbox"

Done when: the build has no errors and no warnings, and all tests pass.

### Phase 9: subscription router

Goal: the router with subscriptions on three owner levels inside each tenant, with an in-memory subscription store and the management API (D97, D116). The package uses only the public API of the core.

1. Add `src/MsgHatch.Subscriptions` and `tests/MsgHatch.Subscriptions.Tests.Unit`, with the subscription types of section 4.4: `SubscriptionLevel`, `SubscriptionOwner`, `SubscriptionKind`, `RecipientKind`, `SubscriptionRecipients`, `Subscription`, `SubscriptionDraft`, `NotificationScope`, the trace types `TargetSource`, `LanguageSource`, `AddressSource`, `SkipReason`, `SubscriptionSource`, `SkippedSubscription`, `ResolutionTrace`, `OptOutDecision`, and `SubscriptionResolution`, then `UserProfile`, `CompanyProfile`, `SubscriptionQuery`, `MessageTypeQuery`, and `MessageTypeState`.
2. Add `ISubscriptionStore` and `IRecipientDirectory` (section 4.6) as tenant services, with `SubscriptionStoreLoggingWrapper`, `RecipientDirectoryLoggingWrapper`, and the `Log` class of the subscriptions area.
3. Add `SubscriptionResolver`, the public resolution of the routing procedure of section 4.17, step 4. It is public because `ISubscriptionManager` uses it for the applied subscriptions, and because a consumer can use it to preview them.
4. Add `SubscriptionRouter<TEvent>` with the routing procedure of section 4.17, the internal `SubscriptionMessage<TEvent>`, `WithScope`, `UseSubscriptions`, `UseSubscriptionStore`, and `UseRecipientDirectory`, each with its factory overload. `UseSubscriptions` registers `ResolutionTrace` with `AddTraceType` as the trace type `Trace:Subscription` (D98). Add the startup validation rules of the package.
5. Add `InMemorySubscriptionStore` and `UseInMemorySubscriptionStore`. Add the contract base class `SubscriptionStoreTestsBase<TStore>` with `ISubscriptionStoreTestData` in `tests/MsgHatch.Subscriptions.Tests.Unit/Contracts/` (section 5.5), and its subclass `InMemorySubscriptionStoreUnitTests`.
6. Add `ISubscriptionManager`, the internal `SubscriptionManager` (section 4.17), and `SubscriptionManagerLoggingWrapper`. `UseSubscriptions` registers them as tenant services.
7. First give the channel settings a readable language. The resolver takes the language of the channel settings (section 4.17), but `ChannelSettings` had no language member, and `MsgHatch.Subscriptions` does not reference the channel packages. Result (D145): the core interface `ILanguageSettings`, which `EmailChannelSettings` and `SignalRChannelSettings` implement. The owner also decided the static form of `SubscriptionResolver.Resolve` (D146), and an internal `SubscriptionRouter<TEvent>` (D147).

Tests:
- `EmailChannelSettings` and `SignalRChannelSettings` give their language through `ILanguageSettings` (D145).
- `SubscriptionResolver`: one data-driven test with one row for each resolution example R1 to R18 of `docs/subscription-domain.md`. The router tests below run R19. Each row asserts the whole list of effective subscriptions. The rows cover the merge for each channel, the mandatory subscription, the fixed subscriptions for the company and for named users, one fixed delivery for each event, the merge of duplicates, and the skip of a named user of another company.
- `SubscriptionResolver` traces: one data-driven test with one row for each trace example T1 to T8 of the domain document. Each row asserts the whole `ResolutionTrace` or `OptOutDecision`.
- `SubscriptionOwner`, `Subscription`, `SubscriptionRecipients`, and `NotificationScope` keep the rules of section 4.4, with the rows of the examples V1 to V5 of the domain document.
- Subscription router tests. They resolve `INotificationService` from a container built with `AddMsgHatch()`, `UseSubscriptions()`, `UseInMemoryStores()`, and the factory overloads, with substitutes for `ISubscriptionStore` and `IRecipientDirectory`. They use the email, SignalR, and broker channel registrations of phases 3, 5, and 6, with embedded test templates:
  - `ProcessAsync` with a user scope and no subscription for named users calls `IRecipientDirectory.GetUsersAsync` one time, and `ISubscriptionStore.FindForDeliveryAsync` one time with the message type, the companies of the users, and the users. Both calls go to the services of the key of the context tenant.
  - An event of the tenant `t2` calls only the store and the directory of `TenantKey.For(t2)`, also when another tenant has a user with the same id (example R19).
  - `ProcessAsync` with a company scope calls `GetCompanyUsersAsync` only when a channel of the message type has no group delivery.
  - `ProcessAsync` calls `GetCompanyAsync` for a company of a user scope only when a subscription for the company applies: for B with user 1, and not for A with user 1. It calls `GetUsersAsync` a second time, with only the named user 2, for D with user 1.
  - The pipeline examples P1 to P7 of `docs/subscription-domain.md`: each example asserts the whole list of outgoing messages in the in-memory outbox, with their targets, channels, languages, and resolution traces.
  - An `Error` from `FindForDeliveryAsync` or from the directory fails the event.
  - A message type with the subscription router and no `WithScope` fails the startup validation with an `OptionsValidationException`. A message type with its own router and no `WithScope` passes it.
- The use cases of `SubscriptionStoreTestsBase`, run by `InMemorySubscriptionStoreUnitTests`, with the store examples S1 to S13 of `docs/subscription-domain.md`.
- Subscription management tests, with the examples M1 to M22 of `docs/subscription-domain.md`, on the in-memory store and a substitute for `IRecipientDirectory`:
  - `GetChannelsAsync` lists each registered channel with its features.
  - `GetMessageTypesAsync` gives the applied subscriptions with their levels for a user, for a company, and with no filter, and only the message types with a scope function. A user subscription that a fixed subscription hides is not in the result. An opt-out that applies is in the result.
  - Each validation rule of section 4.17 gives `Errors.ValidationFailed`, and the store does not change. A named user of another company is one of these rules, with the company from the directory substitute.
  - Each conflict rule of section 4.17 gives `Errors.Conflict`. A draft that is invalid and also conflicts gives `Errors.ValidationFailed`.
  - A valid `CreateAsync` gives revision 1, a `Guid` version 7 id, and the fake time as `CreatedAtUtc` and `LastModifiedAtUtc`. A valid `UpdateAsync` gives the next revision.
- The standard logging wrapper tests for `SubscriptionStoreLoggingWrapper`, `RecipientDirectoryLoggingWrapper`, and `SubscriptionManagerLoggingWrapper`. No log record contains an email address of a test profile.

Commits:
1. "Give the channel settings a readable language"
2. "Add the subscription model and the recipient directory contract"
3. "Add subscription resolution with merge rules"
4. "Add the subscription router and its setup"
5. "Add the in-memory subscription store and its contract test base class"
6. "Add the subscription management API"

Done when: the build has no errors and no warnings, all tests pass, and the examples P1 to P7 run end to end on the in-memory stores.

### Phase 10: subscription stores

Goal: durable subscription stores in files and in SQLite.

1. Add `src/MsgHatch.Subscriptions.Data.FileSystem` with `FileSystemSubscriptionStore` on the `FileStoreOptions` of `MsgHatch.Data.FileSystem` (section 4.5, D59), and `UseFileSubscriptionStore`. Add `tests/MsgHatch.Subscriptions.Data.FileSystem.Tests.Unit` with `FileSystemSubscriptionStoreUnitTests`.
2. Add `src/MsgHatch.Subscriptions.Data.Sqlite` with `SqliteSubscriptionStore` on the `SqliteStoreOptions` of `MsgHatch.Data.Sqlite` (section 4.5, D59), and `UseSqliteSubscriptionStore`. Add `tests/MsgHatch.Subscriptions.Data.Sqlite.Tests.Unit` with `SqliteSubscriptionStoreUnitTests`.
3. First decide how the subscription data packages share code, and record the decisions in the design. The plan did not settle four choices. Result: `InternalsVisibleTo` from each core data package to its subscription data package (D148), the public `ChannelSettingsJson` in `MsgHatch.Subscriptions` (D149), the core method `ConfigureSharedOptions` for the check of the shared options (D150), and the class fixture `AcmeAndGlobexScenario` for the read group of the store tests (D151).

Tests:
- The use cases of `SubscriptionStoreTestsBase` run on the file store and on the SQLite store, with the store examples S1 to S13. The shared test scenario of section 4 of `docs/subscription-domain.md` is seeded one time as a named scenario, for a group of tests that only read.
- `UseSqliteSubscriptionStore()` with no argument uses the `SqliteStoreOptions` that `UseSqliteStores` sets. A connection string in both `UseSqliteStores` and `UseSqliteSubscriptionStore` fails the startup validation, and the message names both methods (D62).
- `FileSystemSubscriptionStoreUnitTests`: a subscription for the user `"auth0|abc"` is stored under an escaped folder name, and is found again.
- `SqliteSubscriptionStoreUnitTests`:
  - A second default subscription with the same message type, channel, and recipient kind gives `Errors.Conflict`, although the default level has no company and no user.
  - The store and `SqliteOutboxStore` share one database file, and each keeps its own schema version.
- `ChannelSettingsJson` (D149): the settings of each scenario channel go through `Serialize` and `Deserialize` unchanged, with the content type `Settings:{Channel}` and version 1. One data-driven test has a row for each failure: an unregistered channel, settings of another type, a newer version, an unknown content type, a content type that is not a settings type, invalid JSON, and the JSON `null`.
- `ConfigureSharedOptions` (D150): two setup methods with a value in code for one options class fail the startup validation, and the message names both. One setup method passes. A value from code wins over a configuration section that a later setup method binds.
- `FileSystemSubscriptionStoreUnitTests`: a change while another caller holds the lock of the owner folder gives `Errors.Unavailable`.
- More tests, from breaks of the code that showed gaps:
  - `SubscriptionStoreTestsBase`: a mandatory default subscription (#3), an opt-out for all channels (#4), and the whole state after an update with new settings and a new `LastModifiedAtUtc` come back unchanged. `GetAsync` of an unknown or a deleted id gives no error.
  - Both stores: settings of an unregistered channel give `Errors.SerializationFailed`, and the store keeps nothing.
  - `FileSystemSubscriptionStoreUnitTests`: an update with another owner moves the file to the folder of that owner. A file that a person broke gives the error of its row: invalid JSON, the JSON `null`, a missing property, an owner that does not match its level, and an invalid message type name give `Errors.SerializationFailed`. A newer schema version or settings version gives `Errors.UnsupportedVersion`, also for `FindForDeliveryAsync`.
  - `SqliteSubscriptionStoreUnitTests`: a second opt-out for all channels of one owner gives `Errors.Conflict`. The store writes the fields as text, with an empty text for no tenant and no user. A row that a person broke gives the error of its row, and a database in a folder that does not exist gives `Errors.Unavailable`.
  - The setup methods of both stores: a value from code, a value in the configuration section, and no argument after the core setup method each reach the store, and the store resolves as its logging wrapper.

Checked facts (2026-11-04) for the SQLite subscription store:

| Fact | Source |
|---|---|
| With `Microsoft.Data.Sqlite` 10.0.14, `EXPLAIN QUERY PLAN` of the query of `FindForDeliveryAsync` on the seeded scenario gave `SEARCH MsgHatchSubscription USING INDEX IX_MsgHatchSubscription_Delivery (TenantId=? AND MessageType=?)`. After `DROP INDEX IX_MsgHatchSubscription_Delivery`, it gave the same search with `UX_MsgHatchSubscription_Owner`, the unique index of `(TenantId, MessageType, Level, CompanyId, UserId, Channel, Recipients)`. So the second index of the plan adds only write cost, and the table has no second index (section 4.5). | A probe test on the schema of the store, after `ANALYZE` |

Commits:
1. "Record the subscription store decisions in the plan (D148 to D151)"
2. "Configure the shared options of two setup methods in one place"
3. "Add the JSON of the channel settings of a subscription"
4. "Seed the store scenario one time for the read tests"
5. "Add the JSON file subscription store"
6. "Serve the subscription deliveries from the unique index in the plan"
7. "Test the mandatory flag, the opt-out, and the change time in the store contract"
8. "Add the SQLite subscription store"
9. "Test the settings that a subscription store cannot write"
10. "Record the extra tests of phase 10 in the plan"

Done when: the build has no errors and no warnings, and all tests pass.

### Phase 11: caching

Goal: the subscription cache, with HybridCache and FusionCache as backends (D38, D39).

1. Add `ISubscriptionCache`, `ISubscriptionCacheBackend`, `SubscriptionCacheOptions` with `SubscriptionCacheOptionsValidator`, `SubscriptionStoreCachingWrapper`, and `UseSubscriptionCache<TBackend>` with its factory overload to `MsgHatch.Subscriptions` (section 4.15). `UseSubscriptionCache` registers the caching wrapper and `ISubscriptionCache` as tenant services. Add the contract base class `SubscriptionCacheBackendTestsBase<TBackend>` with `ISubscriptionCacheBackendTestData` in `tests/MsgHatch.Subscriptions.Tests.Unit/Contracts/`.
2. Add `src/MsgHatch.Subscriptions.Caching.Hybrid` with `HybridCacheSubscriptionBackend`, its entry type `HybridCacheSubscriptionEntry` (D152), and `UseHybridSubscriptionCache` (sections 4.7 and 4.15). First check the names of the `HybridCacheEntryFlags` values in the package version. Add `tests/MsgHatch.Subscriptions.Caching.Hybrid.Tests.Unit` with `HybridCacheSubscriptionBackendUnitTests`.
3. Add `src/MsgHatch.Subscriptions.Caching.FusionCache` with `FusionCacheSubscriptionBackend` and `UseFusionSubscriptionCache`. The backend gets its cache from `IFusionCacheProvider.GetCache("MsgHatch.Subscriptions")`. Add `tests/MsgHatch.Subscriptions.Caching.FusionCache.Tests.Unit` with `FusionCacheSubscriptionBackendUnitTests`.

Tests:
- Caching wrapper tests, with substitutes for `ISubscriptionStore` and `ISubscriptionCacheBackend`:
  - With a miss for all owners, the wrapper calls the inner `FindForDeliveryAsync` one time with all companies and users, and calls `SetAsync` for the defaults, each company, and each user. An owner with no subscription gets an empty list.
  - With a hit for some owners, the wrapper calls the inner store with only the missing companies and users, and returns all subscriptions whole. With a hit for all owners, the inner store receives no call.
  - The wrappers of two keys give different cache keys and tags for the same company id (D114). A tenant id, a company id, and a user id with `:` are percent-encoded in each key and tag (D153).
  - With the defaults in the cache and a miss for one user, the inner store returns the default subscriptions too. The wrapper ignores these rows, and calls `SetAsync` only for the user.
  - `SetAsync` receives the configured expiration, the tenant tag, and the owner tag. After an `Error` from the inner store, `SetAsync` receives no call.
  - A successful `AddAsync`, `UpdateAsync`, or `DeleteAsync` removes the owner tag of that subscription. A failed one removes nothing.
  - A backend error from `TryGetAsync` counts as a miss. A backend error from `SetAsync` or `RemoveByTagAsync` does not fail the call.
  - `InvalidateAsync` removes the owner tag. `InvalidateAllAsync` removes the tenant tag of its key, and the entries of another tenant stay.
- Wiring: with the cache on, `ISubscriptionStore` resolves to the logging wrapper around the caching wrapper around the implementation.
- The options tests of `SubscriptionCacheOptions`. `KeyPrefix` gets a value with a space and a value with an accented character. Configuration binding: a value in the section `MsgHatch:SubscriptionCache` reaches the options, and a value from the `Action` of the setup method wins over it.
- The use cases of `SubscriptionCacheBackendTestsBase`, run by both backend subclasses, each on a real in-memory cache: a set value with two subscriptions is read back whole, a miss gives null and stores no entry, and a removed tag removes the values of that tag while the values of another tag stay.
- `UseHybridSubscriptionCache` and `UseFusionSubscriptionCache` alone register all parts. `UseFusionSubscriptionCache` does not change the default FusionCache of the host.
- Extra tests that the implementation added:
  - The caching wrapper: `QueryAsync` and `GetAsync` use no cache. A read error before `DeleteAsync` stops the delete, and an unknown id removes no tag. It logs the hits and misses of each call at the Debug level, and each backend error at the Warning level with the tenant.
  - The setup: both orders of `UseSubscriptionStore` and `UseSubscriptionCache` give the same wrapper order. With `EnableLogging` false, the cache stays on. The type overload gives one singleton backend. An invalid `KeyPrefix` fails the startup validation. A second cache selection fails it too, and the message names both methods (D62).
  - The backend contract: an empty list comes back as an empty list, not null. An entry with two tags goes with each of them. An entry with an expiration of 1 minute is in the cache 59 seconds later, and not 61 seconds later. Each test data moves the fake clock of the `MemoryCache` of its library. The FusionCache test data starts that clock at the system time, because FusionCache calculates the end of an entry with the system clock.
  - Each backend: an exception of its cache library gives `Errors.Unavailable`, and a cancellation gives `Errors.OperationCancelled`. `UseHybridSubscriptionCache` keeps a `HybridCache` of the host, and gives its second callback to HybridCache. The second callback of `UseFusionSubscriptionCache` changes only the named FusionCache.

Checked facts (2026-11-04 and 2026-11-05) for the cache packages, with a probe program on `Microsoft.Extensions.Caching.Hybrid` 10.11.0 and `ZiggyCreatures.FusionCache` 2.9.0:

| Fact | Source |
|---|---|
| `HybridCacheEntryFlags` has the values `None`, `DisableLocalCacheRead`, `DisableLocalCacheWrite`, `DisableLocalCache`, `DisableDistributedCacheRead`, `DisableDistributedCacheWrite`, `DisableDistributedCache`, `DisableUnderlyingData`, and `DisableCompression`. | `Enum.GetNames` in the probe |
| `GetOrCreateAsync` with a factory that returns null and the flags `DisableLocalCacheWrite` and `DisableDistributedCacheWrite` stores nothing: a second call runs its factory again. | Probe |
| HybridCache with no distributed cache serializes a value of `IReadOnlyList<Subscription>` or `Subscription[]` in memory. The read fails with `NotSupportedException`, because `ChannelSettings` is abstract. So the plan premise that only a distributed level needs a serializer is false (D152). | Probe |
| A sealed type with `[ImmutableObject(true)]` comes back from HybridCache as the same instance, with no serializer. | Probe |
| HybridCache with only `Expiration` also ends the local entry at that time. | Probe, an entry of 1 second read after 1.5 seconds |
| HybridCache uses the `TimeProvider` of the DI container for the tags. With a `FakeTimeProvider` that does not move, a value set again after `RemoveByTagAsync` stays invalid. A test that sets a value again after a tag removal moves the fake clock. | Probe, with the system clock and with a `FakeTimeProvider` |
| `AddHybridCache` keeps a `HybridCache` that the host registered before it. | Probe |
| HybridCache does not cache a key with the control character U+001F or U+001E. FusionCache caches it. | Probe |
| `AddFusionCache("MsgHatch.Subscriptions")` alone registers no default `IFusionCache`. With the default cache of the host, the two caches do not share entries. FusionCache gives the same instance back with no serializer, and shows a value that is set again after `RemoveByTagAsync`. | Probe |
| Neither library ends an entry by the `TimeProvider` of the DI container: with a `FakeTimeProvider` moved 2 minutes forward, an entry of 1 minute is still in each cache. `FusionCacheOptions` has no clock option. | Probe (2026-11-12) |
| Both libraries keep a local entry in a `MemoryCache`, and `MemoryCacheOptions.Clock` (`ISystemClock`, not obsolete) ends it. HybridCache uses the `IMemoryCache` of DI, and a fake clock at 2030-01-01 works. FusionCache takes its own `MemoryCache` through `WithMemoryCache`, but it calculates the end time with the system clock, so the fake clock must start at the system time. | Probe (2026-11-12): an entry of 1 minute is there after 59 seconds and gone after 61 seconds |

Commits:
1. "Keep the HybridCache entries without a serializer in the plan (D152)"
2. "Percent-encode the ids in the subscription cache keys in the plan (D153)"
3. "Add the subscription caching wrapper and the cache backend contract"
4. "Add the HybridCache subscription cache backend"
5. "Add the FusionCache subscription cache backend"
6. "Record the extra tests of phase 11 in the plan"

Done when: the build has no errors and no warnings, and all tests pass.

### Phase 12: subscription HTTP endpoints

Goal: the HTTP endpoints of the subscription management API (D54).

1. Add `src/MsgHatch.Subscriptions.AspNetCore` with `MapMsgHatchSubscriptions`, `SubscriptionEndpointOptions`, the request and response types, and their `JsonSerializerContext` (section 4.17). It maps each route of section 4.17 except the verification route, which phase 14 adds. Add `tests/MsgHatch.Subscriptions.AspNetCore.Tests.Unit`.

Tests, on a `TestServer` with an `ISubscriptionManager` substitute:
- Each route of section 4.17, except the verification route, calls the matching manager method, with the query filters `messageType`, `companyId`, `userId`, and `level`.
- The query parameter `tenantId` selects the manager of `TenantKey.For(tenantId)`, and a route with no `tenantId` uses the manager of `TenantKey.None`.
- `POST` gives 201 with a `Location` header. `PUT` with an old revision gives 409.
- `Errors.ValidationFailed` gives 400, `Errors.NotFound` gives 404, and `Errors.Conflict` gives 409, each with a `ProblemDetails`.
- The `settings` of an email draft deserialize as `EmailChannelSettings`, and an unknown channel gives 400.
- The `/me` routes take the user id from the signed-in user and the tenant from `CurrentTenant`, and a subscription of another owner gives 404 there.
- More tests, from the decisions D154 to D156 and from breaks of the code that showed gaps:
  - Each route with `tenantId` calls only the manager of that tenant, and each route with no `tenantId` only the manager of `TenantKey.None`. An invalid `tenantId`, `messageType`, `companyId`, `userId`, or `level` gives 400 and calls no manager, also an empty value, a level as a number, and a level in lower case.
  - The response JSON of a channel, a message type state, and a subscription, compared whole. A `PUT` gives the id, the revision, and the draft to the manager. A `DELETE` gives 204, and a manager error gives its problem.
  - A body that is not JSON gives 415 (D156). One data-driven test has a row for each invalid body: invalid JSON, the JSON `null`, an empty body, a missing or invalid message type, kind, or level, an undefined enum number, owner ids that do not match the level, an invalid id or channel name, an unknown channel with settings, settings with no channel, invalid settings JSON, users for recipients other than named users, and an empty named user. A `PUT` with no revision gives 400. Each row calls no manager.
  - The draft rows: the email and SignalR settings, a mandatory company subscription for named users, a company subscription for the company, a default opt-out with the scope users, and an opt-out with the settings `null`, with and without a channel.
  - Settings of a type that the content type registry does not know throw `InvalidOperationException`.
  - The `/me` routes (D154): no `CurrentUser` maps no `/me` route, and `CurrentTenant` with no `CurrentUser` throws `ArgumentException`. A signed-in user with no user id gives 403 on each route and calls no manager. With no `CurrentTenant`, the routes use `TenantKey.None`. The owner fields of a body and the owner filters of a query are ignored. An unknown id and another owner give the same 404 body for a read, an update, and a delete. A manager error other than `Errors.NotFound` stays.

Checked facts (2026-11-12) for the endpoints, with `Microsoft.AspNetCore.TestHost` 10.0.14 and the shared framework `Microsoft.AspNetCore.App` 10:

| Fact | Source |
|---|---|
| `Microsoft.AspNetCore.TestHost` 10.0.14 has the license expression MIT, no package dependency, and only the framework reference `Microsoft.AspNetCore.App`. | The `.nuspec` of the pinned version on nuget.org |
| `MapGet` and `MapPost` with a method that has only an `HttpContext` parameter select the `RequestDelegate` overload, which drops the returned `IResult`. The analyzer rule ASP0016 stops the build. A cast to `Delegate` selects the route handler overload. | Build |
| The string enum converter writes the flags value `Languages \| RichText` of `ChannelFeatures` as `"Languages, RichText"` (D155). | Test of `GET {prefix}/channels` |
| A keyed registration with the exact key `TenantKey.None` or `TenantKey.For(t2)` wins over the `KeyedService.AnyKey` registration of `UseSubscriptions`. So a test replaces the manager of one key. | Tests of the tenant selection |
| A query parameter with an empty value, for example `?companyId=`, binds to a `string?` parameter as an empty text, not as null. | A break that read an empty text as null made 3 rows fail |
| `System.Text.Json` reads the JSON `null` of a `JsonElement?` property as null, not as an element of the kind `Null`. So the endpoints need no check of the kind. | A break that removed that check made no test fail, also the row with `"settings": null` and no channel |
| `RespectNullableAnnotations` changes no result of the request type, because a `null` id or name gives the same 400 through the check of that value. So `SubscriptionEndpointJson` does not set it. | A break that removed the option made no test fail |
| `ReadFromJsonAsync` with the JSON type information of a context with `RespectRequiredConstructorParameters` throws `JsonException` for a missing required property, invalid JSON, and an empty body. | Rows of the invalid body test |
| A `TestServer` gives an exception of an endpoint to the caller of `HttpClient.SendAsync`. | Test of settings of an unregistered type |

Commits:
1. "Record the decisions of the subscription endpoints in the plan (D154 to D156)"
2. "Name the content type registry as a need of the subscription endpoints in the plan"
3. "Add HTTP endpoints for subscription management"
4. "Record the checked facts and the extra tests of phase 12 in the plan"

Done when: the build has no errors and no warnings, and all tests pass.

### Phase 13: example, measurements, and documentation

Goal: the subscription example end to end, the allocation budget, and the store guide.

1. Add `examples/ExampleApp`, a web app:
   - Domain event records and payload records for the message types A to E.
   - The message type F, a password reset email with its own router `PasswordResetRouter` (section 4.6), to show a router with no subscriptions.
   - An example recipient directory on SQLite with `Microsoft.Data.Sqlite`. The table `Users` holds the company, the UI language, and the email address of each user, and the table `Companies` holds the default language and the email address of each company. The ids are `int` values, mapped with `UserId.From` and `CompanyId.From` (D73).
   - The inbox and the outbox with `UseSqliteStores`, and the subscriptions with `UseSqliteSubscriptionStore`, on one database file.
   - The subscription cache on FusionCache in memory.
   - JSON samples of the example event and payload types, with a golden-file test for each sample (section 4.13).
   - Embedded email templates in English and Hungarian.
   - A log email client that writes each email to the log, a SignalR hub, and the subscription endpoints with `MapMsgHatchSubscriptions`, including the `/me` routes. A caller signs in with a JWT bearer token from `dotnet user-jwts` (D158).
   - The broker channel through `UseWolverineBroker`, on a local queue of Wolverine, with a Wolverine handler that writes each broker message to the log. So the example needs no broker server. From Wolverine 6.0, `WolverineFx` has no runtime compiler, so the example references `WolverineFx.RuntimeCompilation`, which compiles the code of the handler at startup in the default `TypeLoadMode.Dynamic`.
   - OpenTelemetry with `AddSource(MsgHatchTelemetry.SourceName)` and `AddMeter(MsgHatchTelemetry.MeterName)`, and the OTLP exporter (D131).
   - `MsgHatchWorker : BackgroundService` starts `RunAsync` for the inbox and the outbox in one process, with the in-process dispatch signal and `EnablePolling` false (D101, D102).
   - At startup, the app writes the users and the companies of the shared test scenario (section 4 of `docs/subscription-domain.md`) into the example directory, and its subscriptions through `ISubscriptionManager`, only when the example directory has no user (D157). Then it accepts one event of each message type A to F, and one event of the type C with the scope company Acme, at each start.
2. Add `tests/ExampleApp.Tests.Unit`, on a temporary SQLite database file for each test class. It checks the parts of the contracts that a substitute cannot check:
   - The contract base class `RecipientDirectoryTestsBase<TDirectory>` with `IRecipientDirectoryTestData`, in `tests/MsgHatch.Subscriptions.Tests.Unit/Contracts/`, and its subclass `ExampleRecipientDirectoryUnitTests`, with the directory examples U1 to U4 of `docs/subscription-domain.md`.
   - The whole subscription example of section 1.1 gives the effective result of section 1.1, through the SQLite stores and the example directory.
   - A change through `ISubscriptionManager` changes the next resolution.
   - Two hosts on one database file, each with its own DI container: an event consumer with no transport, and an email publisher with polling. An event that the first host accepts reaches the log email client of the second host (D101).
   - Two tenants with the same user id `"1"` on the same database file: each user gets only the messages of its own tenant (D114).
   - With the in-memory exporter of OpenTelemetry, one event gives a `process` span and a `send` span with a link to it (D132). The duration histograms have the bucket boundaries of section 4.21.
3. Add `tests/MsgHatch.Domain.Tests.Performance`, `tests/MsgHatch.Email.Tests.Performance`, and `tests/MsgHatch.Subscriptions.Tests.Performance` with BenchmarkDotNet and `MemoryDiagnoser`. Each one is an executable with `OutputType` `Exe`. They have their own minimal store, router, loader, transport, and cache backend classes, because a substitute allocates on each call. They measure:
   - `MsgHatch.Email.Tests.Performance`: template parsing and binding, with and without a cache hit, a template store lookup with a cache hit, and the rendering of one email. A cache hit must allocate 0 bytes.
   - `MsgHatch.Domain.Tests.Performance`: `DeterministicId`, one event with 1, 100, and 10,000 routes from a router that does nothing else, on the in-memory stores, `RunAsync` and `DispatchPendingAsync` each with a transport that does nothing, and a call through a logging wrapper with Debug logging off against the same call with no wrapper.
   - `MsgHatch.Subscriptions.Tests.Performance`: the resolution of one event with 1, 100, and 10,000 users, and a subscription lookup with a cache hit against the same lookup with no cache.
4. Write the measured allocation budget into section 4.11.
5. Write `docs/stores.md` with the file stores and the SQLite stores. For each storage kind, it explains the message format, the publish tries, the claim pattern, the two store parts and the tenant field, and the versioning rules. Phases 18 and 19 add their sections.
6. Update `README.md` and `AGENTS.md` with the commands and the package list.

The implementation also did these items, on the request of the owner or from a measurement:
- The files of the example app are in use-case folders: `Notices` (the message types A to E), `PasswordResets` (F), `Recipients`, `Channels`, and `Startup`. The folder names are plural, because the folder is the namespace. The log methods of the example are `[LoggerMessage]` extension methods of `ILogger`.
- `MsgHatchWorker` runs the inbox loop for the role `Consumer` and the outbox loops for the role `Publisher`, so the same worker serves one process and separate processes.
- The example directory keeps its tables `Companies` and `Users` on the SQLite file of the stores, with a `TenantId` column.
- The fixes of D159: a hit of the email template cache allocates nothing, and a span with no listener builds no name.

Tests that the implementation added:
- `tests/ExampleApp.Tests.Unit` starts in the commit of the example app: the golden files of `EventA` to `EventE`, `PasswordReset`, and `Notice` (the email payloads are not stored, so they have no sample), the startup work with two starts on one file (D157), and the whole web app on a `TestServer`: the 7 emails of the startup events, the `/me` route with a token of the user `"1"` (D158) and with no token (401), and the 2 broker messages of a default broker subscription for D through the Wolverine handler.
- The directory tests seed the scenario for each of their 4 tests, not one time.
- The classes with a `TracerProvider` are in the collection `Telemetry`.
- The test project references `Microsoft.AspNetCore.TestHost`, `MsgHatch.Data.Sqlite.Tests.Unit` for the temporary database files and the fake clock that records its waits, and `MsgHatch.Subscriptions.Tests.Unit` for the contract base class and the scenario.

Checked facts (2026-11-12) for phase 13:

| Fact | Source |
|---|---|
| `OpenTelemetry` 1.21.0 (published 2026-11-11) depends on `System.Diagnostics.DiagnosticSource` 11.0.0 for `net10.0`. 1.20.1 has no package dependency for `net10.0`. So the example pins the OpenTelemetry packages at 1.20.1. | The `.nuspec` of both versions on nuget.org |
| From Wolverine 6.0, `WolverineFx` has no runtime compiler. A handler in the default `TypeLoadMode.Dynamic` needs `WolverineFx.RuntimeCompilation` 6.61.0, which brings `JasperFx.RuntimeCompiler` 5.0.1 and Roslyn 5.0.0. | The Wolverine documentation of the code generation, and a break with no package that made the broker test fail |
| Wolverine 6.61.0 routes a message type with a local handler to a local queue by convention. An explicit `ToLocalQueue` route changes no result. | A break that removed the route made no test fail |
| `AddJwtBearer()` with no argument reads `Authentication:Schemes:Bearer`: `ValidIssuer`, `ValidAudiences`, and `SigningKeys` with `Issuer` and a base64 `Value`, the form of `dotnet user-jwts`. The handler maps the claim `sub` to `ClaimTypes.NameIdentifier`. | The test of the `/me` route with a token. A break that read the claim `sub` made it fail. |
| BenchmarkDotNet 0.15.8 counts the allocated bytes with `GC.GetTotalAllocatedBytes`, so a result includes the bytes of all threads. | The assembly references only `GetTotalAllocatedBytes` |
| `DispatchPendingAsync` completes asynchronously. `GetAwaiter().GetResult()` on its pooled `ValueTask` throws `InvalidOperationException`, so the benchmark awaits it. | A benchmark run |
| `AddMsgHatch` binds its options to `IConfiguration`, so a container with no host needs an `IConfiguration`. | The first benchmark run, which failed in each setup |
| `BackgroundService.Dispose` cancels the loops but does not wait for them. `StopAsync` waits. A test that deletes the database file after a worker stops it with `StopAsync`. | The two-host test, which failed to delete its file in a full run |
| On .NET 10 in Release, a lookup of `EmbeddedResourceTemplateStore` with a hit allocates 0 B. | The benchmark of section 4.11 |

The example app was not started with `dotnet run`, because the rules of the phase allow only the unit tests and the benchmarks. The unit tests start it on a `TestServer`, with its startup validation, Wolverine, and the JWT sign-in.

Commits:
1. "Record the decisions of the example app in the plan (D157, D158)"
2. "Name the Wolverine runtime compiler as a package of the example in the plan"
3. "Add the example web app"
4. "Group the files of the example app by use case"
5. "Add example store tests"
6. "Add performance test projects"
7. "Test the broker messages of the example app"
8. "Add the store implementation guide"
9. "Record the decision on the budget breaks in the plan (D159)"
10. "Update README and AGENTS.md"
11. "Allocate nothing on a hit of the email template cache"
12. "Build the span name only for a listener"
13. "Record the measured allocation budget and the checked facts of phase 13 in the plan"

Done when: the build has no errors and no warnings, all tests pass, and the benchmark results are in section 4.11.

### Phase 14: routing verification

Goal: the full routing test of the subscription router (section 4.18).

1. Add `Verification/RoutingVerificationRequest.cs`, `RoutingCheckOutcome.cs`, `RoutingCheck.cs`, `RoutingVerificationReport.cs`, and `IRoutingVerifier.cs` to `MsgHatch.Subscriptions` (sections 4.4 and 4.6).
2. Add `MessageBuilder<TEvent>.WithTestEvent` to `MsgHatch.Subscriptions` (section 4.7).
3. Add the internal `RoutingVerifier` with the procedure of section 4.18, and `RoutingVerifierLoggingWrapper`. `UseSubscriptions` registers them as tenant services. The verifier uses `PreviewAsync` of phase 1 (D100).
4. Add the endpoint `POST {prefix}/verification` to `MsgHatch.Subscriptions.AspNetCore` (section 4.17).
5. In the example app, add a test case for each message type A to E, with the test users Client 1 and Client 3. In the `Development` environment, the app runs the verification at startup with the languages `en` and `hu`, and logs the report.

Tests:
- The verification examples X1 to X7 of `docs/subscription-domain.md`, with the scenario of that document, substitutes for the stores and the directory, and the test channel. Each example asserts the whole list of report rows.
- The standard logging wrapper tests for `RoutingVerifierLoggingWrapper`.
- The endpoint gives 200 with the report, on a `TestServer` with an `IRoutingVerifier` substitute. The query parameter `tenantId` selects the verifier of `TenantKey.For(tenantId)`.
- In `ExampleApp.Tests.Unit`: the verification of the subscription example on the SQLite stores gives no `Failed` row.

Commits:
1. "Record the decisions of the routing verification in the plan (D160 to D163)"
2. "Count the skipped list of an opt-out decision as used in the plan"
3. "Add routing verification types and test events"
4. "Leave the subscriptions of a message type with no test event out of the skipped rows in the plan"
5. "Base the missing template example X4 on the default language fallback in the plan"
6. "Add the routing verifier"
7. "Add the routing verification endpoint"
8. "Add routing verification to the example app"
9. "Record the commits of phase 14 in the plan"

Done when: the build has no errors and no warnings, and all tests pass.

### Phase 15: delivery management

Goal: the API and the HTTP endpoints for a UI of the traffic, the history, and the repairs (section 4.19).

1. Add the members `Cancelled` at the end of `DeliveryStatus` and `PublishOutcome` (D110). The file stores get the status folder `cancelled`.
2. Add the types of section 4.19 in `Management/`: `MessageCursor`, `MessagePage<T>`, `InboxQuery`, `OutboxQuery`, `ReceivedMessageSummary`, `OutgoingMessageSummary`, `OutgoingMessageHistory`, `OutgoingMessageDetail`, `TrafficQuery`, `MessageCount`, `TrafficReport`, `ChannelStatus`, and `DeliveryActionResult`.
3. Add the management members `QueryAsync`, `GetAsync`, `CountAsync`, `RetryAsync`, and `CancelAsync` to the tenant stores `IInboxStore` and `IOutboxStore`. Add `CountAsync` and `PurgeAsync` to `IOutboxDispatchStore`, and `PurgeAsync` to `IInboxDispatchStore`. Add them to the logging wrappers and to the contract base classes (D108, D128, section 5.5).
4. Add the members to the in-memory, file, and SQLite stores, with the indexes of section 4.5. A query or a count of a file store reads each file of the matching folders, with the comment `ponytail: a query reads each file, use a database store for a large outbox`.
5. Add `DeletedMessages`, `IDeliveryManager`, the internal `DeliveryManager` (section 4.19), and `DeliveryManagerLoggingWrapper`. `AddMsgHatch` registers them as tenant services. From this phase on, the tests delete the test data through `IDeliveryManager.DeleteTestDataAsync`.
6. Add `src/MsgHatch.AspNetCore` with `MapMsgHatchDelivery`, `MapMsgHatchDeliveryContent`, the request and response types, and their `JsonSerializerContext` (section 4.19). Add `tests/MsgHatch.AspNetCore.Tests.Unit`.
7. In the example app, map the two route groups, each with its own authorization policy: `DeliveryAdmin` and `DeliveryContent`, each with the role of its name (D165).

Tests:
- The management use cases of `InboxStoreTestsBase` and `OutboxStoreTestsBase` (section 5.5) run on the in-memory, file, and SQLite stores. A count with `IncludeTest` true gives the rows of the real and of the test messages apart (D164). They include the tenant rule that the tenant store of one key does not see the messages of another key.
- A `Cancelled` message is never claimed again, and `RecordTryAsync` keeps its status rules.
- `PurgeAsync` of each dispatch store deletes only the finished messages that finished before the given time, of all tenants, at most `maxCount`, and never a pending message (D128).
- Delivery manager tests, with substitutes for the stores and the dispatch signal, and the test channel:
  - `GetChannelsAsync` gives each registered channel with its down state, its pending counts of real and of test messages, and its oldest due time (D164). It calls `GetDownChannelsAsync` one time and `CountAsync` one time.
  - `PauseChannelAsync` calls `SetChannelDownAsync` with the reason `Operator`, the fake time, and the note. `ResumeChannelAsync` calls `SetChannelUpAsync` with no reason, and notifies the topic of the channel. An unregistered channel gives `Errors.NotFound` for both.
  - Each validation rule of section 4.19 gives `Errors.ValidationFailed`, and the stores receive no call. The rows include `Limit` 0, 1, 500, and 501, `Ids` with 0, 1, 1000, and 1001 ids, a note of 500 and of 501 characters, a bulk action with only `Limit` and `IncludeTest`, a purge with `maxCount` 0, and a pause, a resume, a test data delete, and a purge on the manager of a tenant.
  - A retry over three pages of the query calls `RetryAsync` one time for each page, and returns the sums of the matched and the changed messages.
  - `PurgeAsync` with `maxCount` 100 calls each dispatch store with `maxCount` 100, and returns both counts. `DeleteTestDataAsync` calls both dispatch stores, and returns both counts.
  - With no inbox store, an inbox page is empty, and the inbox counts are empty.
  - A retry of the outbox gives `addedTries` from the retry options of the content type of each message: 3 for a registration with `WithRetry(o => o.MaxTryCount = 3)`, and 8 for another registration on the same page.
  - A retry with changes notifies the topic of each changed channel. A retry with no change notifies nothing.
  - `GetOutgoingMessageAsync` gives the summary, the trace, and the tries, with no envelope. An unknown id, or the id of a message of another tenant, gives `Errors.NotFound`.
  - The standard logging wrapper tests for `DeliveryManagerLoggingWrapper`. A pause, a resume, a retry, and a cancel log at the Information level. No log record contains the address of a test envelope.
- The HTTP endpoints, on a `TestServer` with an `IDeliveryManager` substitute:
  - Each route of section 4.19 calls the matching manager method, with each query parameter, the repeated `ids`, and the cursor text. A page gives `items` and `next`, and the text of `next` reads back as the same `MessageCursor`.
  - The query parameter `tenantId` selects the manager of `TenantKey.For(tenantId)`, and a route with no `tenantId` uses the manager of `TenantKey.None`. The pause, resume, and purge routes with a `tenantId` give 400.
  - `Errors.ValidationFailed` gives 400, and `Errors.NotFound` gives 404, each with a `ProblemDetails`.
  - The content routes exist only in the group of `MapMsgHatchDeliveryContent`. A policy on one group does not apply to the other group.
  - The outgoing content route writes the envelope of a test message with `MsgHatchJson`.
- In `ExampleApp.Tests.Unit`: with the two hosts of phase 13, a pause through the endpoint of the first host stops the email sends of the second host, and a resume starts them again (D106).

Commits:
1. "Record the decisions of the delivery management in the plan (D164 to D167)"
2. "Count the real and the test messages apart in the plan (D164)"
3. "Add cancelled messages"
4. "Add the delivery management types, store members, and purge"
5. "Add delivery management to the in-memory, file, and SQLite stores"
6. "Add the delivery manager"
7. "Add HTTP endpoints for delivery management"
8. "Add delivery management to the example app"
9. "Test the error paths and the response bodies of the delivery management"
10. "Record the commits of phase 15 in the plan"
11. "Record phase 15 as done in README and AGENTS.md"

Commit 4 adds the dispatch store members `CountAsync` and `PurgeAsync` with the in-memory, file, and SQLite stores, and commit 5 adds the tenant store members with the stores. An interface member and its implementations come in one commit, so that each commit builds.

Done when: the build has no errors and no warnings, and all tests pass.

### Phase 16: registry administration

Goal: stored registry settings on top of the code, for an administration UI (section 4.20).

1. Add `ChannelEntry`, `MessageTypeEntry`, `RegistryEntries`, `IRegistryWriter`, and `IRegistrySettingsStore` (sections 4.6 and 4.20), with `RegistryWriterLoggingWrapper` and `RegistrySettingsStoreLoggingWrapper`.
2. Add `RegistryOptions` with its validator, `StoredRegistry` with its snapshot and its `TimeProvider` timer, `UseStoredRegistry`, and `UseRegistrySettingsStore` (sections 4.7 and 4.20). Add the startup validation rule: `UseStoredRegistry` needs a registry settings store.
3. Add the registry checks of the pipeline: the check of a disabled message type in step 2, the skip of a disabled channel in step 3, and `NotificationResult.Skipped` (section 4.8).
4. Add `InMemoryRegistrySettingsStore` to `UseInMemoryStores`, and the registry settings stores of the file and SQLite packages, with the SQLite tables `MsgHatchChannelEntry` and `MsgHatchMessageTypeEntry` (section 4.5). Add the contract base class `RegistrySettingsStoreTestsBase<TStore>` with `IRegistrySettingsStoreTestData` in `tests/MsgHatch.Domain.Tests.Unit/Contracts/`, and its subclasses for the three stores.
5. Add `MapMsgHatchRegistry` to `MsgHatch.AspNetCore` (section 4.20).
6. In the example app, use `UseStoredRegistry`, and map the registry endpoints with an authorization policy.

Tests:
- The use cases of `RegistrySettingsStoreTestsBase`: save a new entry with the revision 1, update it with the stored revision plus 1, a revision that is not the stored revision plus 1 gives `Errors.Conflict`, a delete of an unknown entry succeeds, and the disabled channels round-trip unchanged.
- `StoredRegistry` merges the code descriptors with the entries: an entry wins for each value that it has, `ChannelEntry.DisplayName` replaces the name of the channel, and a channel with no entry keeps the values of the code.
- Before the first read of the store, `StoredRegistry` gives the values of the code. It reads the store again after `RefreshInterval` moves on the fake time, and not before. A write in its own process is visible at once. A store error after a successful read keeps the last snapshot.
- `UseStoredRegistry` with no registry settings store fails the startup validation with an `OptionsValidationException`.
- Each validation rule of the entries in section 4.20 gives `Errors.ValidationFailed`. An entry of an unregistered channel gives `Errors.NotFound`.
- A disabled message type stores nothing, and its router gets no call. A route on a disabled channel, and a route on a channel in `DisabledChannels`, are skipped and counted in `NotificationResult.Skipped`. The other routes are stored.
- The options tests of `RegistryOptions`.
- The endpoints, on a `TestServer`: each route calls the matching reader or writer method. With the in-memory reader, only the GET routes exist, and each revision is 0.
- The standard logging wrapper tests for the writer and settings store wrappers.

Commits:
1. "Record the decisions of the registry administration in the plan (D168 to D171)"
2. "Fix the commit list of phase 16 in the plan"
3. "Add the registry settings store contract and the in-memory store"
4. "Add the stored registry"
5. "Skip disabled message types and channels in the pipeline"
6. "Add registry settings stores to the file and SQLite packages"
7. "Add HTTP endpoints for registry administration"
8. "Add registry administration to the example app"
9. "Record phase 16 as done in README and AGENTS.md"

Commit 3 adds `UseRegistrySettingsStore`, `InMemoryRegistrySettingsStore` with `UseInMemoryStores`, and the contract base class with its first subclass. A test reaches an internal logging wrapper only through a setup method, and a contract base class runs only through a subclass, so each one comes with the code that runs it. Commit 4 adds `RegistryWriterLoggingWrapper` with `UseStoredRegistry`, which registers it. Item 6 gets its own commit 8.

Done when: the build has no errors and no warnings, and all tests pass.

### Phase 17: a store for each tenant

Goal: one storage for each tenant, for example one SQLite file for each tenant (D125).

1. Add `ITenantSource` and `TenantSourceLoggingWrapper` (sections 4.6 and 4.15).
2. Add the library dispatch composites `TenantInboxDispatchStore` and `TenantOutboxDispatchStore`, with the next due time of each tenant and channel, and the core method `UseStorageForEachTenant<TTenantSource>` with the internal wrapper of the tenant stores, to `MsgHatch.Domain`. They follow the rules of section 4.5 (D125). Add the subclasses `TenantInboxDispatchStoreUnitTests` and `TenantOutboxDispatchStoreUnitTests` of the store contract base classes, on in-memory stores for two tenants and for `TenantKey.None`.
3. Add `SqliteTenantStoreOptions` with its validator and `UseSqliteTenantStores<TTenantSource>` to `MsgHatch.Data.Sqlite`, and `UseSqliteTenantSubscriptionStore` to `MsgHatch.Subscriptions.Data.Sqlite` (section 4.7).

Tests:
- The use cases of `InboxStoreTestsBase` and `OutboxStoreTestsBase` run on the dispatch composites. The claim test with the due messages of two tenants compares the claimed messages with no order, because a composite keeps the claim order only inside the storage of one key (section 4.5).
- With an in-memory store for each key through a test function:
  - The tenant store of each key writes only into the store of that key.
  - A claim takes the messages of all stores in turn, and starts after the store that gave the last messages. `NextDueAtUtc` is the earliest value of all stores.
  - After a claim that finds no due message of a tenant, the next claim does not ask that tenant before its next due time. An `AddAsync` through the tenant store of that tenant in the same process clears that time, so the next claim asks it. A message that another store instance adds is found after `MaxPollInterval`.
  - `RecordTryAsync`, `CompleteAsync`, and `FailAsync` go only to the store of the tenant of the message.
  - The down states are only in the store of `TenantKey.None`. With a down state there, a claim returns that state, and claims no message of a tenant store.
  - `CountAsync` sums the counts of all stores. `DeleteTestDataAsync` and `PurgeAsync` call each store, and `PurgeAsync` deletes at most `maxCount` messages in total.
  - A claim skips the store of a tenant that returns an error, logs it with the tenant, and returns the messages of the other stores. An error of the store of `TenantKey.None` fails the claim (D173).
- `UseSqliteTenantSubscriptionStore` gives the subscription store of each key on the file of that key.
- `UseSqliteTenantStores` with the `TenantConnectionString` `Data Source={folder}/msghatch-{tenant}.db` makes one file for each tenant of the tenant source, and the file of `SystemConnectionString` for the messages with no tenant, the down states, and the registry settings. The tenant id `a|b` gives the file `msghatch-a%7^cb.db`, and the tenants `Acme` and `acme` give two files (D172).
- The options tests of `SqliteTenantStoreOptions`: a `TenantConnectionString` with no `{tenant}` fails, and a `SystemConnectionString` with `{tenant}` fails.
- A host that calls both `UseSqliteStores` and `UseSqliteTenantStores` fails the startup validation, because two methods select the outbox stores (D62).
- In `ExampleApp.Tests.Unit`: two tenants on two SQLite files. An event of each tenant is sent, and each file has only the messages of its tenant. The test builds its host from the setup methods, and the example app does not change (D174).

Commits:
1. "Record the decisions of the stores for each tenant in the plan (D172 to D174)"
2. "Fix the claim order of the dispatch composites in the plan"
3. "Add the tenant source and the dispatch composites"
4. "Test the error paths of the dispatch composites"
5. "Add SQLite stores for each tenant"
6. "Record the commits of phase 17 in the plan"
7. "Record phase 17 as done in README and AGENTS.md"

Commit 5 also adds the stores for each tenant to `docs/stores.md`.

Done when: the build has no errors and no warnings, and all tests pass.

### Phase 18: EF Core stores

Goal: the inbox, outbox, registry settings, and subscription stores on EF Core, with SQL Server as the first provider (D120).

1. Add `src/MsgHatch.Data.EFCore` with `EntityFrameworkStoreOptions` and its validator, `MsgHatchDbContext`, `EntityFrameworkInboxStore`, `EntityFrameworkOutboxStore`, `EntityFrameworkRegistrySettingsStore`, and `UseEntityFrameworkStores` (sections 4.5 and 4.7):
   - The model maps the columns of section 4.5, in the schema of `EntityFrameworkStoreOptions.Schema`, with the same indexes. The partial claim index is a filtered index with `WHERE Status = 'Pending'` (D104).
   - Each store is a singleton, and makes one `DbContext` for each operation through the pooled `IDbContextFactory<MsgHatchDbContext>` (D130).
   - The claim is one raw SQL statement through `FromSql`, because LINQ cannot express it. For SQL Server, a subquery selects the `Sequence` of the first due rows with `TOP (@max)`, `ORDER BY NextTryAtUtc, Sequence`, and the hints `ROWLOCK, READPAST, UPDLOCK`, and an `UPDATE` of these rows with an `OUTPUT` clause sets the lease (D120). An `UPDATE` of a CTE puts a sort for the Halloween protection under the `TOP`, so it locks each due row, and a parallel claim gets fewer messages. Phase 18 found this on SQL Server 2025 CU7. `Sequence` is an identity column. The store sorts the `OUTPUT` rows by the old `NextTryAtUtc` from `deleted` and by `Sequence` (D144). The store selects the statement by the provider of the context. A provider with no statement fails the startup validation with an `OptionsValidationException` that names the provider, and a store call on it gives `Errors.NotFound` (D175). The startup validation of MsgHatch reports an `OptionsValidationException`, not an `Error`.
   - `RecordTryAsync` is one `UPDATE` that appends the try to the JSON column `Tries` with `JSON_MODIFY`, with `WHERE Id = @id AND TryCount = @tryNumber - 1` (D27).
   - The package creates no schema. The host creates it with its own EF Core migrations, and the tests use `EnsureCreatedAsync`.
2. Add `src/MsgHatch.Subscriptions.Data.EFCore` with `EntityFrameworkSubscriptionStore` and `UseEntityFrameworkSubscriptionStore`.
3. Add `tests/MsgHatch.Data.EFCore.Tests.Integration` and `tests/MsgHatch.Subscriptions.Data.EFCore.Tests.Integration`, with Testcontainers for SQL Server (D124). From D180, the tests use the SQL Server of `SqlServerIntegration:ConnectionString` in the git-ignored `appsettings.Local.json`, for example the local container dev-mssql, and Testcontainers only with no value. They drop the databases with the name prefix of their test project at the start and at the end of a run.
4. Add the EF Core sections to `docs/stores.md`.

Tests:
- The use cases of `InboxStoreTestsBase`, `OutboxStoreTestsBase`, `RegistrySettingsStoreTestsBase`, and `SubscriptionStoreTestsBase` run on the EF Core stores against SQL Server in a container. Each test class uses its own database.
- Two parallel claims get different messages, so the claim is atomic between concurrent dispatchers. A claim of one message takes the message with the earliest `NextTryAtUtc`.
- The options tests of `EntityFrameworkStoreOptions`.
- A host with the EF Core SQLite provider fails the startup validation, and the message names the provider (D175).
- More tests unique to the EF Core stores, from breaks of the code that showed gaps of the contract tests:
  - The setup methods: one call of `UseEntityFrameworkStores` registers each store with its logging wrapper, and with no logging each store is the one instance of its class. A schema from code keeps the tables and the rows in that schema, also for the subscription store after `UseEntityFrameworkStores`. A schema in the section reaches the options, a schema from code wins, and a schema that is not an identifier fails the validation on `Schema`. A store call on the SQLite provider gives `Errors.NotFound`. `UseEntityFrameworkStores` after `UseInMemoryStores` names both methods for each store interface.
  - The stores write the enum values as text, an empty tenant, the trace type, and `[]` as the first `Tries`. A batch keeps its order in `Sequence`. A row with a column that is not JSON, or that adds a property to the document, fails only that row. A newer version stays leased. A down row with an unknown reason, and a registry or subscription row that a person broke, give the error of the row. A database that does not exist gives `Errors.Unavailable`, and a cancelled token gives `Errors.OperationCancelled`.
  - The times of a summary have the kind UTC.
- Four tests from these breaks are in the contract base classes, so each store runs them (see "After the first tag" below): 160 parallel adds of 20 messages store each message one time with no error. A second down state with the reason `Errors` returns false with no error. A purge with `maxCount` 1 deletes the message that finished first. A delete through the subscription store of another tenant keeps the subscription.

Checked facts (2026-11-13) for the EF Core stores, on SQL Server 2025 RTM-CU7 (17.0.4065.4) in the container image `mcr.microsoft.com/mssql/server:2025-CU7-ubuntu-24.04`:

| Fact | Source |
|---|---|
| An `UPDATE` of a CTE with `TOP (@maxCount)`, `ORDER BY NextTryAtUtc, Sequence`, and `ROWLOCK, READPAST, UPDLOCK` has a `Sort` under the `Top`, also after an `Index Seek` of the filtered due index. The sort is the Halloween protection, because the statement changes `NextTryAtUtc`, a key of that index. It reads and locks each due row. In a probe of 30 rounds with 10 due messages and two parallel claims of 5, rounds 1 to 15 each gave one claim only 0, 2, or 3 messages. The output stopped after round 15, so the later rounds are not known. An `UPDATE ... WHERE Sequence IN (SELECT TOP (@maxCount) ...)` has the `Top` directly on the ordered `Index Seek`, and a `Table Spool` above it. It gave 10 different messages in 30 of 30 rounds. | `SET SHOWPLAN_TEXT ON` on a probe table, and a probe test of the inbox store |
| `OUTPUT deleted.NextTryAtUtc` gives the due time before the lease. EF Core 10.0.14 runs an `UPDATE ... OUTPUT` statement through `FromSqlRaw` with no composition, and maps the `OUTPUT` columns by name. | A probe on SQL Server, and the contract tests |
| `JSON_MODIFY(Tries, 'append $', JSON_QUERY(@try))` appends the try as an object. Without `JSON_QUERY`, it appends the text as a JSON string, for example `["{\"tryNumber\":3}"]`. `JSON_MODIFY(@try, '$.tryNumber', TryCount + 1)` sets a number. | A probe on SQL Server |
| `JSON_VALUE(@document, '$.trace."$type"')` reads the trace discriminator, and `JSON_QUERY` of a `null` property gives `NULL`. | A probe on SQL Server |
| `JSON_OBJECT` writes a `datetime2` as `"2030-01-01T00:00:00"`, with no `Z`. `JSON_QUERY` of a text that is not JSON fails the statement with error 13609 (D176). | A probe on SQL Server |
| The default collation `SQL_Latin1_General_CP1_CI_AS` compares `'abc'` and `'ABC'` as equal, and `Latin1_General_100_BIN2` as different. A parameter and the `value` of `OPENJSON` compare with a `BIN2` column with no collation conflict, with the collation of the column. | A probe on SQL Server |
| `ORDER BY` of a `uniqueidentifier` descending gives `00000000-0000-0000-0000-000000000002` before `01000000-0000-0000-0000-000000000000`, so it is not the order of `Guid.CompareTo` (D112). | A probe on SQL Server |
| `Microsoft.EntityFrameworkCore.Relational`, `.SqlServer`, and `.Sqlite` 11.0.0 target only `net11.0`, so the phase pins 10.0.14, the servicing band of the other Microsoft packages. EF Core SqlServer 10.0.14 and 10.0.15 depend on `Microsoft.Data.SqlClient` 6.1.7, which brings `System.Security.Cryptography.Pkcs` 9.0.11 with GHSA-846f-f764-76mx (high) and GHSA-2fmv-rv2c-59x7 (moderate). `Microsoft.Data.SqlClient` 6.1.8 brings the fixed 9.0.21, so the test projects pin it. | The nuspec of each package, the NuGet warnings NU1902 and NU1903, and the GitHub advisories |
| The new packages have the MIT license, also each transitive package, except `Microsoft.Data.SqlClient.SNI.runtime` 6.0.3 and `Microsoft.Identity.Client.NativeInterop` 0.20.6, which have the Microsoft Software License Terms. Only the test projects reference them (D179). | The nuspec and the license file of each package |
| With the connection string of dev-mssql in `appsettings.Local.json`, both projects passed with no new container. The log of dev-mssql names each test database with its start and its delete. `@@SERVERNAME` gives `2c84ce1ea605`, and the host name of dev-mssql is `1a246115df45`, because the volume `transcend-dev_dev-mssql-data` keeps the `master` database of an older container. A run with no drop at its end left 5 databases, the next run dropped these 5 at its start, and the run after it found 0 (D180). | Three runs of the core project with the diagnostic messages of the fixture, `docker events`, and `docker logs dev-mssql` |
| Testcontainers.MsSql 4.16.0 starts the local image with no pull. With the default settings, it also starts the Ryuk container `testcontainers/ryuk`, which this machine does not have (D177). | The test runs with `TESTCONTAINERS_RYUK_DISABLED=true` |
| After the first tag: with the message id parameter `@id` as `nvarchar` (`DbType.String`) and the column `Id` as `char(36)`, 8 parallel adds of each of 20 outgoing messages got the deadlock 1205 in 4 of 14 runs. With `@id` as `char` (`DbType.AnsiStringFixedLength`), 13 of 13 runs passed, and the test took 3 to 4 seconds instead of up to 11. The query plan is not checked. So the cause in the lock ranges of `UPDLOCK, HOLDLOCK` is not confirmed. A likely cause is that SQL Server converts the column to compare it with an `nvarchar` value. | The contract test `AddAsync_EightParallelAddsOfEachOfTwentyMessages_StoreEachMessageOneTimeWithNoError`, 10 runs of each type on dev-mssql, and the error number in the test output |

Commits:
1. "Record the decisions of the EF Core stores in the plan (D175 to D178)"
2. "Fix the check of the EF Core provider in the plan"
3. "Fix the claim statement of the EF Core stores in the plan"
4. "Record the license decision of the test database provider (D179)"
5. "Add EF Core stores for the inbox, the outbox, and the registry settings"
6. "Add the EF Core subscription store"
7. "Record the commits and the checked facts of phase 18 in the plan"
8. "Record phase 18 as done in README and AGENTS.md"
9. "Record the server decision of the EF Core integration tests (D180)"
10. "Run the EF Core integration tests on a configured SQL Server"
11. "Record the rename of the EF Core packages (D181)"
12. "Rename the EF Core projects and namespaces to EFCore"

Commit 5 also adds the EF Core rules to section 4.5 of the design and the EF Core section to `docs/stores.md`, and commit 6 adds the subscription part of that section.

After the first tag `phase-18` on `bf84e33`, a review before phase 19 found three loose ends, and the owner chose to fix them:
- Four tests from the breaks of the phase ran only on the EF Core stores. They moved into the contract base classes, so the in-memory, file, SQLite, and EF Core stores run them, and the MongoDB stores of phase 19 get them. The parallel adds and the purge order now also run for the other dispatch store. The new outbox test found the deadlock above, and commit 14 fixes it.
- `UseEntityFrameworkSubscriptionStore` had no startup check (D182).
- D124 and section 5.5 of the design did not name the configured server of D180, and `docs/stores.md` did not tell a host to pin `Microsoft.Data.SqlClient` 6.1.8.

The owner also asked for one line end in the repository: `.gitattributes` sets LF for each text file. The index already had LF in each text file, so the commit changes no other file.

Commits after the first tag:
13. "Add a .gitattributes file with LF line ends"
14. "Fix a deadlock of parallel adds in the EF Core outbox store"
15. "Move four store tests of phase 18 into the contract base classes"
16. "Record the startup check of the EF Core subscription store (D182)"
17. "Add the startup check of the EF Core subscription store"
18. "Record the test server of D180 in D124 and section 5.5"
19. "Record the fixes after phase 18 in the plan"

Then the owner answered the open questions of the review:
- The MongoDB integration tests of phase 19 use dev-mongo when a connection string is set, and Testcontainers only with no connection string (D183).
- The cancel statement of the EF Core outbox store compares the `char(36)` column with the `nvarchar` values of `OPENJSON`. The owner asked for test coverage. A new contract test runs 8 parallel cancels of each of 20 outgoing messages. It passed 10 of 10 runs on SQL Server, so the statement stays unchanged.
- A second new contract test runs 8 parallel records of the first try of each of 20 outgoing messages. It covers the `@id` type of the try statement, which commit 14 changed with no test of its own. On SQL Server, it passed 10 of 10 runs also with the old `nvarchar` type. On the file store, 52 to 61 of the 160 calls gave `Errors.Unavailable` in 5 of 5 runs, and each message kept one try. The fix runs a try record of the file store behind the gate of its claims, retries, and cancels.

More commits after the first tag:
20. "Record the server decision of the MongoDB integration tests (D183)"
21. "Serialize the try records of the file outbox store"
22. "Add a contract test of parallel cancels of outgoing messages"
23. "Record the answers after phase 18 in the plan"
24. "Record the move of the phase-18 tag in the plan"

Then the owner moved the tag `phase-18` from `bf84e33` to commit 24, so the tag holds these fixes.

Done when: the build has no errors and no warnings, all unit tests pass, and the integration tests pass in the stage after the merge.

### Phase 19: MongoDB stores

Goal: the inbox, outbox, registry settings, and subscription stores on MongoDB (D121).

1. Add `src/MsgHatch.Data.MongoDb` with `MongoDbStoreOptions` and its validator, `MongoDbInboxStore`, `MongoDbOutboxStore`, `MongoDbRegistrySettingsStore`, and `UseMongoDbStores` (sections 4.5 and 4.7):
   - A store converts the JSON document of `MsgHatchJson` into one BSON document, and back for a read (D184). Each time is the sub-document `{ d, t }`, and each filter, sort, and index uses `t` (D185). An add is one unordered `insertMany` with no transaction (D186).
   - A claim is a loop of `findOneAndUpdate` calls, one for each message, until `maxCount` messages or no due message (D121). Each call sorts by `nextTryAtUtc` and then by the ObjectId `_id`, so the claim takes the messages in the order of their due time, and of their add for the adds of one process (D144, D187). Each call sets `nextTryAtUtc` and `leaseUntilUtc` to the end of the lease (D103).
   - A try is a `$push` in the same update as the delivery state, with the filter `tryCount` equal to the try number minus 1 (D27).
   - The claim index is a partial index on `channel`, `nextTryAtUtc.t`, and `_id` with the filter `status: "Pending"` (D104), and the inbox has one on `nextTryAtUtc.t` and `_id`. On MongoDB 8.3.7, a `findOneAndUpdate` with this sort has a `SORT` stage above the index scan when the index has no `_id`, so it reads each due document. With `_id` as the last key, the plan is a `LIMIT` on the index scan with no sort (checked with `explain` on dev-mongo). The store creates its indexes one time for each database in the process.
2. Add `src/MsgHatch.Subscriptions.Data.MongoDb` with `MongoDbSubscriptionStore` and `UseMongoDbSubscriptionStore`.
3. Add `tests/MsgHatch.Data.MongoDb.Tests.Integration` and `tests/MsgHatch.Subscriptions.Data.MongoDb.Tests.Integration`. They use the MongoDB of `MongoDbIntegration:ConnectionString` in the git-ignored `appsettings.Local.json`, for example the local container dev-mongo, and Testcontainers for MongoDB only with no value (D124, D183). They drop the databases with the name prefix of their test project at the start and at the end of a run. The name of the subscription test project alone has 53 characters. MongoDB 8.3.7 allows at most 63 characters in a database name (error 73 `InvalidNamespace` at 64, checked on dev-mongo), so the prefix with its `_` stays, and the name of each test database adds at most 9 characters to it.
4. Add the MongoDB sections to `docs/stores.md`.

Tests:
- The use cases of `InboxStoreTestsBase`, `OutboxStoreTestsBase`, `RegistrySettingsStoreTestsBase`, and `SubscriptionStoreTestsBase` run on the MongoDB stores against the MongoDB of D183. Each test class uses its own database.
- Two parallel claims get different messages.
- The options tests of `MongoDbStoreOptions`.
- More tests unique to the MongoDB stores, from breaks of the code that showed gaps of the contract tests:
  - The document shape: the enum values as text, a null tenant, the trace with its `$type`, the empty array `tries`, a publish try, a down state, a registry entry, and a subscription with its settings as a sub-document. Each time is `{ d, t }`, and a time with ticks below a millisecond round-trips unchanged and keeps its claim order (D185).
  - The numbers of an event keep their digits and their BSON types, and a number of 35 digits gives `Errors.SerializationFailed` (D184).
  - The ObjectIds keep the order of the creation times, a batch with a stored id first stores the other messages (D186, D187), and the indexes have their keys, their partial filter, and `unique`.
  - A document with a value that a person broke, or with a newer version, fails only itself. A server that the driver cannot reach gives `Errors.Unavailable`, and a cancelled token gives `Errors.OperationCancelled`.
  - The setup methods: one call of `UseMongoDbStores` registers each store with its logging wrapper, an `IMongoClient` of the host stays, the database name of the section and of code reaches the stores, and two setup methods with a value in code fail the startup validation.

Checked facts (2026-11-14) for the MongoDB stores, on MongoDB 8.3.7 (the local container dev-mongo, a replica set with one member) with `MongoDB.Driver` 3.14.0:

| Fact | Source |
|---|---|
| A database name has at most 63 characters: 64 give error 73 `InvalidNamespace`. Two database names that differ only in case give error 13297. | A probe with `mongosh` on dev-mongo, with probe databases of the test prefix, dropped after it |
| A `findOneAndUpdate` with the filter on the status, the channel, and the due ticks, and the sort by the due ticks and `_id`, has the plan `UPDATE > SORT > FETCH > IXSCAN` with the partial index on `(channel, nextTryAtUtc.t)`, and `UPDATE > LIMIT > FETCH > IXSCAN` with `_id` as the third key. | `explain` of `findAndModify` on dev-mongo |
| A nested field `$type`, for example `trace.$type`, inserts and reads back unchanged through `mongosh` and through the C# driver. | A probe on dev-mongo, and the outbox tests with a route trace |
| The driver keeps the BSON class maps for the whole process, and freezes a class map at its first use (D184). | The documentation of the driver repository, through Context7 |
| `Decimal128.TryParse` of `MongoDB.Bson` 3.14.0 keeps the digits of a number text up to 34 significant digits, for example `19.90`, `5E-324`, and `1.7976931348623157E+308`. It returns false for 35 digits and for `1E+7000`, and does not round. | A probe with `MongoDB.Bson` 3.14.0 |
| An unordered `InsertManyAsync` of a batch with a stored id first inserts the other documents, and gives a `MongoBulkWriteException` with only `DuplicateKey` errors. | The test `AddAsync_BatchWithAStoredIdFirst_StoresTheNewMessagesAfterIt`, which fails with `IsOrdered = true` |
| A server on a port with no listener gives `System.TimeoutException` after the server selection timeout, not a `MongoException`. | The tests `..._ServerThatCannotBeReached_...`, which fail when the store maps only `MongoException` |
| `MongoDB.Driver` 3.14.0 (published on 2026-11-12) and `MongoDB.Bson` 3.14.0 have the license Apache-2.0. The resolved transitive packages are `DnsClient` 1.6.1 (Apache-2.0), `SharpCompress` 0.48.1 (MIT), `Snappier` 1.3.1 (BSD-3-Clause), and `ZstdSharp.Port` 0.7.3 (MIT). `Testcontainers.MongoDb` 4.16.0 has MIT. | The nuspec of each package, and `dotnet list package --include-transitive` |
| With no connection string, `new MongoDbBuilder("mongo:8.3")` of `Testcontainers.MongoDb` 4.16.0 started MongoDB 8.3.7 from the local image, and Ryuk from the local image `testcontainers/ryuk:0.14.0`, with no pull. The core project passed 335 tests, and no container stayed. | A run in a worktree with no `appsettings.Local.json`, and `docker images` and `docker ps -a` before and after |
| With the connection string of dev-mongo, the core project passed 335 tests and the subscription project 50 tests. Each fixture dropped its test databases at the end, and dev-mongo had no test database after the runs. | The diagnostic messages of the fixtures, and `listDatabases` on dev-mongo |

Commits:
1. "Record the decisions of the MongoDB stores in the plan (D184 to D187)"
2. "Fix the claim index and the test server of the MongoDB stores in the plan"
3. "Add MongoDB stores for the inbox, the outbox, and the registry settings"
4. "Add the MongoDB subscription store"
5. "Record the commits and the checked facts of phase 19 in the plan"
6. "Record phase 19 as done in README and AGENTS.md"

Commit 3 also adds the MongoDB rules to section 4.5 of the design and the MongoDB section to `docs/stores.md`, and commit 4 adds the subscription part of that section.

Done when: the build has no errors and no warnings, all unit tests pass, and the integration tests pass in the stage after the merge.

### Phase 20: Amazon SES against a real account

Goal: the SES client of phase 4 works against a real SES account, and each difference that the run finds is fixed. The owner moved this test out of phase 4, so that the next phases do not wait for an SES account. The phase needs only phase 4, so it can run at any time after it.

1. Put the settings of an SES account into `tests/MsgHatch.Email.AmazonSES.Tests.Integration/appsettings.Local.json`, which git ignores: the access key of an IAM user with `ses:SendEmail` and `ses:GetAccount`, the region code, and a verified sender identity. `appsettings.json` next to it describes each key.
2. Run the SES integration tests of phase 4. Each run sends 3 billed emails to the SES mailbox simulator, also in the SES sandbox.
3. For each test that fails, first find out whether the client or the premise of the test is wrong. The premises with the most risk are the RFC 2047 sender name, and `Errors.Rejected` for a sender with an accented local part. Record each changed rule in the design, and fix the client with a unit test that fails first.

Tests:
- The 4 SES integration tests of phase 4 pass against the real account.
- Each fix has a unit test in `MsgHatch.Email.AmazonSES.Tests.Unit` that fails first.

Commits:
1. One commit for each fix, if the run finds a difference, for example "Fix the sender name of the Amazon SES client".

Done when: the build has no errors and no warnings, all tests pass, and the SES integration tests pass with an SES account in `appsettings.Local.json`.

### Phase 21: dispatch signal between processes

Goal: a publisher in its own process wakes at once when another process of the same OS user on the same machine stores messages for its channel (D188). While a signal does not work, the fallback wait of a dispatch loop obeys a next try time that a worker gives (D103).

1. Fix the fallback wait of the dispatch loop (D102, D103). Since phase 2, `WaitForTheBrokenSignalAsync` in `src/MsgHatch.Domain/Dispatch/DispatchLoop.cs` (lines 186 to 192) waits with `Task.Delay(..., time, ct)` on line 190. That delay listens only to the stop token `ct`. A worker gives a time through `Give` (line 220), which cancels the wake token of the wait, but the delay does not listen to the wake token. So a time that a worker gives during the fallback wait has no effect: the next try time of a recorded try, the next try time of a failed received message, or the lease end of a skipped message. The loop claims again only at the due time of the wait: the next due time of the claim, the poll delay, or `PollInterval` when the loop has no due time. The fix:
   - The fallback wait listens to the same wake token as the signal wait. It returns `Wake.TimeGiven` when a worker gives an earlier time, and `Wake.Due` when its due time comes. The loop of `WaitAsync` (line 131) then calculates the earliest time again, so the loop claims at the given time.
   - With no due time, the fallback wait still ends after `PollInterval`.
   - The rule of `IDispatchSignal.WaitAsync` in section 4.6 and part 3 of section 4.9 get the new text: after a signal error, a loop waits until the earliest of its times of step 5, which include a time that a worker gives during the wait, or for `PollInterval` when it has no such time.
2. Add `src/MsgHatch.Dispatch.NamedPipes` (D188, D189, D192) with `NamedPipeSignalOptions` and its source-generated validator, the internal `NamedPipeDispatchSignal`, and `UseNamedPipeDispatchSignal`:
   - `NamedPipeSignalOptions` has the section `MsgHatch:NamedPipeSignal` and one setting, `PipeNamePrefix`. It is required, and has 1 to 40 characters from the ASCII letters, the digits, `_`, and `-`. All processes of one system use the same prefix. Two systems on one machine use two prefixes.
   - Each topic has one pipe: `{PipeNamePrefix}.inbox`, and `{PipeNamePrefix}.outbox.{Channel}` for a channel. A channel name has only ASCII letters, digits, and `_`, so each pipe name is valid on each platform.
   - `NotifyAsync` notifies the loops of its own process at once through an `InProcessDispatchSignal`, and returns null at once. When the process does not serve the pipe of the topic, it also gives the topic to the writer of that pipe. The writer holds one bounded queue of capacity 1 with `DropWrite`, so many notifications before one write become one byte.
   - The writer of a pipe keeps one client connection with `PipeDirection.Out` and `PipeOptions.CurrentUserOnly` (D191). The connection starts with the 16-byte id of the signal instance, and the writer then writes one byte for each notification. When no process serves the pipe, the connect fails with a `TimeoutException` after 100 ms, and the writer drops the notification. The next notification tries to connect again. A write error closes the connection, and the writer connects one more time and writes again. A lost notification only delays a send until the next poll (D102, D190).
   - At the first `WaitAsync` of a topic, the signal starts to serve the pipe of the topic: a loop of `NamedPipeServerStream` instances with `PipeDirection.In`, `MaxAllowedServerInstances`, and `PipeOptions.CurrentUserOnly`, and with no `PipeOptions.FirstPipeInstance` (D192). Each byte that arrives notifies the loops of the topic in the process. A server closes a connection that starts with the id of its own signal instance. `WaitAsync` then waits for a notification of the in-process signal.
   - When the server of a topic cannot start, or stops with an error, `WaitAsync` returns `Errors.Unavailable` at once, and the loop uses the fallback wait of item 1. The next `WaitAsync` tries to start the server again. The signal logs the error at the Warning level, one time for each outage, with the topic.
   - The signal reads no time. It is a thread-safe singleton, and `Dispose` closes each server instance, each connection, and each writer at once.
   - `UseNamedPipeDispatchSignal(Action<NamedPipeSignalOptions>? configure = null)` binds and validates the options, and selects the signal through `UseDispatchSignal` with its own name. The core method `UseDispatchSignal(Func<IServiceProvider, IDispatchSignal>)` gets the optional parameter `string? setupMethod = null`, as the store methods have it (D62). So a second signal setup names both setup methods in the startup validation.
3. Add the startup checks of `UseNamedPipeDispatchSignal`:
   - `EnablePolling` false in the `DispatchOptions` of the inbox or of the outbox fails the startup validation with an `OptionsValidationException` (D190).
   - On Linux and macOS, .NET makes the pipe of a name the Unix domain socket `{Path.GetTempPath()}CoreFxPipe_{name}`. A path of a topic of the host that is too long for the platform fails the startup validation, and the message names the topic. `UnixDomainSocketEndPoint` checks the limit of the platform, for example 107 characters on Linux. The topics of a host are the inbox topic and the topic of each channel with a registered transport.
4. In `ExampleApp.Tests.Unit`, add a test with two hosts on one SQLite file and the named pipe signal. The test builds both hosts from the setup methods, and the example app does not change, as in D174. `ExampleApp.Tests.Unit` references the package.
5. Section 4.9 gets the choice of a host (D102, D188):
   - One process: the default `InProcessDispatchSignal`. `EnablePolling` can be false.
   - Separate processes of one OS user on one machine: each process calls `UseNamedPipeDispatchSignal` with the same prefix. `EnablePolling` stays true (D190), and `MaxPollInterval` can be longer, because the signal wakes the loops.
   - Separate machines, or separate OS users: no signal call. The loops find new messages by the poll and by the next due time.

Tests:
- The fallback wait of item 1. All times come from a `FakeTimeProvider`. A substitute signal returns `Errors.Unavailable` from each `WaitAsync`:
  - `RunAsync_SignalThatDoesNotWorkAndATimeGivenDuringTheFallbackWait_ClaimsAgainAtTheGivenTime` in `OutboxDispatcherUnitTests.RunAsync.cs`: `EnablePolling` false, `MaxDegreeOfParallelism` 1, and one claimed message with a next due time of 60 seconds, the lease end. The send of the transport waits for the test. The test waits for the timer of the fallback wait, and then lets the send give a transient error with a next try time 5 seconds later. When the fake time moves 5 seconds, the loop claims again, with `nowUtc` 5 seconds after the start. On the code of phase 20, the next claim comes only after 60 seconds, so the test fails first.
  - The same case in the inbox loop, with a `FailAsync` that has a next try time 5 seconds later.
  - The phase 2 tests `RunAsync_SignalThatDoesNotWork_WaitsOnePollIntervalAndClaimsAgain` and `RunAsync_SignalThatFailsAfterTheDueTime_ClaimsAgainAtOnce` stay unchanged, and pass.
- The use cases of `DispatchSignalTestsBase` run on the named pipe signal. The subclass `NamedPipeDispatchSignalUnitTests : DispatchSignalTestsBase<IDispatchSignal>` builds the signal with `UseNamedPipeDispatchSignal`, because the signal class is internal. Each test class uses its own random prefix.
- Two signals, each from its own DI container, with the same prefix. The pipe between them goes through the operating system, the same as between two processes. The tests wait for the pipe with real time and `TestHost.HangGuard`:
  - A notification of a channel topic in the first container completes a wait of that topic in the second container.
  - A notification of the inbox topic in the first container does not complete a wait of a channel topic in the second container.
  - A notification in the first container while no container serves the topic returns at once with no error. After the second container starts to wait, the next notification completes the wait.
  - After the second container is disposed, and a third container with the same prefix starts to wait for the topic, a notification of the first container completes the wait of the third container.
- The error path. The test holds the pipe name of a topic before the wait: on Windows with a server of one instance, and on Linux and macOS with a folder at the socket path. `WaitAsync` then returns `Errors.Unavailable` at once, and the signal logs one Warning with the topic. After the test releases the name, the next `WaitAsync` serves the pipe, and a notification of another container completes it.
- The setup and the startup checks:
  - The options tests of `NamedPipeSignalOptions`, with the rows of section 5.5: no prefix, an empty prefix, 40 and 41 characters, a `.`, and a `/`.
  - `EnablePolling` false in the options of the inbox, and in the options of the outbox, each fails the startup validation with an `OptionsValidationException`. Both true pass.
  - `UseNamedPipeDispatchSignal` after `UseDispatchSignal` fails the startup validation, and the message names both methods.
  - On Linux and macOS, a registered channel with a name of 100 characters fails the startup validation, and the message names its topic. On Windows, the test is skipped.
- In `ExampleApp.Tests.Unit`: a consumer host and a publisher host on one SQLite file, each with `UseNamedPipeDispatchSignal` and the same random prefix, and with `EnablePolling` true. The fake time does not move after the first claim of the publisher, so no poll and no due time wakes its loop. An event that the consumer processes reaches the `LogEmailClient` of the publisher. The test fails first when the publisher host does not call `UseNamedPipeDispatchSignal`.

Checked facts (2026-11-14) for the named pipe signal. The Windows probe ran on Windows 11 build 26300 with .NET 10.0.15. The Linux probe ran on .NET 10.0.12 in the local image `mcr.microsoft.com/dotnet/sdk:10.0`, with no pull. The probe is a console app in a scratch folder, outside the repository:

| Fact | Source |
|---|---|
| On Linux, .NET makes the named pipe `n2` the Unix domain socket `/tmp/CoreFxPipe_n2`, in `Path.GetTempPath()`. | The probe, and `ls -l`, which shows the type `s` |
| On Windows, two processes start servers of one pipe name with `MaxAllowedServerInstances`. Of 4 clients, 2 reached each process, and each client reached one server only. | The probe |
| On Linux, a second process that starts a server of the same name with no `FirstPipeInstance` takes the socket path. All 4 clients reached the second process. The first process got no connection and no error. | The probe |
| `FirstPipeInstance` while a server of the name exists gives `UnauthorizedAccessException`, on Windows and on Linux. | The probe |
| On Linux, a process that is killed with `kill -9` leaves its socket file. A client then gets a `TimeoutException` after its connect timeout. A new server with `FirstPipeInstance` gives `UnauthorizedAccessException`, and a new server with no `FirstPipeInstance` takes over the path. | The probe |
| A client with no server gets a `TimeoutException` after about its timeout: on Windows after 7 ms with a timeout of 0 ms and 63 ms with 50 ms, and on Linux after 32 ms and 54 ms. | The probe |
| On Windows, the default DACL of a pipe server gives read access (`FR`) to Everyone and to Anonymous, and full access to SYSTEM, to the Administrators, and to the owner. With `CurrentUserOnly`, only the owner has access. The .NET source makes `CurrentUserOnly` a DACL of the owner only, and a .NET test shows that a client of another user gets `UnauthorizedAccessException`. | The probe with `GetAccessControl`, and the dotnet/runtime source through Context7 |
| On Linux, the socket file has the mode `srwxr-xr-x` with and with no `CurrentUserOnly`, with the umask 022. A client that runs as the user `nobody` gets a `SocketException` "Permission denied" for both. A client of the same user connects. | The probe with `runuser -u nobody` |
| On Linux, a pipe path of 107 characters works, and 108 characters give an `ArgumentOutOfRangeException` from the Unix domain socket endpoint. `UnixDomainSocketEndPoint` reads the limit of the platform at run time on Unix. | The probe, and the dotnet/runtime source through Context7 |
| A held name stops a new server: on Windows, a server with `maxNumberOfServerInstances` 1 gives a second server `IOException` "All pipe instances are busy". On Linux, a folder at the socket path gives `SocketException` "Address already in use". After the release, a new server starts. | The probe |
| On macOS, the temp folder is different for each user. The probe did not run on macOS, so the path limit and the behavior of the pipes there are not checked. | The .NET document `docs/design/security/unix-tmp.md` through Context7 |
| `MsgHatch.Dispatch.NamedPipes.Tests.Unit` passed 25 of 25 tests on Linux with .NET 10.0.12, with no skipped test, so the path limit test ran there. On Windows, it passed 24 tests and skipped the path limit test. | A run in the local image `mcr.microsoft.com/dotnet/sdk:10.0` on a copy of the branch, with the NuGet cache of the host read-only, and a run on Windows |
| At the tip of the branch, the 19 unit test projects passed 3,379 tests, and skipped 1 test: the path limit test on Windows. The solution built with no errors and no warnings. | `dotnet test` of each `tests/*.Tests.Unit` project, and `dotnet build MsgHatch.slnx`, in the worktree |
| The line coverage of the unit tests is 99.3 %. `MsgHatch.Dispatch.NamedPipes` has 83.8 % on Windows. The check of the Unix path runs only on Linux and macOS. The restart after a server that fails after its start has no test, because no test can make a started server fail: on Linux, each new instance of a running server uses the socket that is already bound. | `pwsh ./coverage.ps1` on Windows |
| Each new test failed first. On the code of phase 20, both fallback wait tests timed out at the wait for the given time. The pipe tests failed when the server did not notify (4 of 4), when the writer did not connect again after a broken write (1 of 1), when the signal logged each failed start (1 of 1), and with no poll check of the outbox (the row `Outbox`). The example test failed when the publisher host did not call `UseNamedPipeDispatchSignal`. | A run with each change of the code, which was then restored |

Commits:
1. "Record the decisions of the dispatch signal between processes in the plan (D188 to D192)"
2. "Obey a time that a worker gives during the fallback wait of the dispatch loop"
3. "Add the named pipe dispatch signal"
4. "Test the named pipe signal between two example hosts"
5. "Record the commits and the checked facts of phase 21 in the plan"
6. "Record phase 21 as done in README and AGENTS.md"

Commit 2 holds item 1 with its tests and the new text of sections 4.6 and 4.9. Commit 3 holds items 2, 3, and 5: the new parameter of `UseDispatchSignal` comes with the package that uses it, and the commit adds the package to sections 4.2, 4.7, 4.9, 5.5, 6, and 7. Commit 4 holds item 4. Each commit builds and passes its tests on its own: commit 2 changes only `MsgHatch.Domain` and its tests, and commit 4 only adds a test and a project reference to `ExampleApp.Tests.Unit`. Before the push, a worktree at each commit runs `dotnet build` of the changed projects and `dotnet test` of their test projects. At the time of this plan, `MsgHatch.slnx` has uncommitted work of another session. Commit 3 adds two projects to it, so the work starts after that work is committed, or commit 3 stages only its own lines.

The implementation changed the plan in these points:
- Item 1 has no test for a time that a worker gives after the due time. `Give` already ignores such a time, and the loop always waits for the earliest time, so no change of the fix can make that test fail.
- The writer has no reconnect delay: each notification that finds no connection tries to connect, with a connect timeout of 100 ms. The server has no restart delay either, because the fallback wait of the loop already paces the next `WaitAsync`. So the signal needs no `TimeProvider`, and the error path test moves no fake time.
- Each connection starts with the 16-byte id of its signal instance, and a server closes a connection of its own instance. Without it, a writer that connects while its own process starts to serve the topic wakes its own process a second time, and the contract test of two notifications before two waits can fail.
- A write that fails connects one more time and writes again, so the first notification after a restart of the waiting process reaches it.
- The signal implements `IDisposable`, not `IAsyncDisposable`. `Dispose` closes each pipe handle at once, so the next notification of another process reaches a new waiting process, and not the old one.
- The tests of two containers and of the error path are in `NamedPipeDispatchSignalUnitTests`, the subclass of the contract tests. The example test is in `MsgHatchWorkerUnitTests`, next to the two-host test with a poll.
- The work ran in the worktree `.claude/worktrees/phase-21` on the branch `phase-21`, because `MsgHatch.slnx` and `Directory.Packages.props` of the main checkout had uncommitted work of another session.

Done when: the build has no errors and no warnings, all tests pass, and `MsgHatch.Dispatch.NamedPipes.Tests.Unit` also passes on Linux in the image `mcr.microsoft.com/dotnet/sdk:10.0`.

### Phase 22: Razor email templates

Goal: a host renders the html of an email with a Razor component, and takes the subject and the text part from the template store (D45, D193). The template stores read the template of a company from a company folder, for both engines (D199).

1. The company folder in the template stores (D199, D201). `TemplateFiles.BaseName` in `src/MsgHatch.Email/Templates/TemplateFiles.cs` gives the name of a template with a scope as `{Scope}/{MessageType}.{Language}` for `FileSystemTemplateStore`, and as `{Segment}.{MessageType}.{Language}` after the prefix for `EmbeddedResourceTemplateStore`. The segment is the scope, with `_` before a scope that starts with a digit. A scope outside the rule of D201 gives a miss with no read, so the selector uses the template with no scope (D123). The language stays escaped with `Uri.EscapeDataString`. The two test files `TestEvent.en.acme.*` of `tests/MsgHatch.Email.Tests.Unit/TemplateFiles` move to `TemplateFiles/acme/TestEvent.en.*`. No other template file of the repository has a scope. Section 4.10 gets the new names.
2. The parts of a placeholder template (D197). `MsgHatch.Email.Engines` gets the public enum `PlaceholderParts` with `All` and `SubjectAndText`, and `PlaceholderEmailEngine<TPayload>` gets the public constructor `(ITemplateStore? tenantStore, ITemplateStore? sharedStore, string defaultLanguage, PlaceholderParts parts)`:
   - With `SubjectAndText`, the parser requires `subject`, allows `text`, and gives `Errors.TemplateInvalid` for an `html` part. The prepared template then renders a null html body.
   - The cache of the parsed templates keeps the two modes apart.
   - The existing constructor keeps `All`. A default language that is the language of the request gives no fallback to another language. This is the existing rule of the template selector.
3. Add `src/MsgHatch.Email.Razor` (D196), which references `MsgHatch.Email` and `Microsoft.AspNetCore.Components.Web`:
   - `RazorEmailComponent<TPayload> : ComponentBase` with the parameter `[Parameter, EditorRequired] public TPayload Payload`.
   - `UseRazorEmailTemplates(this MsgHatchBuilder builder, Assembly assembly, string rootNamespace)` records the assembly and the root namespace of the templates (D198). A second call fails the startup validation, and the message names both calls (D62).
   - `AsRazorEmail(this PayloadBuilder<TEvent, TPayload> payload, string name, Action<ChannelBuilder<EmailMessage>>? configure = null)`, with `TPayload : IPlaceholderPayload<TPayload>`, calls `AsEmail` with one static engine factory. So the email registrations of one payload type share one engine, and the existing check of two engines for one payload type applies (section 4.7).
   - The startup validation with the role `Consumer`: an `AsRazorEmail` registration fails with no `UseRazorEmailTemplates`, and with no template store, as an email registration with the placeholder engine does.
4. The internal `RazorEmailEngine<TPayload>`, a tenant service of the key of the request:
   - `PrepareAsync` tries the language of the request, and then the default language (D194). In each language, it finds the component and the store template. Both found give the template. `Errors.NotFound` of one of them goes to the next language. Another error is the result. With no language left, the result is `Errors.NotFound`.
   - The component of a language is the class `{rootNamespace}.{Segment}.{MessageType}_{Language}_body` for the scope of the request, and else `{rootNamespace}.{MessageType}_{Language}_body` (D195, D198). The language has `_` for `-`, and the engine compares the class names with no case. The segment follows D201. A class of this name that does not inherit `RazorEmailComponent<TPayload>` gives `Errors.TemplateInvalid`.
   - The engine finds the classes with one scan of the assembly at its first prepare, and keeps them for the life of the process: each class in the root namespace, or in one namespace below it, that inherits `RazorEmailComponent<T>` for some `T`.
   - The store template of a language comes from a `PlaceholderEmailEngine<TPayload>` with the stores of the key, `SubjectAndText`, and that language as its default language. So it keeps the lookups of D123 in that language (D195).
   - `RenderAsync` renders the subject and the text with the placeholder template, and the html with a new `HtmlRenderer` for each render (D202).
5. Sections 4.2, 4.7, 4.10, 4.11, 5.5, and 7 of the design get the package. Section 4.10 replaces the paragraph "Razor as a later engine" with the engine. Section 4.11 gets the allocation of a Razor render, about 4.7 KB for each email, as a measured cost outside the zero allocation target. Section 7 loses the item of the Razor engine.

Tests:
- The store layout of item 1, in `TemplateStoreTestsBase` for both stores:
  - A template of the scope `acme` in the folder `acme` is found, and the old name `TestEvent.en.acme.subject.txt` is not found.
  - The scope `42` finds the folder `42` of the file store and the resource segment `_42` of the embedded store.
  - The scopes `acme-1`, `..`, and `_x` give a miss.
- The parts of item 2, in `PlaceholderEmailEngineUnitTests`:
  - With `SubjectAndText`: a subject and a text render with a null html body. A subject only renders with a null html body and a null text body. An `html` part gives 422. No subject gives 422.
  - A default language `hu` and a store with only `en` give 404 for a request in `hu`.
  - The existing rows of `All` stay unchanged and pass.
- The engine contract: `RazorEmailEngineUnitTests : EmailTemplateEngineTestsBase<IEmailTemplateEngine<OrderEmail>>` builds the engine with `UseRazorEmailTemplates` and `AsRazorEmail`, because the engine class is internal. A substitute store holds the subject and the text of each `Seed`, and the test project holds the html as components. The base class names the message type of each test after its test method, not with a new GUID, because a Razor template needs a fixed class name. Each test method still has a message type of its own.
- The rules of the Razor engine, in `RazorEmailEngineUnitTests`:
  - The company (D195, D198): 4 rows for the request of the company `acme`: an override of both parts, of only the subject, of only the body, and of no part. Each row asserts the source of each part.
  - The company id (D201): `42` finds the folder `42`, and `acme-1` uses the template with no company.
  - The language (D194): a component and a subject in `hu` give `hu`. A component in `hu` with no subject in `hu`, and a subject in `hu` with no component in `hu`, each give `en` for all parts. Nothing in `en` gives 404.
  - A store template of a Razor email with an `html` part gives 422.
  - A class with the name of a template and another payload type gives 422.
  - The culture of the caller is the same after a render in another culture.
  - A component that throws gives the exception to the caller.
- The setup: `AsRazorEmail` with no `UseRazorEmailTemplates` fails the startup validation. Two calls of `UseRazorEmailTemplates` fail, and the message names both. `AsEmail` with the placeholder engine and `AsRazorEmail` for one payload type fail. Two `AsRazorEmail` registrations of one payload type pass.
- Through the pipeline: `INotificationService.ProcessAsync` with a Razor registration stores an email with the subject of the store and the html of the component.
- The base class tests of encoding and culture prove the encoder and the culture of D202: `RenderAsync_AccentedCharacters_RenderUnchangedInEachPart` fails with `HtmlEncoder.Default`.

Checked facts (2026-11-14 and 2026-11-15) for the Razor engine. The probe is a console app in a scratch folder, outside the repository, on Windows 11 build 26300 with the .NET SDK 10.0.404, the runtime 10.0.15, and `Microsoft.AspNetCore.Components.Web` 10.0.14:

| Fact | Source |
|---|---|
| Runtime compilation is obsolete from .NET 10, and does not support Razor components. So a component cannot load at run time. | The ASP.NET Core documentation "Razor file compilation" through Context7 |
| A call of `HtmlRenderer.RenderComponentAsync` must run in `Dispatcher.InvokeAsync`. | The ASP.NET Core documentation "Render Razor components outside of ASP.NET Core" through Context7 |
| `HtmlRenderer` needs no DI scope. It renders with the root provider of a container with no registration, and with a `HtmlRenderer` that a DI scope gives. The sync dispose of that scope works. `HtmlRenderer` implements `IDisposable` and `IAsyncDisposable`. | The probe |
| A console host with only the package reference needs only the runtime `Microsoft.NETCore.App`, in its `runtimeconfig.json`. The package brings 8 packages of the ASP.NET Core 10.0.14 release, each with the license MIT: `Microsoft.AspNetCore.Components.Web`, `Components`, `Components.Forms`, `Components.Analyzers`, `Authorization`, `Metadata`, `Microsoft.JSInterop`, and `Microsoft.Extensions.Validation`. | The probe, `project.assets.json`, and the `.nuspec` of each package |
| `HtmlRenderer` encodes a value with the `HtmlEncoder` of its service provider. With no encoder, `Ö` becomes `&#xD6;`. With `HtmlEncoder.Create(UnicodeRanges.All)`, `Ö` stays, and `<script>` becomes `&lt;script&gt;`. Literal markup in a component stays unchanged, for example `ő`, `&amp;`, and `Tom & Jerry`. | The probe |
| A culture that the code sets in `Dispatcher.InvokeAsync` applies to the render, also after an `await` in a component. The culture of the caller is the same after the render. A culture that the caller sets before the call also applies to the render. | The probe |
| An exception of a component reaches the caller of `RenderComponentAsync` with its own type, for example `FormatException`. A missing payload gives `NullReferenceException`, a payload of another type or an unknown parameter gives `InvalidOperationException`, and the renderer still renders after these errors. | The probe |
| 200 parallel renders on one renderer give the correct culture in each result. | The probe |
| One shared renderer keeps about 2 KB for each render until it is disposed: 39 MB after 20,000 renders, and 117 MB after 60,000. A new renderer for each render allocates about 4.7 KB and a shared renderer about 3.8 KB to 4.0 KB, and each takes about 5 µs. | The probe, with `GC.GetTotalMemory` and `GC.GetTotalAllocatedBytes` |
| A parameter of a base class works: a component with `@inherits RazorEmailComponent<Pay>` gets `Payload`. | The probe |
| The Razor SDK makes a class name from a file name and a namespace from a folder: `A.hu.body.razor` gives `A_hu_body`, `A.en-US.body.razor` gives `A_en_US_body`, and `42/A.hu.body.razor` gives `_42.A_hu_body`. Folder names change: `acme-1` to `acme_1`, `1acme` to `_1acme`, and `a.b` to `a_b`. | The probe |
| A component name that starts with a lowercase letter stops the build with RZ10011, for example `orderShipped.en.body.razor` and `hu.razor`. | The probe |
| The folders `acme-1` and `acme_1` with one file name each stop the Razor source generator with CS8785, and then no component of the project compiles. | The probe |
| MSBuild names an embedded resource in a folder in the same way: `42/A.hu.title.txt` gives `{prefix}._42.A.hu.title.txt`, and `acme-1` gives `acme_1`. | The probe |
| In `MsgHatch.Email.Tests.Unit`, the default resource names of MSBuild put the company folders at the place that `ScopeFolders.SegmentOf` gives: the embedded store finds `TemplateFiles/42/TestEvent.en.subject.txt` for the scope `42`. | The row `digits` of `FindAsync_ScopeFolder_GivesTheSubjectOfTheFolderOfTheScopeOrNull` |
| `IServiceProviderIsKeyedService.IsKeyedService(typeof(ITemplateStore), TenantKey.None)` is true for a store that `UseTenantTemplateStore` registers with `KeyedService.AnyKey`. | The row `a template store of each tenant` of `Validate_CompleteRazorSetup_Succeeds`, which fails when the check leaves out the keyed store |
| In xUnit v3, `TestContext.Current.TestMethod` is set when the test class is constructed, so a property initializer of a base class can read the name of the test method. | The engine contract tests of both engines, which use it for their message type |
| A Razor component of the test project renders `<p>@Payload.Name</p>` as exactly `<p>Client 1</p>`, with no whitespace before or after it. | The engine contract tests of `RazorEmailEngineUnitTests` |
| The 22 unit test projects pass 3,464 tests and skip 1 test: the path limit test of the named pipe signal on Windows. `MsgHatch.Email.Razor.Tests.Unit` passes 37 tests, and `MsgHatch.Email.Tests.Unit` 208. The solution builds with no errors and no warnings. | `pwsh ./coverage.ps1`, which runs each `tests/*.Tests.Unit` project, and `dotnet build MsgHatch.slnx` |
| Each of the commits 2 to 5 builds on its own with no errors and no warnings, and passes its unit tests: `MsgHatch.Email.Tests.Unit` (198 tests at commit 2, 208 at commits 3 to 5), `MsgHatch.Email.AmazonSES.Tests.Unit` (69), `MsgHatch.Email.Resend.Tests.Unit` (68), `MsgHatch.Subscriptions.Tests.Unit` at commit 2 (365), and `MsgHatch.Email.Razor.Tests.Unit` at commit 5 (37). | `dotnet build MsgHatch.slnx` and `dotnet test` in a temporary worktree at each commit, outside the repository |
| The line coverage of the unit tests is 99.3 %. `MsgHatch.Email` has 100 %, and `MsgHatch.Email.Razor` 98.2 %. The 2 lines with no test in `RazorEmailTemplate` give an error of the subject render to the caller. The placeholder template never gives an error at render, so no test can reach them. | `pwsh ./coverage.ps1` |
| Each new test failed first, or failed with a break of the code that it covers. The store rows failed on the code of phase 21 and with no rule of D201. The parts rows failed with no check of the `html` part, with one cache for both modes, and with the default language ignored. Each Razor test failed with one of 22 breaks, for example with no encoder, no culture, no default language, a subject with its own fallback, no company component, no rule of D201, no payload type check, no tenant store, no cancellation check, an engine that catches the exception of a component, and with each check of the startup validation left out. | A run of the tests with each break, after which the code was restored |

Commits:
1. "Record the decisions of the Razor email templates in the plan (D193 to D202)"
2. "Read the template of a company from its folder in the template stores"
3. "Prepare a placeholder template with only a subject and a text part"
4. "Name the message type of each engine contract test after its test method"
5. "Add the Razor email template engine"
6. "Record the commits and the checked facts of phase 22 in the plan"
7. "Record phase 22 as done in README and AGENTS.md"

Commit 2 holds item 1 with its tests and the new text of section 4.10. Commit 3 holds item 2. Commit 4 changes only `EmailTemplateEngineTestsBase`, and the placeholder tests pass with it. Commit 5 holds items 3, 4, and 5, the new projects in `MsgHatch.slnx`, and the pinned version in `Directory.Packages.props`. Each commit builds and passes its tests on its own: commits 2, 3, and 4 change only `MsgHatch.Email` and its test projects. Before the end of the phase, a worktree at each commit runs `dotnet build` of the changed projects and `dotnet test` of their unit test projects.

The implementation changed the plan in these points:
- The engine compares the class names exactly, not with no case. With no case, the companies `acme` and `ACME` share one template, and D201 does not allow that.
- The scan takes each class of the assembly that inherits `RazorEmailComponent<T>`, with no filter for the namespace and for an abstract class. The engine looks up only names in the root namespace and one segment below it, so such a filter changes no result, and no test can make it fail.
- The render also sets the UI culture of the route, for a component that reads resources. The test of the culture of the caller covers both cultures.
- The embedded templates of `MsgHatch.Email.Tests.Unit` get the default resource names of MSBuild, with no `LogicalName`, because a `LogicalName` of only the file name loses the company folder. The test data of the file store copies the folder `TemplateFiles` from the output folder. The scope rows also have a folder `acme-1`, a template in the folder above the root folder, and the scope `acme/../42`, so that each row fails when the rule of D201 is missing.
- The file store test `FindAsync_ScopeWithPathCharacters_FindsTheFileWithTheEscapedScope` becomes `FindAsync_TemplateWithTheScopeInItsFileName_IsNotRead`, because a scope with path characters has no folder (D201).
- The test of the pipeline uses the embedded store of the test project, with the subject files next to the components in the folder `EmailTemplates`, as in the layout of the owner (D198).
- More tests: the template store of a tenant, a cancelled render, the arguments of `UseRazorEmailTemplates`, the arguments of the new `PlaceholderEmailEngine` constructor, and a publisher host with a Razor registration and no templates. The setup tests are in `RazorEmailExtensionsUnitTests`.

Done when: the build has no errors and no warnings, all unit tests pass, and `pwsh ./coverage.ps1` passes.

After the phase, the owner required that a host adds email templates at run time, with no build and no deployment. A Razor component cannot meet this (D204), so the owner removed the Razor engine:
- `03ea3a7` "Remove the Razor email template engine" reverts commit 5.
- `c473fe8` "Remove the subject and text parts mode of the placeholder engine" reverts commit 3.
- The company folders of the template stores (commit 2, D199, D201) and the message types of the engine contract tests (commit 4) stay.
- The Liquid engine of D203 is the first item of "Later phases".

Checked facts (2026-11-15) for the removal and for the choice of the engine:

| Fact | Source |
|---|---|
| The newest version of `Microsoft.AspNetCore.Razor.Language`, the stand-alone Razor compiler, is 6.0.36, for `netstandard2.0`. `Microsoft.AspNetCore.Mvc.Razor.RuntimeCompilation` 11.0.0 depends on `Microsoft.CodeAnalysis.Razor` 6.0.0. `RazorLight` 2.3.1 targets at most `net6.0`, and has a license file, not a license expression. | The NuGet flat container API, with the `.nuspec` of each version |
| `Fluid.Core` 2.41.1 has the license MIT and targets `net10.0`. `Scriban` 7.6.1 has BSD-2-Clause and targets at most `net8.0`. `Handlebars.Net` 2.4.3 has MIT and targets `net10.0`. | The NuGet flat container API, with the `.nuspec` of each version |
| Fluid renders with a `TextEncoder`, for example an `HtmlEncoder`, which encodes each value. The filter `raw` and the tag `raw` turn this off. `TemplateContext.CultureInfo` sets the culture of one render, and the default is the invariant culture. A member access strategy controls which members of a model a template reads. `TemplateContext.CancellationToken` stops a render in Fluid 3, but 2.41.1 does not have it (checked facts of phase 23). | The Fluid documentation through Context7 |
| After the two reverts, the solution builds with no errors and no warnings. `MsgHatch.Email.Tests.Unit` passes 198 tests, `MsgHatch.Email.AmazonSES.Tests.Unit` 69, and `MsgHatch.Email.Resend.Tests.Unit` 68. | `dotnet build MsgHatch.slnx` and `dotnet test` after each revert |

### Phase 23: Liquid email templates

Goal: a host adds an email template, a language, or a company override at run time, with no build and no deployment: an operator copies a Liquid file into the template store (D203). The Liquid engine renders all parts of an email from `ITemplateStore`, next to the placeholder engine (D205, D206).

1. Make the template selector of `MsgHatch.Email` public (D205). `TemplateSelector` in `src/MsgHatch.Email/Templates/TemplateSelector.cs` becomes `public sealed class TemplateSelector(ITemplateStore? tenantStore, ITemplateStore? sharedStore, string defaultLanguage)` with `FindAsync(EmailTemplateRequest, CancellationToken)`. The constructor gives an `ArgumentException` for a default language that is null, empty, or only whitespace. The placeholder engine keeps it, and its fallback tests stay.
2. Change the base class test `PrepareAsync_TemplateWithAnUnknownPlaceholderName_GivesTemplateInvalid` of `EmailTemplateEngineTestsBase` into a test of the first error of the prepare and the render: `RenderAsync_TemplateWithAnUnknownName_GivesTemplateInvalidAtPrepareOrRender`. The placeholder engine finds an unknown name at prepare (D5), and the Liquid engine at render (D207). Both then pass the same test.
3. Add `src/MsgHatch.Email.Liquid` (D205), which references `MsgHatch.Email` and `Fluid.Core`:
   - `AsLiquidEmail(this PayloadBuilder<TEvent, TPayload> payload, string name, Action<ChannelBuilder<EmailMessage>>? configure = null)` calls `AsEmail` with one static engine factory, so the Liquid registrations of one payload type share one engine, and the check of two engines for one payload type applies (D62, D206). A payload type needs no `[PlaceholderPayload]` (D209).
   - The package needs no `Use` method, because its templates come from the template store of the setup methods of `MsgHatch.Email`. With the role `Consumer`, an `AsLiquidEmail` registration with no template store fails the startup validation, as a placeholder registration does.
   - The internal `LiquidEmailEngine<TPayload>` is a tenant service. `PrepareAsync` finds the source with `TemplateSelector` and the stores of its key (D123, D199). It parses the parts `subject` and `html`, which are required, and the optional part `text`, one time for each `TemplateKey` (D210). A missing part and a parse error give `Errors.TemplateInvalid`.
   - `RenderAsync` renders each part with the payload as the model (D209): the html with `HtmlEncoder.Create(UnicodeRanges.All)`, and the subject and the text with `NullEncoder.Default`. Each render gets the culture of the route, the time zone UTC, the `now` of the `TimeProvider`, and a limit of 100,000 steps (D210).
   - An unknown name (D207), the step limit, and a `render` or `include` tag (D208) give `Errors.TemplateInvalid`. An exception of the payload code reaches the caller.
   - The options of Fluid for a payload type are built one time, with the registered member types of D209.
4. Sections 4.1, 4.2, 4.7, 4.10, 4.11, and 5.5 of the design get the package. Section 4.10 replaces the paragraph "An engine with loops and conditions" with the Liquid engine. Section 4.11 gets the allocation of a Liquid render, about 700 B, as a measured cost outside the zero allocation target.

Tests:
- The selector of item 1: `TemplateSelector` with a default language of null, an empty string, or only whitespace gives an `ArgumentException`. The fallback tests of D123 in `PlaceholderEmailEngineUnitTests` stay unchanged and pass.
- The base class test of item 2 passes for the placeholder engine before and after the change.
- The engine contract: `LiquidEmailEngineUnitTests : EmailTemplateEngineTestsBase<IEmailTemplateEngine<OrderEmail>>` builds the engine with `AsLiquidEmail`, because the engine class is internal. A substitute store holds each `Seed`. The test data writes `{Name}` as `{{ Name }}`, and `{ShippedAt}` as `{{ ShippedAt | format_date: 'G' }}`, because Liquid writes a date in its own format.
- The rules of the Liquid engine, in `LiquidEmailEngineUnitTests`:
  - A parse error gives 422 at prepare. A missing `subject` or `html` part gives 422.
  - An unknown name gives 422 at render, and a property with a null value in `{% if Note %}` renders the `else` branch (D207).
  - A loop over the lines of an order renders each line, with no registration in the host (D209).
  - A template with 200,000 steps gives 422 (D210). A template with `render` and one with `include` each give 422 (D208).
  - The filter `raw` turns the encoding of the html off, and the subject and the text are not encoded.
  - `{{ 'now' | date: '%Y-%m-%d' }}` renders the date of the `FakeTimeProvider`, `2030-01-01`.
  - A source with the key of a parsed template uses the parsed template, and a source with a new version is parsed again.
  - The engine of a tenant takes the template from the template store of the tenant, and the request of the company `42` takes the template of the folder `42` (D123, D199).
- The setup: `AsLiquidEmail` with no template store fails the startup validation, and with a shared store or a store of each tenant it passes. A publisher host with an `AsLiquidEmail` registration and no template store passes. `AsEmail` with the placeholder engine and `AsLiquidEmail` for one payload type fail. Two `AsLiquidEmail` registrations of one payload type pass.
- Through the pipeline: `INotificationService.ProcessAsync` with an `AsLiquidEmail` registration and the embedded Liquid templates of the test project stores an email for each route, with a loop and a condition in the html.

Checked facts (2026-11-15) for the Liquid engine. The probe is a console app in a scratch folder, outside the repository, on Windows 11 build 26300 with the .NET SDK 10.0.404 and `Fluid.Core` 2.41.1:

| Fact | Source |
|---|---|
| `Fluid.Core` 2.41.1 targets `net10.0`, and depends there on `Parlot` 1.5.9 (BSD-3-Clause), `TimeZoneConverter` 7.2.0 (MIT), and `Microsoft.Extensions.FileProviders.Abstractions` 10.0.0 (MIT). The newest version is the prerelease 3.0.0-beta.9. | The NuGet flat container API, with the `.nuspec` of each version |
| The Fluid documentation of Context7 describes Fluid 3, not 2.41.1. Version 2.41.1 has a mutable `TemplateOptions`, no `TemplateOptionsBuilder`, no `ITemplateFileProvider`, and no `TemplateContext.CancellationToken`. So the fact of phase 22 that `TemplateContext.CancellationToken` stops a render is not true for 2.41.1. Its `FileProvider` is the synchronous `IFileProvider`. | A build of the probe against 2.41.1, and the XML documentation of the package |
| With the payload as the model, `{{Name}}` and `{{ Name }}` render the property. `{{ name }}` renders an empty text, so member names are case-sensitive. | The probe |
| With `HtmlEncoder.Create(UnicodeRanges.All)`, `Ödön Űr <script>` renders as `Ödön Űr &lt;script&gt;`. Literal text of the template stays unchanged, also `&amp;`. The filter `raw` turns the encoding off. With `NullEncoder.Default`, no value is encoded. | The probe |
| A `decimal` renders in the culture of the context: `1234,5` in `hu-HU`. A `DateTime` renders as `2030-01-02 15:04:05Z` in each culture. `format_date: 'G'` renders the same text as `DateTime.ToString(culture)` in `hu-HU` and `en-US`. | The probe |
| An unknown name renders an empty text by default. With `StrictVariables`, it gives a `FluidException`, and a property with a null value in `{% if Note %}` also gives a `FluidException`. The `Undefined` callback of `TemplateOptions` runs for the unknown name `Nmae`, and not for the null property `Note`. | The probe |
| `FluidParser.TryParse` gives false and a message with the position for `Hello {{ Name `. | The probe |
| `TemplateContext(model, options)` lets a template read the members of the model type with no registration. With `allowModelMembers: false`, nothing renders. The element type of a list property needs `MemberAccessStrategy.Register`, or each member renders an empty text. | The probe |
| `MaxSteps` 1,000 stops a loop of 100,000 steps with an `InvalidOperationException`. | The probe |
| `render` and `include` read a partial from `TemplateOptions.FileProvider`, which gets the names `Footer` and then `Footer.liquid`. A missing partial gives a `FileNotFoundException` at render. | The probe |
| 200 parallel renders of one parsed template, each with its own `TemplateContext`, give the correct culture and value in each result. | The probe |
| A render of a short html template allocates about 700 B and takes about 3 µs. | The probe, with `GC.GetTotalAllocatedBytes` |
| `TemplateOptions.Now` sets the value of `now`. | The probe |
| The `Undefined` callback also runs for an unknown member of an object, for example `{{ line.Prodcut }}` in a loop, and for each member of an object whose type has no registration. A callback that throws a `FluidException` stops the render with that exception. | A second probe (2026-11-15, during the build), and the rows of `RenderAsync_LiquidInTheHtml_GivesTheRuleOfTheRender` |
| Fluid writes a `Uri` and a `Version` as a string, so a member of them renders an empty text, with or without a registration. A `TimeZoneInfo`, a `CultureInfo`, and a `Type` stay objects: with no registration, a member read runs `Undefined`, and with a registration it renders the member. | The second probe |
| The step limit throws an `InvalidOperationException`, and a `render` or `include` tag with the default `NullFileProvider` throws a `FileNotFoundException`. The code of the assembly `Fluid` throws both, so `Exception.TargetSite` is in that assembly. An exception of a getter of the model reaches the caller unchanged, with the getter as its `TargetSite`. | The second probe |
| The default `TemplateOptions` has the local time zone, `MaxSteps` 0 (no limit), and a `NullFileProvider`. `TemplateContext.Now` sets the value of `now` for one context. The time zone changes a date with no zone, for example `'2030-06-01 10:00' \| date: '%z'`, and not a `now` in UTC. | The second probe, and the break of the time zone |
| Two renders with one `TemplateContext` share the steps of the step limit: two loops of 60,000 steps stop at the limit of 100,000. Each render has its own scope, so a variable of `{% assign %}` in the first render is unknown in the second. | The second probe |
| An unknown filter, for example `{{ Name \| nofilter }}`, writes the value with no filter and no error. | The second probe |
| `HtmlEncoder.Create(UnicodeRanges.All)` writes `+` as `&#x2B;`. `{{ Lines.size }}` gives the number of items of a list. | The rows of `RenderAsync_LiquidInTheHtml_GivesTheRuleOfTheRender`, and the pipeline test |
| The 22 unit test projects pass 3,464 tests and skip 1 test: the path limit test of the named pipe signal on Windows. `MsgHatch.Email.Liquid.Tests.Unit` passes 44 tests, and `MsgHatch.Email.Tests.Unit` 201. The solution builds with no errors and no warnings. | `pwsh ./coverage.ps1`, which runs each `tests/*.Tests.Unit` project, and `dotnet build MsgHatch.slnx` |
| The line coverage of the unit tests is 99.3 %. `MsgHatch.Email` and `MsgHatch.Email.Liquid` each have 100 %. | `pwsh ./coverage.ps1` |
| Each of the commits 2 to 4 builds on its own with no errors and no warnings, and passes its unit tests: `MsgHatch.Email.Tests.Unit` (201), `MsgHatch.Email.AmazonSES.Tests.Unit` (69), and `MsgHatch.Email.Resend.Tests.Unit` (68) at commits 2 and 3, and `MsgHatch.Email.Liquid.Tests.Unit` (44) and `MsgHatch.Email.Tests.Unit` (201) at commit 4. | `dotnet build MsgHatch.slnx` and `dotnet test` in a temporary worktree at each commit, outside the repository |
| Each new test failed first, or failed with a break of the code that it covers. The selector test failed with no check of the default language. The base class test failed with no check of an unknown placeholder name. Each Liquid test failed with one of 25 breaks, for example with no `Undefined` callback, no step limit, a step limit of 99,000, the local time zone, no element types, registered runtime types, no html encoder, the culture of the caller, the system clock, no parse cache, no tenant store, and no check of a template store. | The break scripts, outside the repository |

Commits:
1. "Record the decisions of the Liquid email templates in the plan (D205 to D210)"
2. "Make the template selector public for other engines"
3. "Check an unknown name of an engine contract test at prepare or at render"
4. "Add the Liquid email template engine"
5. "Record the commits and the checked facts of phase 23 in the plan"
6. "Record phase 23 as done in README and AGENTS.md"

Commit 2 holds item 1, and commit 3 holds item 2. Each changes only `MsgHatch.Email` or its test project, and the placeholder tests pass with it. Commit 4 holds items 3 and 4, the new projects in `MsgHatch.slnx`, and the pinned version in `Directory.Packages.props`. Before the end of the phase, a worktree at each commit runs `dotnet build MsgHatch.slnx` and `dotnet test` of the changed unit test projects.

The implementation changed the plan in these points:
- Commit 3 also changes the doc comment of `EmailTemplateEngineTestsBase`, which still named the removed Razor engine.
- The `Undefined` callback throws a `FluidException`, a type of Fluid, so the engine adds no exception type (section 4.12). The engine gives `Errors.TemplateInvalid` for a `FluidException`, and for an exception whose `TargetSite` is in the assembly `Fluid`: the step limit and a partial. An exception of a getter of the payload has the getter as its `TargetSite`, so it reaches the caller.
- The three parts of an email render with one `TemplateContext`, so the limit of 100,000 steps applies to all parts of the email together. Each part still renders in its own scope.
- D209 also registers a struct that is not a primitive type or an enum. A type of the .NET runtime is a type in the namespace `System` or in a namespace below it.
- The test of a runtime type reads a `TimeZoneInfo` and a `CultureInfo`. Fluid writes a `Uri` and a `Version` as a string, so a test with them passes also when the engine registers runtime types.
- More tests: the default language through `AsLiquidEmail`, a variable of the subject in the html, `now` and a date with no zone in UTC, a loop of 99,990 steps under the limit, and the encoded value in the html.

Done when: the build has no errors and no warnings, all unit tests pass, and `pwsh ./coverage.ps1` passes.

After the build, the owner asked that an unknown filter of a Liquid template gives an error (D211). Phase 23 has no tag yet, so these commits are part of phase 23:

1. `LiquidOptions` in `src/MsgHatch.Email.Liquid/LiquidOptions.cs` sets `StrictFilters = true`. Fluid then throws a `FluidException` for an unknown filter, and `LiquidEmailTemplate` already gives `Errors.TemplateInvalid` for it.
2. Section 4.10 of the design replaces the sentence "An unknown filter is not an error" with the rule of D211.

Tests, as new rows of `RenderAsync_LiquidInTheHtml_GivesTheRuleOfTheRender`:
- `{{ Name | upcse }}` gives 422 at render.
- `{{ Name | Upcase }}` gives 422, because filter names are case-sensitive.
- `{% assign x = Name | upcse %}{{ x }}` gives 422.
- `{% if Note %}{{ Note | upcse }}{% endif %}` with no note renders an empty text, because the branch does not run.
- `{{ Name | upcase }}` renders the name in upper case, encoded, so a known filter still works.

Checked facts (2026-11-16) for unknown filters. The probe is a console app in a scratch folder, outside the repository, with `Fluid.Core` 2.41.1:

| Fact | Source |
|---|---|
| With `TemplateOptions.StrictFilters`, an unknown filter throws a `FluidException` with the message `Undefined filter 'nofilter'` at render, from the assembly `Fluid`. The XML documentation of the option names an `InvalidOperationException`, but the probe gets a `FluidException`. | The probe |
| A filter in a branch that does not run gives no error. An unknown filter in `{% assign %}` gives the error. Filter names are case-sensitive, so `Upcase` is unknown. | The probe |
| The default filter collection of `TemplateOptions` has 72 filters, among them `raw` and `format_date`. | The probe |
| `Fluid.Ast.AstVisitor` is public. It finds the filters of an output, `if`, `unless`, `for`, `case`, `assign`, and `capture`, but not of the `{% liquid %}` tag. Liquid allows no filter in the condition of `if`. | The probe |
| The 22 unit test projects pass 3,469 tests and skip 1 test: the path limit test of the named pipe signal on Windows. `MsgHatch.Email.Liquid.Tests.Unit` passes 49 tests. The solution builds with no errors and no warnings. The line coverage is 99.3 %, and `MsgHatch.Email.Liquid` has 100 %. | `pwsh ./coverage.ps1` and `dotnet build MsgHatch.slnx` at commit 8 |
| Commit 8 builds on its own with no errors and no warnings, and passes `MsgHatch.Email.Liquid.Tests.Unit` (49) and `MsgHatch.Email.Tests.Unit` (201). | `dotnet build MsgHatch.slnx` and `dotnet test` in a temporary worktree at commit 8, outside the repository |
| The 3 rows of an unknown filter failed first, and they fail again with no `StrictFilters`. The row of a known filter fails with an empty filter collection. The row of a branch that does not run passes before and after the change: it records the limit of D211, and no break of the engine makes it fail. | The break script, outside the repository |

Commits:
7. "Record the decision of unknown Liquid filters in the plan (D211)"
8. "Give 422 for an unknown filter of a Liquid template"
9. "Record the checked facts of the unknown Liquid filters in the plan"

Commit 7 holds D211, this plan, and the decision range of AGENTS.md. Commit 8 holds items 1 and 2 with the tests. Commit 9 records the test results, the coverage, and the check of commit 8 in a temporary worktree. Each new test fails first, or fails with a break of the code that it covers.

Done when: the build has no errors and no warnings, all unit tests pass, and `pwsh ./coverage.ps1` passes.

### Phase 24: Template library

Goal: a template library that knows no channel, as `MsgHatch.Subscriptions` knows no channel (D212). Each channel renders its texts through one engine contract that another implementation can replace (D213), and the email channel is a thin layer on the library (D215). No package is released yet, so the phase changes the public API of `MsgHatch.Email` with no compatibility types.

1. Add `src/MsgHatch.Templates` (D212). It references `MsgHatch.Domain`, and its package includes `MsgHatch.SourceGeneration` as an analyzer, as `MsgHatch.Email` does today:
   - The template code of `MsgHatch.Email` moves there, in the namespaces `MsgHatch.Templates`, `MsgHatch.Templates.Placeholders`, and `MsgHatch.Templates.DependencyInjection`: `ITemplateStore`, `TemplateLookup`, `TemplateSource`, `TemplateKey`, `TemplatePartKind`, `ScopeFolders`, the file and embedded stores with `FileTemplateOptions` and its validator, `TemplateStoreLoggingWrapper`, `TemplateSelector`, the placeholder parser, renderer, cache, and payload types, and their log events with their event ids.
   - The generator reads the attribute `MsgHatch.Templates.Placeholders.PlaceholderPayloadAttribute` and writes `global::MsgHatch.Templates.Placeholders`.
   - `UseTemplateStore`, `UseEmbeddedTemplates`, `UseFileTemplates`, and `UseTenantTemplateStore` move to `MsgHatch.Templates.DependencyInjection`, with their checks of D62. With the role `Consumer`, each registration of a template engine needs a template store.
2. Change the contract to one text for each template (D213, D214):
   - `TemplateRequest(MessageType Type, string Language, string? Scope, string Part, TemplatePartKind Kind)`. `TemplateLookup` gets the same part and kind, and `TemplateSource` becomes `TemplateSource(TemplateKey Key, string Text)`.
   - `ITemplateEngine<TPayload>.PrepareAsync(TemplateRequest, CancellationToken)` gives `ValueTask<ErrorOr<ITemplate<TPayload>>>`, or `Errors.NotFound` when no template exists. `ITemplate<TPayload>.RenderAsync(TPayload, CultureInfo, CancellationToken)` gives `ValueTask<ErrorOr<string>>`. A template of the kind `Html` encodes each value. The route is no longer an argument, because no engine reads it.
   - The file store reads only `{RootPath}/[{CompanyFolder}/]{MessageType}.{Language}.{Part}.txt` or `.html`, and the embedded store reads the resource of the same name. `TemplateKey` is the key of one file. `TemplateSelector` finds one part with the lookups of D123, so each part falls back on its own.
   - `PlaceholderTemplateEngine<TPayload>` parses one text for each `TemplateKey`. A cache hit of a prepare allocates nothing, and a render allocates only its output string (section 4.11). `TemplateEngineLoggingWrapper<TPayload>` replaces the email engine wrapper.
3. The email layer (D215, D216):
   - `MsgHatch.Email` references `MsgHatch.Templates`. The email composer prepares `subject` (`PlainText`), `body` (`Html`), and `body` (`PlainText`) one time for each group of routes, and renders the three parts of each email into `RenderedEmail`. `Errors.NotFound` of the text body gives no text body. Each other error is the error of the email.
   - `AsEmail(name)` keeps the placeholder engine. `AsEmail(name, ITemplateEngineFactory engine)` replaces the overload with a factory delegate. Two registrations of one payload type with two engines fail the startup validation, as today.
   - `IEmailTemplateEngine`, `IEmailTemplate`, `EmailTemplateRequest`, `PlaceholderEmailEngine`, and `PlaceholderEmailTemplate` are removed.
4. Move the Liquid engine (D216). `src/MsgHatch.Email.Liquid` becomes `src/MsgHatch.Templates.Liquid`, which references only `MsgHatch.Templates` and `Fluid.Core`. `LiquidTemplateEngine<TPayload>` renders one text with the rules of D207 to D211, so the step limit applies to each part. `LiquidTemplates.Engine` is its `ITemplateEngineFactory`. `AsLiquidEmail` and the Liquid setup check are removed, because the check of item 1 covers them.
5. The examples get the new namespaces. Sections 4.1, 4.2, 4.7, 4.10, 4.11, and 5.5 of the design get the library. Section 4.11 keeps the measured numbers of phase 13, with a note that a template is now one part.

Tests:
- `tests/MsgHatch.Templates.Tests.Unit` gets the tests of the stores, the selector, and the placeholder engine from `MsgHatch.Email.Tests.Unit`, with `TemplateStoreTestsBase<TStore>` and `TemplateEngineTestsBase<TEngine>`, which replaces `EmailTemplateEngineTestsBase` and checks one part. The fallback rows of D123 check one part. A company folder with only `subject.txt` gives the subject of the company and the body of the default folder (D214). A part with no translation takes the default language.
- `tests/MsgHatch.Templates.Liquid.Tests.Unit` replaces the Liquid test project: the engine contract, the rows of the Liquid rules, and `LiquidTemplates.Engine`.
- `tests/MsgHatch.Email.Tests.Unit` keeps the email layer: the three parts of an email, no text body for a missing text part, `Errors.NotFound` for a missing subject or html body, the error of one part as the error of the email, `AsEmail` with a factory, the startup check of two engines for one payload type, and a pipeline test with the Liquid templates of the test project.
- The generator tests use the new namespace. The template benchmarks move to `tests/MsgHatch.Templates.Tests.Performance` and build. They do not run in this phase.
- Each new test fails first, or fails with a break of the code that it covers. Each moved test passes before and after its move.

Checked facts (2026-11-16). The probes are console apps in a scratch folder, outside the repository, with the .NET SDK 10.0.404:

| Fact | Source |
|---|---|
| A generic method with a constraint cannot implement an interface method with no constraint: CS0425. So the placeholder engine, whose writer needs `IPlaceholderPayload<TPayload>`, cannot implement `ITemplateEngineFactory.Create<TPayload>`. | A probe |
| C# cannot infer the type argument of a generic method group from the return type of the delegate: `AsEmail("x", Liquid.Engine)` gives CS0411. So an engine is selected with a factory object, not with a generic method. | A probe |
| Outside `MsgHatch.Email`, the examples and `MsgHatch.Subscriptions.Tests.Unit` use `MsgHatch.Email.Placeholders` in 4 files and `UseEmbeddedTemplates` in 4 setups. The generator names `MsgHatch.Email.Placeholders` in 2 constants. `tests/MsgHatch.Email.Tests.Performance` has 5 files with template code. | A search of the repository |
| Each of the 9 breaks of the contract change fails the right test, for example a missing text part as an error, the html of a plain text route, one cache entry for both kinds, the file of each kind as `.txt`, a scoped lookup with no part, and the Liquid encoder for each kind. Each of the 2 breaks of the move fails the right test: no store check for an engine registration, and a selector with no tenant store. The 2 breaks of the render errors fail the new test. | The break scripts, outside the repository |
| Each of the commits `dcc5e0a`, `b52e686`, and `52ac7d5` builds on its own with no errors and no warnings, and passes its unit tests: at `dcc5e0a` `MsgHatch.Email.Tests.Unit` (219), `MsgHatch.Email.Liquid.Tests.Unit` (48), `MsgHatch.Subscriptions.Tests.Unit` (365), `MsgHatch.Email.AmazonSES.Tests.Unit` (69), and `MsgHatch.Email.Resend.Tests.Unit` (68); at `b52e686` `MsgHatch.Templates.Tests.Unit` (136), `MsgHatch.Email.Tests.Unit` (83), `MsgHatch.Email.Liquid.Tests.Unit` (48), and `MsgHatch.SourceGeneration.Tests.Unit` (24); at `52ac7d5` `MsgHatch.Templates.Liquid.Tests.Unit` (48), `MsgHatch.Templates.Tests.Unit` (136), and `MsgHatch.Email.Tests.Unit` (83). | `dotnet build MsgHatch.slnx` and `dotnet test` in a temporary worktree at each commit, outside the repository |
| The 23 unit test projects pass 3,489 tests and skip 1 test: the path limit test of the named pipe signal on Windows. `MsgHatch.Templates.Tests.Unit` passes 136 tests, `MsgHatch.Email.Tests.Unit` 86, and `MsgHatch.Templates.Liquid.Tests.Unit` 48. The solution builds with no errors and no warnings. The line coverage is 99.3 %, and `MsgHatch.Templates`, `MsgHatch.Templates.Liquid`, and `MsgHatch.Email` each have 100 %. | `pwsh ./coverage.ps1` and `dotnet build MsgHatch.slnx` at `e32aa14` |

Commits:
1. "Record the decisions of the template library in the plan (D212 to D217)"
2. "Move the template code of MsgHatch.Email into MsgHatch.Templates"
3. "Render one text for each template with a replaceable engine"
4. "Move the Liquid engine into MsgHatch.Templates.Liquid"
5. "Record the commits and the checked facts of phase 24 in the plan"
6. "Record phase 24 as done in README and AGENTS.md"

Commit 2 holds item 1 and its moved tests and benchmarks, and changes no behavior. Commit 3 holds items 2 and 3, and the Liquid engine of `MsgHatch.Email.Liquid` implements the new contract in it. Commit 4 holds item 4. Item 5 goes with the commit whose code it describes. Before the end of the phase, a worktree at each commit runs `dotnet build MsgHatch.slnx` and `dotnet test` of the changed unit test projects.

The implementation changed the plan in these points:
- The commits 2 and 3 change places. The contract change comes first, as `dcc5e0a` "Render one text for each template with a replaceable engine", because the placeholder engine implemented the email contract with internal parser types, so a move with no change of behavior was not possible. The move follows as `b52e686`, and the Liquid move as `52ac7d5`. A test commit `e32aa14` "Test the render error of each part of an email" follows, because the coverage showed that no test gave a render error of the html body or of the text body.
- The first try of the contract commit made a partial commit `9d51d31`, because `git add` stopped at a path that `git rm` had staged. That commit did not build. It was local and not pushed, so it was amended to `dcc5e0a`.
- `RenderedEmail` is removed, because the composer builds the `EmailMessage` from the three output strings.
- The library gives a channel the public method `AddTemplateEngine<TPayload>(registration, engine)`, which registers the engine of a payload type with its logging wrapper and its startup checks. `TemplateSelector.For(tenantServices, key)` and `PlaceholderTemplates.Engine<TPayload>` build an engine from a container with no channel. The email setup check `EmailSetup` moves into the library as `TemplateSetup`.
- With the role `Consumer`, each registration of an engine needs a template store, also with a custom engine factory (item 1). The test of a custom engine with no store now expects the error.
- The startup check of the Liquid package goes away in commit 3 already, because the check of `AddTemplateEngine` failed the same setup a second time.
- The tests of the template setup methods stay in `EmailExtensionsUnitTests`, because they check the methods with an email registration. The template benchmarks stay in `MsgHatch.Email.Tests.Performance`, because the benchmark of the email composer uses the same store and payload.
- The route is no longer an argument of a render, so the composer test of the route trace is removed. The engine tests of a missing part move to the email layer, and the Liquid row of a variable of the subject is removed, because each part is a template of its own.
- D214 shows in three tests: a Hungarian email with no Hungarian text part now gets the English text body (`NotificationServiceUnitTests`, `OutboxDispatcherUnitTests`, and the example P2 of `docs/subscription-domain.md`, whose text now says so).

Done when: the build has no errors and no warnings, all unit tests pass, and `pwsh ./coverage.ps1` passes.

After the phase, the owner asked for two more changes. The tag `phase-24` includes both commits:
- `5f969b0` "Share the log level rule of the wrappers from MsgHatch.Domain". `ErrorLogLevels` in `MsgHatch.Domain.Failures` is now public (section 4.15). The copies in `MsgHatch.Email`, `MsgHatch.Templates`, and `MsgHatch.Subscriptions` are removed. The private `LevelOf` of the broker client wrapper is removed too.
- `6878fa2` "Cache only the found sources in the file template store". A missing file has no read to skip, so the file store does not cache a miss (section 4.10). The contract test of a missing template now also checks that the result is not an error, because `ErrorOr.Value` gives null for an error too.
- After both commits, 3,492 unit tests pass, 1 is skipped, and the line coverage is 99.3 %.

### Phase 25: HTTP API of the templates

Goal: an operator lists, reads, writes, and deletes the templates of a store over HTTP, with no build, no deployment, and no access to the folders of the host (D217). The engine of the payload type checks each written text before the store keeps it, so a bad text gives 422 and the store keeps the old text (D219). `MsgHatch.Templates.AspNetCore` maps the endpoints, as `MsgHatch.Subscriptions.AspNetCore` does for the subscriptions. No package is released yet, so the phase changes the public API of `MsgHatch.Templates` with no compatibility types.

1. Check a template text (D219):
   - `ITemplateEngine<TPayload>` gets `ValueTask<Error?> CheckAsync(TemplateLookup lookup, string text, CancellationToken ct)`. It checks the text of one part as `PrepareAsync` does after a store read, with no store read and no cache entry. A bad text gives `Errors.TemplateInvalid`.
   - `PlaceholderTemplateEngine<TPayload>` parses and binds the text with `TemplateParser`, so a syntax error and an unknown placeholder give the error (D5).
   - `LiquidTemplateEngine<TPayload>` parses the text, and walks the parsed tree with `Fluid.Ast.AstVisitor`. A filter that the filter collection of `LiquidOptions<TPayload>` does not have (D211), and a `render` or `include` tag (D208), give the error. An unknown name stays an error at render (D207), because the check has no payload. A filter or a tag in a `{% liquid %}` tag stays an error at render, because the visitor does not enter that tag (checked facts of phase 23).
   - `TemplateEngineLoggingWrapper<TPayload>` logs each check with the next event ids of the area 2000 to 2099. A check that passes logs at the Debug level. The test engines of the email tests implement the new method.
2. Write the templates of a file template store (D218, D223):
   - `ITemplateWriter` in `MsgHatch.Templates`: `ListAsync(TemplateQuery query, CancellationToken ct)` gives `ErrorOr<IReadOnlyList<TemplateEntry>>`, `SaveAsync(TemplateLookup lookup, string text, long expectedVersion, CancellationToken ct)` gives `ErrorOr<TemplateEntry>`, and `DeleteAsync(TemplateLookup lookup, CancellationToken ct)` gives `Error?`. `TemplateEntry(TemplateLookup Lookup, long Version)`. `TemplateQuery(MessageType? Type = null, string? Scope = null)`, where a null value matches each value.
   - A store is writable when it implements `ITemplateWriter`. `FileSystemTemplateStore` implements it. `EmbeddedResourceTemplateStore` does not. For a writable store, the logging wrapper also implements `ITemplateWriter`. It logs each save and each delete at the Information level, and each list at the Debug level, never with the text.
   - A save writes the file of `TemplateFiles.PathOf`. So the company folder comes from `ScopeFolders.FolderOf` (D201), and no request can leave the template folder. The store creates a missing company folder. It writes the text into a temporary file in the same folder, and moves that file over the file of the lookup.
   - The version is the last write time of the file in ticks. When it is not later than the version before the save, the store sets it one tick after that version. So each save gives a new `TemplateKey`, and the parse caches of both engines parse the new text.
   - `expectedVersion` is the version that the caller read, or 0 for a new file. Another version gives `Errors.Conflict` (D79). One write lock for each store makes the check of the version and the move atomic in one process. The lock has the comment `ponytail: one write lock for each store, and two processes on one folder can race between the check and the move; a lock file if two operators write one part at the same time`.
   - `DeleteAsync` deletes the file. A missing file is not an error.
   - `ListAsync` reads the root folder and each company folder, or only the folder of `query.Scope`. It gives one entry for each file whose name `TemplateFiles` makes. The new public method `ScopeFolders.ScopeOf(folder)` gives the scope of a folder name, the reverse of `FolderOf`. The store skips each folder and each file whose name the library does not make from its own parts, for example the temporary file of a save, or a folder `Acme` with a capital letter.
3. Manage the templates of a tenant (D220, D222):
   - `PayloadBuilder<TEvent, TPayload>` gets the public property `MessageType Type`.
   - `AddTemplateEngine` becomes an extension method of `PayloadBuilder<TEvent, TPayload>`: `AddTemplateEngine(string registration, IReadOnlyList<TemplatePart> parts, Func<IServiceProvider, TenantKey, ITemplateEngine<TPayload>> engine)`, with `TemplatePart(string Name, TemplatePartKind Kind)`. A part list that is empty, or a part name that is not a name of ASCII letters, digits, and `_` that starts with a letter, gives an `ArgumentException`. The email layer declares `subject` (`PlainText`), `body` (`Html`), and `body` (`PlainText`).
   - `ITemplateManager` in `MsgHatch.Templates.Management` is a tenant service. Each template setup method and `AddTemplateEngine` registers it one time. The manager of `TenantKey.None` uses the shared store. The manager of a tenant uses only the template store of that tenant. Its methods:
     - `GetTemplatesAsync(TemplateQuery query, CancellationToken ct)` calls `ITemplateWriter.ListAsync`.
     - `GetTemplateAsync(TemplateLookup lookup, CancellationToken ct)` calls `ITemplateStore.FindAsync` with the exact lookup, with no fallback, so it works also on the embedded store. A miss gives `Errors.NotFound`.
     - `SaveTemplateAsync(TemplateLookup lookup, string text, long expectedVersion, CancellationToken ct)` validates the request, checks the text with the engine of each payload type that has a registration of the message type, and then calls `ITemplateWriter.SaveAsync`.
     - `DeleteTemplateAsync(TemplateLookup lookup, CancellationToken ct)` validates the lookup, and calls `ITemplateWriter.DeleteAsync`. It accepts each part, also a part that no registration declares (D220).
   - Validation gives `Errors.ValidationFailed`, and calls no store, in these cases:
     - The language is not a culture that `CultureInfo.GetCultureInfo` knows, as for a subscription (section 4.17).
     - The scope is not a valid `CompanyId`, or its folder name has more than 255 characters.
     - The part is not a name.
     - A save has a part and a kind that no registration of the message type declares (D220).
     - A save has a text with more than 1,000,000 characters.
     - The store of the key is not an `ITemplateWriter`, for a list, a save, or a delete.
   - A save of a message type with no registration of a template engine gives `Errors.NotFound`. A key with no template store gives `Errors.NotFound`.
   - The manager has no logging wrapper of its own: the store wrapper logs each change, and the engine wrapper logs each check.
4. Add `src/MsgHatch.Templates.AspNetCore` (D224). It references `MsgHatch.Templates` and the framework `Microsoft.AspNetCore.App`. `MapMsgHatchTemplates(this IEndpointRouteBuilder routes, string prefix = "/templates")` maps these endpoints, and returns the `RouteGroupBuilder`. The library adds no authorization.

   | Method | Path | Body | Success |
   |---|---|---|---|
   | GET | `{prefix}?messageType=&companyId=` | none | 200 with the entries: `messageType`, `language`, `companyId`, `part`, `kind`, and `version` |
   | GET | `{prefix}/{messageType}/{language}/{part}/{kind}?companyId=` | none | 200 with the entry and its `text` |
   | PUT | `{prefix}/{messageType}/{language}/{part}/{kind}?companyId=` | `text` and `version` | 200 with the saved entry |
   | DELETE | `{prefix}/{messageType}/{language}/{part}/{kind}?companyId=` | none | 204 |

   - Each route takes the optional query parameter `tenantId`. The endpoint resolves `ITemplateManager` with `TenantKey.For(tenantId)`, and with `TenantKey.None` when the parameter is missing (D126).
   - `kind` is `PlainText` or `Html`. An invalid message type, part, kind, company id, or tenant id gives 400, and calls no manager.
   - The endpoints read and write each body with their own source-generated `TemplateEndpointJson`: camelCase names, enum values as strings, and the required properties `text` and `version`. A `PUT` with a content type that is not JSON gives 415 (D156). Invalid JSON and a missing required property give 400.
   - `Errors.ValidationFailed` gives 400, `Errors.NotFound` gives 404, `Errors.Conflict` gives 409, `Errors.TemplateInvalid` gives 422, and each other error gives 500. The body is a `ProblemDetails` with the error name as the title and the error message as the detail.
   - A response never has the template id, because the id of the file store is a path on the host.
5. The example app (D221, D225):
   - The build copies `examples/ExampleApp/Templates` to the output folder, in place of the embedded resources. The app selects `UseFileTemplates` on the folder `Templates` of `AppContext.BaseDirectory`.
   - The app maps `MapMsgHatchTemplates("/templates")` with the policy `TemplateAdmin`, which needs the role of its name.
   - `tests/ExampleApp.Tests.Unit` copies the templates into its temporary folder, so a test write never changes the build output.
6. Sections 4.2, 4.7, 4.10, 4.15, 4.16, 5.1, and 5.5 of the design get the writer, the check, the manager, and the package. A new section 4.22 "Template administration" describes the manager and the endpoints, as section 4.20 does for the registry.

Tests:
- The check, in `TemplateEngineTestsBase<TEngine>`: a valid text gives null, and a text with a syntax error gives 422. The placeholder engine: an unknown placeholder gives 422, and a check reads no store.
- The Liquid check, in `LiquidTemplateEngineUnitTests`: `{{ Name | upcse }}`, `{{ Name | Upcase }}`, `{% assign x = Name | upcse %}`, `{% render 'Footer' %}`, and `{% include 'Footer' %}` give 422. `{{ Name | upcase }}` gives null. `{{ Nmae }}` and `{% liquid echo Name | upcse %}` give null, and the next render of them gives 422, so the tests record the limits of D219.
- The logging wrapper logs a check that passes at the Debug level, and a failed check with the error code, and never the text.
- The writer contract, in `TemplateWriterTestsBase<TStore>`, which `FileSystemTemplateStoreWriterUnitTests` builds on a temporary folder:
  - A save with the version 0 makes the file, and `FindAsync` then gives the text with the new version.
  - A save with an old version, and a save with the version 0 of an existing file, give 409 and do not change the file.
  - A save after the last write time of the file is set one hour into the future gives a version later than that time.
  - Ten parallel saves with the same expected version give one entry and nine conflicts.
  - A delete removes the file, and a delete of a missing file gives null.
  - The list gives the entries of the root folder and of the company folders, filters by message type and by scope, and skips other files, the temporary file of a save, and a folder `Acme`.
  - The company ids `ACME`, `acme-1`, `..`, and a GUID write into their escaped folders, and no file is written outside the root folder.
- `ScopeFolders.ScopeOf` gives back each scope of the rows of `FolderOf`, and null for a name that `FolderOf` does not make.
- The setup: the shared file store and a file store of each tenant resolve as an `ITemplateWriter` with and without logging. The embedded store does not. `AddTemplateEngine` with no parts, or with a part name that is not a name, gives an `ArgumentException`.
- The manager, in `TemplateManagerUnitTests`, with a file store on a temporary folder and the placeholder engine:
  - A save gives the entry, and the next prepare of the engine renders the new text.
  - A save with an unknown placeholder gives 422, and writes no file.
  - A save of a part or a kind that no registration declares gives 400. A delete of such a part removes the file.
  - A save of a message type with no registration gives 404.
  - An unknown language, an invalid company id, a company id whose folder name is too long, and a text of 1,000,001 characters each give 400.
  - A list, a save, and a delete on the embedded store give 400, and a read of an embedded template gives its text.
  - The manager of a tenant writes into the store of the tenant, and not into the shared store. A tenant with no template store gives 404.
- The Liquid manager check, in `MsgHatch.Templates.Liquid.Tests.Unit`: a save of a text with an unknown filter gives 422, and writes no file.
- The endpoints, in `tests/MsgHatch.Templates.AspNetCore.Tests.Unit`, on a `TestServer` with an `ITemplateManager` substitute for each key:
  - Each route calls the matching manager method one time, with the lookup of the path and the query.
  - `tenantId` selects the manager of the tenant.
  - A `PUT` with `text/plain` gives 415. Invalid JSON and a missing `version` give 400.
  - An invalid path value or query value gives 400, and calls no manager.
  - Each error type gives its status code and a `ProblemDetails` with the error name.
  - The JSON of a list, a read, and a save has the exact field names and enum values, and no template id.
- The example app: a token with no role `TemplateAdmin` gives 403. With the role, a `PUT` of a template followed by a `GET` gives the new text.
- Each new test fails first, or fails with a break of the code that it covers. Each changed test passes before and after its change.

Checked facts (2026-11-17). The probes are console apps in a scratch folder, outside the repository, on Windows 11 build 26300 with the .NET SDK 10.0.404 and `Fluid.Core` 2.41.1:

| Fact | Source |
|---|---|
| `Fluid.Ast.AstVisitor` has the protected virtual methods `VisitFilterExpression`, `VisitRenderStatement`, and `VisitIncludeStatement`, and the public `VisitTemplate(IFluidTemplate)`. `FilterExpression.Name` gives the name of a filter, and `FilterCollection.TryGetValue` finds a filter of the options. | A probe, with reflection on the assembly `Fluid` |
| The walk finds a filter in an output, an `assign`, a branch of `if` that does not run, `unless`, `for`, `capture`, `case`, `echo`, and the second filter of a chain. It finds no filter in `raw` and `comment`, and none in a `{% liquid %}` tag. Liquid allows no filter in the condition of `if`, of `for`, or of `cycle`: each one is a parse error. | The probe, and the rows of `CheckAsync_LiquidInAnHtmlPart_GivesTheRuleOfTheCheck` |
| With the default `FluidParser`, a `{% liquid %}` tag runs no statement: `{% liquid echo Name %}` and `{% liquid render 'Footer' %}` render an empty text with no error. A tag of several lines with `assign` and a filter is a parse error. | A second probe, and the rows of `RenderAsync_LiquidInAnHtmlPart_GivesTheRuleOfTheRender` |
| `CultureInfo.GetCultureInfo(name, predefinedOnly: true)` gives the invariant culture for an empty text, and the names `hu`, `hu-HU`, and `en-US` for `HU`, `hu-hu`, and `en-US-x-test`. It throws `CultureNotFoundException` for `xx`, `xx-ZZ`, `hu-HUU`, and `iv`. With no `predefinedOnly`, `xx` and `xx-ZZ` are cultures. | A probe |
| `File.Move(source, target, overwrite: true)` replaces a file, and the file then has the last write time of the moved file. `File.SetLastWriteTimeUtc` sets the exact ticks. After a time one hour in the future, a move gives an earlier time, so the version needs the rule of one tick after the version before the save. | A probe, and `SaveAsync_FileWithALastWriteTimeInTheFuture_GivesTheVersionOneTickAfterThatTime` |
| On Windows, a move over a file that a reader holds open with `FileShare.Read`, also with `FileShare.Delete`, throws `UnauthorizedAccessException`. | A probe, and `SaveAsync_FileThatAReaderHoldsOpen_GivesUnavailableAndLeavesNoTemporaryFile` |
| `ScopeFolders.FolderOf` writes the hex digits of an escape as capital letters, so it writes them with `^`: `../outside` has the folder `%2E%2E%2^foutside`. `ScopeOf` gives back each scope. | The rows of `SaveAsync_CompanyId_WritesIntoItsEscapedFolderInsideTheRootFolder` and `ScopeFoldersUnitTests` |
| On Windows, `Directory.CreateDirectory("Acme")` gives the existing folder `acme`. | The first run of `ListAsync_FolderThatNoCompanyHas_IsSkipped` |
| MSBuild copies the `None` items with `CopyToOutputDirectory` of the referenced `ExampleApp` into the output folder of `ExampleApp.Tests.Unit`. | The build output of the test project |
| Each of the commits `7b33ee8`, `9e9b890`, `321dd82`, `719bdd5`, `52f7687`, and `a7ed8cf` builds on its own with no errors and no warnings, and passes its unit tests: at `7b33ee8` `MsgHatch.Templates.Tests.Unit` (167), `MsgHatch.Templates.Liquid.Tests.Unit` (73), and `MsgHatch.Email.Tests.Unit` (87); at `9e9b890` `MsgHatch.Templates.Tests.Unit` (234); at `321dd82` `MsgHatch.Templates.Tests.Unit` (280), `MsgHatch.Templates.Liquid.Tests.Unit` (77), `MsgHatch.Email.Tests.Unit` (92), and `MsgHatch.Domain.Tests.Unit` (1,449); at `719bdd5` `MsgHatch.Templates.AspNetCore.Tests.Unit` (46); at `52f7687` `ExampleApp.Tests.Unit` (39); at `a7ed8cf` `MsgHatch.Templates.Tests.Unit` (283). | `dotnet build MsgHatch.slnx` and `dotnet test` in a temporary worktree at each commit, outside the repository |
| The 24 unit test projects pass 3,724 tests and skip 1 test: the path limit test of the named pipe signal on Windows. `MsgHatch.Templates.Tests.Unit` passes 283 tests, `MsgHatch.Templates.Liquid.Tests.Unit` 77, `MsgHatch.Templates.AspNetCore.Tests.Unit` 46, `MsgHatch.Email.Tests.Unit` 92, and `ExampleApp.Tests.Unit` 39. The solution builds with no errors and no warnings. The line coverage is 99.3 %. `MsgHatch.Templates` has 99.5 %, and `MsgHatch.Templates.AspNetCore`, `MsgHatch.Templates.Liquid`, and `MsgHatch.Email` each have 100 %. | `pwsh ./coverage.ps1` and `dotnet build MsgHatch.slnx` at `a7ed8cf` |
| Each new test failed first, or failed with one of 76 breaks of the code that it covers: 11 breaks of the check, 23 of the writer, the scope folders, and the file names, 25 of the manager, the setup, and the email parts, 12 of the endpoints, 2 of the example app, and 3 of the test commit. For example: no tree walk, an empty filter collection, no version check, no write lock, no tick after the version, no round trip of a folder name, no check of a part, the shared store for a tenant, the engine of `TenantKey.None` for a tenant, no 422, no 415, a kind with no case, and no template policy. The break of the configured template folder of the example wrote its text into the build output of the test project. The file was deleted, and the build copied it again. | The break scripts, outside the repository |

Commits:
1. "Record the decisions of the template HTTP API in the plan (D218 to D225)"
2. "Check a template text with the engine of its payload type"
3. "Write the templates of a file template store"
4. "Manage the templates of a tenant with a check before each write"
5. "Add the HTTP endpoints of the templates"
6. "Use a file template store and the template endpoints in the example app"
7. "Record the commits and the checked facts of phase 25 in the plan"
8. "Record phase 25 as done in README and AGENTS.md"

Commit 2 holds item 1, commit 3 item 2, commit 4 item 3, commit 5 item 4 with the new projects in `MsgHatch.slnx`, and commit 6 item 5. Item 6 goes with the commit whose code it describes. Before the end of the phase, a worktree at each of the commits 2 to 6 runs `dotnet build MsgHatch.slnx` and `dotnet test` of the changed unit test projects. The build first checks `Fluid.Ast.AstVisitor`, the move of a file over another file, and the change of a last write time with probes in a scratch folder, outside the repository.

The implementation changed the plan in these points:
- Fluid 2.41.1 runs no statement of a `{% liquid %}` tag with the parser of the engine: also `{% liquid echo Name %}` renders an empty text. So the plan sentence "a filter or a tag in a `{% liquid %}` tag stays an error at render" was not true. Commit 2 changes D219 and section 4.10, and the rows of the Liquid tests record that such a tag renders an empty text, also with an unknown filter or a `render` tag in it.
- The engine wrapper logs a rejected text (`Errors.TemplateInvalid` of `CheckAsync`) at the Information level, because it is the input of an operator, not a fault of the system. Section 4.15 has this as a second exception of the log level rule.
- The manager checks the language more strictly than a subscription: the language is not empty, and is the name of its predefined culture. `CultureInfo.GetCultureInfo(name, predefinedOnly: true)` gives the invariant culture for an empty text, and accepts `HU`, `hu-hu`, and `en-US-x-test` with the names `hu`, `hu-HU`, and `en-US`. A send looks for the file of the language as written, so these spellings give 400.
- A save checks the text only with the engines of the registrations that declare the part and the kind, not with each engine of the message type. An engine of another part never renders that text.
- The endpoints check only the values that need a type: the message type, the kind, and the tenant id. The manager checks the language, the part, and the company id, so an invalid part or company id gives 400 from the manager, and each rule has one place.
- A list of the file store also skips a file name with an empty language or an empty part.
- The email channel holds its part names and its part list in `EmailChannel`, and the composer uses the same constants, so the parts of an email are in one place.
- `AddTemplateEngine` moved to the payload builder, so `TemplateEngineLoggingWrapperUnitTests` and `EngineHost` register the engine with the new test helper `AddOrderEmailEngine`, which adds a message type with a substitute loader.
- The test of ten parallel saves starts each save with `Task.Run`, because a save of the file store completes synchronously, so `Task.WhenAll` alone ran them one after another. With no write lock, the test then fails.
- The test of a folder that no company has uses the folder `Initech`. On Windows, `Directory.CreateDirectory("Acme")` gives the folder `acme` of another test of the class, because the file system ignores the case of a name.
- The other example tests read the templates of the build output of `ExampleApp.Tests.Unit`, where MSBuild copies the `Templates` folder of the referenced example app. Only the tests that write a template use a copy in a temporary folder.
- More tests: the Liquid manager test with a parse error, the email parts test in `EmailExtensionsUnitTests`, and the one-time registration of the manager for each template setup method.
- The row of `TemplateWriterTestsBase` in section 5.5 went with commit 4, not commit 3.
- Section 5.1 gets no line: its tree has no project of the phases 21 to 24, so one line of phase 25 alone would not make it complete. A later change can bring the tree up to date.
- A test commit `a7ed8cf` "Test a blocked delete, a read with an empty scope, and a store error of the template manager" follows commit 6, because the coverage showed three paths with no test. The catch of an I/O error in the list of the file store still has no test: a unit test cannot make a folder that the operating system refuses to list on each platform.

Done when: the build has no errors and no warnings, all unit tests pass, and `pwsh ./coverage.ps1` passes.

### Phase 26: query and command interfaces

Goal: each management API gets a query interface and a command interface (D231), one error contract (D233), and stateless rules that run first (D234). The HTTP bodies move into contracts packages (D227, D228), an error type gets its identity from its code (D238), and the packages of the API boundary become AOT-compatible (D237). So the HTTP clients of phase 27 need no change of a core package. No package is released yet, so the phase changes the public API with no compatibility types. The first two commits of this phase hold the plan of phases 27 and 28.

1. Split the subscription manager (D231):
   - `ISubscriptionQueries` and `ISubscriptionCommands` in `MsgHatch.Subscriptions.Management` get the methods of `ISubscriptionManager`, with the same signatures and documentation. `ISubscriptionManager` goes away.
   - `SubscriptionManager` implements both interfaces. The logging wrapper becomes one wrapper for each interface, with the same event ids.
   - The endpoints of `MsgHatch.Subscriptions.AspNetCore` use the queries for a read. An update and a delete of a `/me` route also use the queries, because they read the owner first (D154).
   - `ExampleApp`, `WolverineExample.Consumer`, and the tests use the new interfaces.
2. Split the template manager (D231): `ITemplateQueries` and `ITemplateCommands` in `MsgHatch.Templates.Management` get the methods of `ITemplateManager`. `ITemplateManager` goes away, `TemplateManager` implements both interfaces, and the template endpoints use them.
3. Give the template queries the parts and the check of a draft (D240, D241):
   - Check first, before the code: the parse error of Fluid 2.41.1 has a line and a column that the engine can read with no parse of the message text.
   - `ITemplateEngineFactory` gets `string Name`: `placeholder` for the placeholder engine, and `liquid` for `LiquidTemplates.Engine`.
   - `ITemplateEngine<TPayload>.CheckAsync` gives `ErrorOr<IReadOnlyList<TemplateIssue>>`. The placeholder engine and the Liquid engine give one issue for each problem that they find today, and the Liquid engine adds the line and the column of a parse error.
   - `ITemplateQueries` gets `GetTemplatePartsAsync(MessageType? type, CancellationToken ct)`, which gives `TemplatePartInfo(MessageType Type, TemplatePart Part, IReadOnlyList<string> Engines)` for each declared part (D220), and `CheckTemplateAsync`. A save with an issue gives `Errors.TemplateInvalid` with the message of the first issue, as today.
   - The template endpoints map `GET {prefix}/parts` and `POST {prefix}/{messageType}/{language}/{part}/{kind}/check`.
4. Select an implementation of each interface (D231):
   - `UseSubscriptions` registers the in-process `ISubscriptionQueries`, `ISubscriptionCommands`, and `IRoutingVerifier` as the defaults. Each template setup method and `AddTemplateEngine` register the in-process `ITemplateQueries` and `ITemplateCommands` as the defaults, one time.
   - `UseSubscriptionQueries`, `UseSubscriptionCommands`, `UseRoutingVerifier`, `UseTemplateQueries`, and `UseTemplateCommands` take a factory `Func<IServiceProvider, TenantKey, TService>`. Each one replaces the default of its interface only, adds the logging wrapper of its package, and calls `Setup.Select`.
   - So two selections of one interface give the existing setup error "… select an implementation of …". The order of the setup calls does not matter.
   - Each interface stays a scoped tenant service (D126).
5. Run the stateless rules first (D234):
   - `SubscriptionDraftRules.Check(SubscriptionDraft draft)` in `MsgHatch.Subscriptions.Management` gives `Error?` for these rules: the owner matches its level. An opt-out has no settings, is for the scope users, and is not mandatory. A channel subscription has a channel and settings. A user subscription is for the scope users. A mandatory subscription is a default or company subscription for the scope users. Named users are 1 to 100 different users.
   - `TemplateLookupRules` in `MsgHatch.Templates.Management` has `Check(TemplateLookup lookup)` and `CheckText(string text)`: the scope is a valid `CompanyId`, its folder name has at most 255 characters, the part is a name, and the text has at most 1,000,000 characters.
   - A null argument gives an `ArgumentNullException` in each implementation.
   - The managers run these rules first. Then the template manager checks the language. Then each manager runs the checks that need the registry, the directory, the store, or the engines, in their current order. So an input that breaks a rule and another check gives the error of the rule.
6. Give each error type its identity from its code (D238): `ErrorType` compares and hashes by `Code` only. `Errors.Find(int code)` gives the type of a code of `Errors`, or null.
7. Write the errors of the HTTP APIs (D233):
   - Add `src/MsgHatch.Api.Contracts`. It references `MsgHatch.Domain`, and has the record `ErrorTypeBody(int Code, string Name, bool IsTransient)` with `From(ErrorType type)`, `ToErrorType()`, and its source-generated JSON context. `ToErrorType()` gives the type of `Errors.Find` for a known code.
   - `src/Shared/ErrorProblems.cs` is the internal class `ErrorProblems`, with `int StatusCodeOf(ErrorType type)`, and `Error ErrorOf(int statusCode, ErrorTypeBody? errorType, string? detail)` with the rules of D233 for a body with no `errorType`. `MsgHatch.Subscriptions.AspNetCore` and `MsgHatch.Templates.AspNetCore` compile it, and reference `MsgHatch.Api.Contracts`.
   - The routes of `MsgHatch.Subscriptions.AspNetCore` and `MsgHatch.Templates.AspNetCore` write each error with `title`, `detail`, the status of `StatusCodeOf`, and the extension member `errorType`. `Unavailable` then gives 503 in place of 500, and `Rejected`, `Expired`, and `RateLimited` give 403, 410, and 429.
   - The delivery and registry endpoints of `MsgHatch.AspNetCore` keep their table. A later phase changes them.
8. Move the HTTP contract (D227, D228, D229):
   - Add `src/MsgHatch.Subscriptions.Api.Contracts` and `src/MsgHatch.Templates.Api.Contracts`. Each one references its core package and `MsgHatch.Api.Contracts`, and the `.AspNetCore` package of its API references it.
   - The body records, the JSON contexts `SubscriptionEndpointJson` and `TemplateEndpointJson`, and `SettingsJson` move into the contracts packages. The template records become public.
   - The route paths move into the internal classes `SubscriptionApiRoutes` and `TemplateApiRoutes` in `src/Shared`, which the `.AspNetCore` packages compile. No contracts package has a route path, a status code, an HTTP type, or an exception type.
   - Each record gets public mappings in both directions. A mapping from the wire checks each string with `Create`, and gives `Errors.SerializationFailed` for a value that is not valid.
   - `JsonChannelSettings(JsonElement Json)` in `MsgHatch.Subscriptions.Api.Contracts` is a `ChannelSettings`. The mapping of a response gives it for a channel with no settings type in the registry, and the mapping of a request writes its JSON with no change. The server still gives `Errors.ValidationFailed` for the settings of a channel with no settings type.
   - `RequestValues` and `Caller` stay in `MsgHatch.Subscriptions.AspNetCore`.
9. Allow a host with no role (D230): `MsgHatchOptions` accepts `MsgHatchRoles.None`. The setup check gives an error for `None` with a registered message type.
10. Make the packages of the API boundary AOT-compatible (D237):
   - Check first, before the code: set `IsAotCompatible` on each package of D237 that exists after step 8, and record the warnings of each package in the checked facts. Also check that `EnableRequestDelegateGenerator` works in a class library with the framework reference `Microsoft.AspNetCore.App`.
   - `MsgHatch.Domain` replaces its 4 `MakeGenericType` calls with code that the compiler sees, for example a generic method of the registration that knows `TEvent`.
   - `MsgHatch.Subscriptions.AspNetCore` and `MsgHatch.Templates.AspNetCore` set `EnableRequestDelegateGenerator`.
   - Each package of D237 builds with `IsAotCompatible` and no warning.
11. Sections 4.2, 4.7, 4.12, 4.15, 4.16, 4.17, 4.18, 4.22, 5.1, 5.2, and 5.5 of the design get the contracts packages, the interfaces, the selections, the parts and the check of a draft, the rules, the error contract, the identity of an error, and the AOT setting.

Tests:
- The test base classes `SubscriptionQueriesTestsBase<T>`, `SubscriptionCommandsTestsBase<T>`, `RoutingVerifierTestsBase<T>`, `TemplateQueriesTestsBase<T>`, and `TemplateCommandsTestsBase<T>` hold the use cases of the current tests of the managers and the verifier. The in-process subclass of each one only builds the implementation.
- The selection:
  - The defaults resolve with no selection.
  - A selection replaces only its interface, before and after the setup method of the defaults.
  - Two selections of one interface give the setup error.
  - Each selection has its logging wrapper with `EnableLogging`, and none without.
- The parts and the check of a draft: `GetTemplatePartsAsync` gives each declared part with the names of its engines. `CheckTemplateAsync` gives no issue for a valid text, the issues of each engine for a text that is not valid, the line and the column of a Liquid parse error, and stores nothing. A save of a text with an issue still gives `TemplateInvalid` and 422.
- The rules: a row for each rule of each rule class. An input that breaks a rule and a check with state gives the error of the rule. An unknown language with a part that is not a name gives the error of the part.
- The identity of an error: two types with one code and other names are equal and have one hash. `Errors.Find` gives each type of `Errors` for its code, and null for an unknown code. `ErrorTypeBody.From(type).ToErrorType()` gives the same instance for each type of `Errors`, and an equal type for a type of the consumer.
- The contracts packages: no contracts assembly references an assembly whose name starts with `Microsoft.AspNetCore` or is `System.Net.Http`, and none has a public type that derives from `Exception`.
- `ErrorProblems`:
  - Each type of `Errors` gives the status of D233.
  - `ErrorOf(StatusCodeOf(type), body, detail)` gives an error equal to the original error, for each type of `Errors` and for a type of the consumer.
  - The rows of a body with no `errorType` give the types of D233.
- The endpoints: each error type gives its status, its `ProblemDetails`, and the member `errorType`.
- The contract records: the existing JSON tests of the endpoints pass with no change. A domain value mapped to a response and back gives an equal value, with typed settings and with `JsonChannelSettings`. A response with an invalid id gives `SerializationFailed`.
- `MsgHatchRoles.None` starts a host with no message type, and gives the setup error with one.
- AOT: the build of each package of D237 is the test, because `IsAotCompatible` turns on the analyzers, and each warning is an error.
- Each new test fails first, or fails with a break of the code that it covers. Each changed test passes before and after its change.

Checked facts (2026-11-18). The probes are console apps in a scratch folder, outside the repository, on Windows 11 build 26300 with the .NET SDK 10.0.404 and `Fluid.Core` 2.41.1:

| Fact | Source |
|---|---|
| `FluidParser.TryParse` and `Parse` give a parse error only as text: the message, then ` at (line:column)` at the end of its first line, and for most errors the lines `Source:` and the line of the text. `Fluid.ParseException` has no position, no inner exception, and no data. | A probe with 6 texts |
| `FluidParser.Grammar.TryParse(new FluidParseContext(text), ...)` gives a Parlot `ParseError` with a `TextPosition` for a grammar error, for example `{% for %}` at 1:7. An unknown tag, for example `{% endif %}` with no `if`, throws `Fluid.ParseException` from the parser with only a message (D243). | The probe |
| The positions of Fluid start at 1: `a\n\nb {{ x \| }}` gives 3:9, `{% for %}` 1:7, `ab\ncd {% endif %}` 2:12, `line1\nline2 {% if x %}` 2:17, and `{{ 'x }}` 1:3. | The probe, and the rows of `CheckTemplateAsync_LiquidHtmlBody_GivesTheLineAndTheColumnOfAParseErrorOfFluid` |
| `IsAotCompatible` on the 8 packages of D237 that exist after step 8 gave these warnings before the fixes. `MsgHatch.Domain`: IL2026 for 10 `[Range(typeof(TimeSpan), ...)]` and `[MaxLength]` attributes of options, IL2091 for 12 calls of `ActivatorUtilities.CreateInstance<T>` and `ServiceDescriptor.Singleton<TService, TImplementation>` with a type parameter, IL2055, IL2072, and IL3050 for the closed open router of `MessageRegistration`, IL2070 and IL3050 for the router type check of `UseRouter`, and IL3050 for the message shapes of `MsgHatchJson`. `MsgHatch.Subscriptions`: IL2026 for 1 range and IL2091 for 3 calls. `MsgHatch.Templates`: IL2091 for 2 calls. `MsgHatch.Subscriptions.AspNetCore` and `MsgHatch.Templates.AspNetCore`: IL2026 and IL3050 for each of their 8 and 6 `Map…(string, Delegate)` calls. The three contracts packages: none. | The build of each package with `-p:TreatWarningsAsErrors=false` |
| `EnableRequestDelegateGenerator` works in a class library with `<FrameworkReference Include="Microsoft.AspNetCore.App" />`: with it, both `.AspNetCore` packages build with no warning of their own, also for a handler cast to `Delegate`. | The builds of the two packages |
| The options validator generator writes the file-local copies `__SourceGen__RangeAttribute` and `__SourceGen__MaxLengthAttribute`, and the generated validators check the rules with these copies, with no reflection on the `System.ComponentModel.DataAnnotations` attributes. | The generated `Validators.g.cs` of `MsgHatch.Domain` with `EmitCompilerGeneratedFiles` |
| `PlaceholderTemplates.Engine<TPayload>` needs `TPayload : IPlaceholderPayload<TPayload>`, so it cannot implement `ITemplateEngineFactory.Create<TPayload>`, which has no constraint. | The compiler |
| A substitute of an interface whose method returns `ValueTask<ErrorOr<T>>` gives `default(ErrorOr<T>)` for an unconfigured call, which is the error `Uninitialized`. The template tests configure each engine substitute to give no issue. | The first run of the template tests after step 3 |
| The 27 unit test projects pass 4,024 tests and skip 1 test: the path limit test of the named pipe signal on Windows. `MsgHatch.Domain.Tests.Unit` passes 1,455 tests, `MsgHatch.Subscriptions.Tests.Unit` 408, `MsgHatch.Templates.Tests.Unit` 357, `MsgHatch.Subscriptions.AspNetCore.Tests.Unit` 147, `MsgHatch.Templates.AspNetCore.Tests.Unit` 69, `MsgHatch.Templates.Liquid.Tests.Unit` 91, `MsgHatch.Api.Contracts.Tests.Unit` 60, `MsgHatch.Subscriptions.Api.Contracts.Tests.Unit` 50, and `MsgHatch.Templates.Api.Contracts.Tests.Unit` 14. The line coverage of the production code is 99.1 %. | `pwsh ./coverage.ps1` on the content of `898899d` |
| Each new test failed first, or failed with a break of the code that it covers: the routes of the parts and the check (14 tests failed with no routes), the position of a Liquid parse error (3 rows), the default rule of `AddDefaultTenantService` (3 rows), the order of the rules of the subscription manager (4 rows) and of the template manager (3 tests), the identity of an error type by its code (2 tests), the member `errorType` of the template endpoints (13 tests), two mappings of the subscription contracts (2 tests), and the 3 tests of the role `None` before the change. | The breaks, each one reverted after its run |
| `MsgHatch.Data.MongoDb.Tests.Integration` passes 335 tests and `MsgHatch.Subscriptions.Data.MongoDb.Tests.Integration` 50, on the MongoDB of the local settings. The first run of the store tests failed 1 test, because its entry of `NumbersEvent` came from the plain constructor and had no shape (D245), so the commit `Build the numbers entry of the MongoDB tests with its shape` follows commit 14. The EF Core integration tests did not run: their local settings name a SQL Server that is not available, because the container `dev-mssql` exited before the phase, and the phase starts or stops no container. | `dotnet test` of each integration project |

Commits:
1. "Record the decisions of the HTTP API clients in the plan (D227 to D235)"
2. "Align the plan of the HTTP APIs with contracts packages, generated clients, AOT, and admin UIs (D236 to D242)"
3. "Split the subscription manager into queries and commands"
4. "Split the template manager into queries and commands"
5. "Give the template queries the parts and the check of a draft"
6. "Select the implementation of each management interface"
7. "Run the stateless rules of the managers first"
8. "Compare error types by their code"
9. "Write each error of the management APIs with its error type"
10. "Move the HTTP bodies of the management APIs to contracts packages"
11. "Allow a host with no role and no message type"
12. "Make the packages of the API boundary AOT-compatible"
13. "Record the commits and the checked facts of phase 26 in the plan"
14. "Record phase 26 as done in README and AGENTS.md"

Commit 3 holds item 1 (`c995fb1`), commit 4 item 2 (`6980149`), commit 5 item 3 (`25ad7df`), commit 6 item 4 (`b8c3eea`), commit 7 item 5 (`b5cc2e8`), commit 8 item 6 (`0416280`), commit 9 item 7 (`9eab401`), commit 10 item 8 (`e69cbfa`), commit 11 item 9 (`c31b4e9`), and commit 12 item 10 (`898899d`). Item 11 goes with the commit whose code it describes. The solution build had no errors and no warnings at the commits 3 to 10 and 12, and the unit test projects of the changed code passed at each commit. At commit 11, only the domain projects were built and tested. Commits 9 and 10 add the projects of the contracts packages and their test projects to `MsgHatch.slnx`.

The implementation changed the plan in these points:
- Fluid 2.41.1 gives a parse error only as text, with ` at (line:column)` at the end of the first line of its message. The grammar of `FluidParser` gives a Parlot `ParseError` with a position for most errors, but an unknown tag, for example `{% endif %}` with no `if`, throws `Fluid.ParseException` with only a message. So the check-first item of step 3 fails, and the Liquid engine reads the position from the text of the message (D243, Recommended).
- The placeholder engine has no factory object, because its payload type needs the constraint `IPlaceholderPayload<TPayload>`. So `PlaceholderTemplates.EngineName` is the constant `placeholder`, and `AddTemplateEngine` takes the name of the engine as a parameter. `AsEmail` gives `PlaceholderTemplates.EngineName` or `ITemplateEngineFactory.Name`.
- Each engine gives one issue for the first problem that it finds, as each engine finds one problem today. A check with issues logs the event 2016 with the number of the issues, at the Information level, and no message of an issue.
- `Setup.Select` is internal to `MsgHatch.Domain`, so the builder gets the public methods `AddDefaultTenantService`, which registers a default only when no registration of the service type exists, and `UseTenantService`, which records the selection, removes each registration of the service type, and registers the factory. The selection methods of the packages call them.
- Each selection method also takes `string? setupMethod = null`, as `UseSubscriptionStore` does, so that a client package of phase 27 gives its own name to the setup error.
- The template package had no logging wrapper of the manager (section 4.22). The selections need one (step 4), so `TemplateQueriesLoggingWrapper` and `TemplateCommandsLoggingWrapper` wrap the default and each selection, with the event ids 2017 to 2028 (D244, Recommended).
- `TemplateLookupRules` also has `Check(TemplateQuery)`, which checks the scope of a list, because `GetTemplatesAsync` takes a query and no lookup.
- `SubscriptionManager.GetSubscriptionsAsync` gives an `ArgumentNullException` for a null query, as the other methods do. Before, the store got the null.
- `TemplateSaveRequest` and `TemplateCheckRequest` hold only the text and the version, which are domain values already, so they get no mapping. Each other body record gets a mapping in both directions.
- `ChannelResponse.ToChannelDescriptor(registry)` gives a private descriptor type of the contracts package, with the settings type of the newest entry `Settings:{channel}`, or `ChannelSettings` (D229). So the client of phase 27 can use this mapping in place of its own `HttpChannelDescriptor`.
- `RoutingCheckResponse.ToCheck(registry)` reads the trace with the entry `Trace:Subscription` of the registry of the host. A host with no such entry, for example a host with only API clients and no `UseSubscriptions`, gets each row with no trace.
- `SettingsJson` is public, because the server reads the settings of a request with it, and the server is in another assembly. Its server read keeps `Errors.ValidationFailed`, and the mappings from the wire give `JsonChannelSettings` or `Errors.SerializationFailed`.
- The contracts tests are in `tests/MsgHatch.Api.Contracts.Tests.Unit`, `tests/MsgHatch.Subscriptions.Api.Contracts.Tests.Unit`, and `tests/MsgHatch.Templates.Api.Contracts.Tests.Unit`, one for each contracts package. The first one compiles `src/Shared/ErrorProblems.cs` to test the internal class with no `InternalsVisibleTo`.
- The 4 `MakeGenericType` calls of `MsgHatch.Domain` close an open generic type that only a `Type` object names: the open router of `UseRouter`, and the message type of a content type entry. So `UseRouter` takes an `IOpenRouterFactory`, whose generic method `Create<TEvent>` makes the router, and the entries of messages come from `ContentTypeEntry.ForReceivedMessage<TEvent>` and `ForOutgoingMessage<TEnvelope>`, which give each entry an internal shape with the typed code (D245, Recommended). The open router types of the tests become `TestOpenRouter.Factory` and `TestOpenRouter.Failing`. The test of an open router with a constraint becomes a test of a factory that cannot make the router, and the test of a type that is not an open generic router becomes a test of a null factory.
- A test that builds the entry of a message with the plain constructor of `ContentTypeEntry` gets an entry with no shape, so the test entries of `TestContentTypes` and of the numbers event of the MongoDB tests use `ForReceivedMessage` and `ForOutgoingMessage`.
- The type parameters that reach `ActivatorUtilities.CreateInstance<T>` or `ServiceDescriptor.Singleton<TService, TImplementation>` get `[DynamicallyAccessedMembers(PublicConstructors)]`, for example `UseInboxStore<TStore>` and `WithRouter<TRouter>`.
- The `[Range(typeof(TimeSpan), ...)]` and `[MaxLength]` attributes of the options give IL2026, because their constructors need reflection. The source-generated validators check these rules with their own generated copies of the attributes, `__SourceGen__RangeAttribute` and `__SourceGen__MaxLengthAttribute`, with no reflection. So each such property gets `[UnconditionalSuppressMessage("Trimming", "IL2026")]` with this reason.

Done when: the build has no errors and no warnings, all unit tests pass, and `pwsh ./coverage.ps1` passes.

### Phase 27: HTTP API clients

Goal: a host calls the subscription and template management APIs of another host through the interfaces of phase 26 (D227). A consumer cannot see which implementation it uses: the same input gives the same result and the same error (D233, D234). A host can read over HTTP and write in process, or the reverse, or use one implementation for both (D231).

1. Check first that a routing verification changes no stored subscription (D232). If it does, the verifier goes with the commands, and the plan changes before the code.
2. Add the generator of the HTTP clients to `MsgHatch.SourceGeneration` (D236):
   - The internal attributes, which the generator adds with `RegisterPostInitializationOutput`: one on an internal interface with the type of a JSON context, and one on each method with the HTTP method and the route.
   - A parameter whose name is in the route is a route value. One parameter with the body attribute is the body. Each other parameter, except the last `CancellationToken`, is a query value, which a request leaves out when it is null. A route value or a query value is a string, a `Guid`, a number, a `bool`, or an enum, which the request writes with its name.
   - A method returns `Task<ErrorOr<TResponse>>`, or `Task<Error?>` for a success with no body. The generator writes an internal sealed class with a constructor that takes the `HttpClient`. It finds the `JsonTypeInfo` of each body in the JSON context, and calls `ErrorProblems.ErrorOf` for each failure (D233).
   - An interface or a method that the generator cannot write gives an error diagnostic with its own id, for example a route value with no parameter, a second body, or a type that is not in the JSON context.
3. Add `src/MsgHatch.Templates.Api.Client` (D227, D236). It references `MsgHatch.Templates.Api.Contracts`, the package `Microsoft.Extensions.Http`, and `MsgHatch.SourceGeneration` as an analyzer. It compiles `src/Shared/TemplateApiRoutes.cs` and `src/Shared/ErrorProblems.cs`, and sets `IsAotCompatible` (D237):
   - `TemplateApiClientOptions` from the section `MsgHatch:TemplateApiClient`, with a source-generated validator: `BaseAddress`, a required absolute URI with no default, `Prefix`, with the default `/templates` of `MapMsgHatchTemplates`, and `Timeout`, from 1 second to 10 minutes, with the default 30 seconds.
   - `UseTemplateApiClient` selects the queries and the commands. `UseTemplateQueriesApiClient` selects the queries, and `UseTemplateCommandsApiClient` selects the commands. Each method takes `Action<TemplateApiClientOptions>? configure = null` and `Action<IHttpClientBuilder>? configureHttp = null`, and has an overload with `Uri baseAddress`. The host adds its handlers with `configureHttp`, for example the authentication. The name of the HTTP client stays internal. Each method calls the selection methods of phase 26.
   - The internal interface `ITemplateApi` has a method for each route of D224, D240, and D241, and the generator writes `TemplateApi`.
   - The internal adapters `HttpTemplateQueries` and `HttpTemplateCommands` implement the interfaces of phase 26. Each method runs `TemplateLookupRules` first, maps the domain values to the bodies, adds `tenantId` when the key has a tenant, calls `TemplateApi`, and maps the response back.
   - The API has no template id (D224), so `TemplateSource.Key.Id` is a stable id from the lookup. A caller reads it as opaque, and sends back only the version.
4. Add `src/MsgHatch.Subscriptions.Api.Client` (D227, D236) in the same way:
   - `SubscriptionApiClientOptions` from the section `MsgHatch:SubscriptionApiClient`, with the default `Prefix` `/subscriptions` of `MapMsgHatchSubscriptions`. It compiles `src/Shared/SubscriptionApiRoutes.cs`.
   - `UseSubscriptionApiClient` selects the queries, the commands, and the verifier. `UseSubscriptionQueriesApiClient` selects the queries and the verifier. `UseSubscriptionCommandsApiClient` selects the commands.
   - The internal interface `ISubscriptionApi` has the routes of section 4.17 with no `/me`. A `/me` route narrows each query to the signed-in user, and a browser calls it (D154). The internal adapters `HttpSubscriptionQueries`, `HttpSubscriptionCommands`, and `HttpRoutingVerifier` call the generated `SubscriptionApi`. Each command runs `SubscriptionDraftRules` first.
   - `GetChannelsAsync` gives an internal `HttpChannelDescriptor`, with the settings type of the registry entry of the channel, or `typeof(ChannelSettings)` (D229).
   - A create is not idempotent. The design tells the host not to retry a `POST`.
5. Logging: `IHttpClientFactory` logs the URI of each request, and the query can hold a user id or a company id. A test checks the log of .NET 10. If the log holds the query, the setup method calls `RemoveAllLoggers()` on its HTTP client, and the logging wrappers of phase 26 log each call.
6. Add the section 4.23 "HTTP API clients" to the design, with the generator and the setup of a Blazor admin UI (D235): `AddMsgHatch` with `MsgHatchRoles.None`, `UseSubscriptionApiClient()`, `UseTemplateApiClient()`, and a token handler through `configureHttp` of both. In Interactive Server mode, a handler of `IHttpClientFactory` runs in its own DI scope, so it reads the token through a circuit services accessor. Phase 28 adds the admin UIs. Sections 4.2 and 5.1 get the two packages.

Tests, in `tests/MsgHatch.SourceGeneration.Tests.Unit`, `tests/MsgHatch.Templates.Api.Client.Tests.Unit`, and `tests/MsgHatch.Subscriptions.Api.Client.Tests.Unit`:
- The generator: a row for each diagnostic. An interface of each supported shape compiles with no warning, also with `IsAotCompatible`.
- The public types of each client package are only its options, their validator, and its setup class. No public member has a type of `System.Net.Http`.
- The HTTP subclass of each test base class of phase 26. A `TestServer` maps the endpoints on a real store, and the HTTP client of the client host uses the handler of the `TestServer` as its primary handler.
- Parity: the same failing inputs run on both implementations, and `local.Error with { Exception = null }` equals the error of the client. No test asserts the text of a message.
- The mixed hosts: reads over HTTP and writes in process on one store, and the reverse. A write in process followed by a read over HTTP gives the new version.
- Validation before HTTP: a handler counts the requests, and each rule gives `ValidationFailed` with 0 requests.
- The errors of D233:
  - A body with `errorType`.
  - A 401 and a 403 with a default problem body.
  - A 404 of a wrong prefix gives `Exception`.
  - A handler that throws `HttpRequestException` gives `Unavailable`.
  - A cancelled call gives `OperationCancelled`.
  - A 200 with a body that does not parse gives `SerializationFailed`.
- A service of a tenant sends `tenantId`, and the service of `TenantKey.None` sends none.
- The settings: typed settings make a round trip. A channel with no settings type gives `JsonChannelSettings`, and an update sends its JSON back with no change.
- The setup:
  - No base address, and a relative base address, give an options error at start.
  - Two selections of one interface give the setup error.
  - A host with `MsgHatchRoles.None` and only the clients starts.
- No log record holds a user id, a company id, or a template text.
- Each new test fails first, or fails with a break of the code that it covers.

Checked facts (2026-11-18). The checks ran on Windows 11 build 26300 with the .NET SDK 10.0.404 and `Microsoft.CodeAnalysis.CSharp` 5.0.0:

| Fact | Source |
|---|---|
| A routing verification changes no stored subscription. `RoutingVerifier` calls only `ISubscriptionStore.QueryAsync`, and runs the routing on the `InMemorySubscriptionStore` copy of `VerificationRun`. So the verifier goes with the queries (D232), and the check-first item of step 1 holds. | `RoutingVerifier.cs` and `VerificationRun.cs`, and the example X7 over HTTP |
| `IHttpClientFactory` of .NET 10 logs the URI of each request with the query as `*`, for example `Start processing HTTP request PUT http://localhost/templates/Requests/en/subject/PlainText?*`. So step 5 adds no `RemoveAllLoggers()`. With the switch `System.Net.Http.DisableUriRedaction`, the log holds the query, and the log test of the template client fails. | The log tests of both clients, and one run with the switch |
| An HTTP route cannot hold an empty segment. The in-process template manager gives `ValidationFailed` for an empty language, and a request with an empty language gets the 404 of the framework, so the client gave `Exception` (D246). | The row "an empty language" of `TemplateManagementTestRows.InvalidLookupRows` |
| The JSON of the settings has no type. Before D247, the server read the settings of the type of another channel as the settings of the channel, so the HTTP subclass of `SubscriptionCommandsTestsBase` failed 2 rows of rule 2.3.4: `M11` and the email settings on the channel `Pager`. | The first run of `HttpSubscriptionCommandsUnitTests` |
| The endpoint of the verification rejected an empty language, but the in-process verifier gave a report for it (D248). | The first run of `SubscriptionApiClientParityUnitTests` |
| `AddEmbeddedAttributeDefinition` of `IncrementalGeneratorPostInitializationContext` exists in `Microsoft.CodeAnalysis.CSharp` 5.0.0, and adds the source `Microsoft.CodeAnalysis.EmbeddedAttribute.cs`. | The hint names of the generator tests |
| A source-generated JSON context has a `JsonTypeInfo<T>` property also for each type that its body types reach, for example `String` in `TemplateEndpointJson`. So the rows of `MSGHATCH010` use `Uri`. The generator finds the properties only in the metadata of a referenced assembly, because one generator does not see the output of the System.Text.Json generator in the same compilation. | The generator tests |
| Both client packages, with the generated code, build with `IsAotCompatible` and no warning. The generator tests check the errors and the warnings of the C# compiler, with no trim analyzer. | The build of each client package |
| A minimal API route `/templates` matches the generated URI `templates` relative to the base address `http://localhost/`, and a base address with a path, for example `https://api.example.com/api/`, keeps its path. | The HTTP subclasses, and `UseTemplateApiClient_OptionsFromTheConfiguration_SendsEachRequestToTheBaseAddressAndThePrefix` |
| The unit test projects pass 4,310 tests and skip 1 test: the path limit test of the named pipe signal on Windows. `MsgHatch.SourceGeneration.Tests.Unit` passes 62 tests, `MsgHatch.Templates.Tests.Unit` 360, `MsgHatch.Templates.Api.Client.Tests.Unit` 103, `MsgHatch.Subscriptions.Tests.Unit` 418, `MsgHatch.Subscriptions.Api.Contracts.Tests.Unit` 56, `MsgHatch.Subscriptions.AspNetCore.Tests.Unit` 150, and `MsgHatch.Subscriptions.Api.Client.Tests.Unit` 123. The line coverage of the production code is 99.0 %. | `pwsh ./coverage.ps1` on the content of `0150113` |
| The client packages compile `src/Shared/ErrorProblems.cs` and use only `ErrorOf`, so `StatusCodeOf` has 12 lines with no test in each client assembly. The adapters and the setup classes have no line with no test, and the generated code is not part of the coverage, as each `*.g.cs` file. | The coverage report of `pwsh ./coverage.ps1` |
| Each new test failed first, or failed with a break of the code that it covers: the 16 diagnostic rows (no report of a diagnostic), the compile test of the shapes (no `#nullable enable`), the 21 tests of the generated client (a wrong separator, a wrong prefix, no `errorType`, no `detail`, the status 500, no catch of `JsonException`, and no mapping of a cancellation, a timeout, or an `HttpRequestException`), the 3 rows of the empty language of `TemplateLookupRules` before the rule, the tests of both clients (no selection, a wrong prefix, no rules, no tenant, no options rules, an internal validator, a changed default prefix, and no check of a null item), the endpoint rows and the contract tests of `settingsType` before D247, the rows of the empty language of the verifier and the parity row before D248, and the log tests (the switch `System.Net.Http.DisableUriRedaction`). | The breaks, each one reverted after its run |

Commits:
1. "Add the generator of the HTTP API clients"
2. "Add the HTTP client of the template management API"
3. "Name the settings type in a subscription request"
4. "Check a routing verification request with rules first"
5. "Add the HTTP client of the subscription management API"
6. "Describe the HTTP clients and the setup of an admin UI in the design"
7. "Record the commits and the checked facts of phase 27 in the plan"
8. "Record phase 27 as done in README and AGENTS.md"

Commit 1 holds item 2 (`a484703`), commit 2 item 3 with D246 (`d24513d`), commit 3 D247 (`f0fb4e6`), commit 4 D248 (`4f8c81b`), commit 5 item 4 (`23ba5f3`), and commit 6 item 6 (`0150113`). Item 1 and item 5 are checks, and their results are in the checked facts. The unit test projects of the changed code passed at each commit, and the solution build had no errors and no warnings at commit 6. Commits 2 and 5 add the client projects and their test projects to `MsgHatch.slnx`.

The implementation changed the plan in these points:
- The generated class takes the `HttpClient` and the prefix of the route group, because the prefix is an option of the host. The relative URI is the prefix and the route, so a base address with a path keeps its path.
- The attributes of the generator are `HttpApi`, `HttpCall`, and `HttpBody` in the namespace `MsgHatch.Http`, with `Microsoft.CodeAnalysis.EmbeddedAttribute`. The package `MsgHatch.Templates` gives the generator to each consumer, so each consumer compilation gets its own copy, and two assemblies with `InternalsVisibleTo` get no conflict.
- The generator gives the diagnostics `MSGHATCH002` to `MSGHATCH010`, one for each cause (section 4.23). The interface must not be generic or nested, and the JSON context comes from a referenced assembly.
- The test "An interface of each supported shape compiles with no warning, also with `IsAotCompatible`" checks the compiler errors and warnings of a Roslyn compilation. The trim and AOT analyzers run in the build of each client package, which sets `IsAotCompatible`, as for phase 26.
- The errors of D233 that need no server are tests of the generated client `ShapesApi` of the generator tests: a body with `errorType`, an error type of the consumer, a 401 and a 403 with a default problem body, a 404 with a default problem body, a body that is not a problem object, `HttpRequestException`, a timeout, a cancelled token, and a success body that does not parse. The client tests check the 404 of a wrong prefix against a real server.
- The internal source `src/Shared/ApiResults.cs`, which both client packages compile, maps a generated result with the mapping of a contract record, and gives `SerializationFailed` for a null item of a list.
- Each interface has its own named HTTP client, so a host that calls `UseTemplateQueriesApiClient` and `UseTemplateCommandsApiClient` does not add its handlers two times to one client. The subscription verifier uses the HTTP client of the queries.
- `GetChannelsAsync` of the subscription client uses `ChannelResponse.ToChannelDescriptor`, as phase 26 recorded, so the client has no `HttpChannelDescriptor`.
- `TemplateLookupRules.Check(TemplateLookup)` also rejects a null or empty language, because an HTTP route cannot hold an empty segment (D246, Recommended). `TemplatePartResponse.ToPartInfo` checks a part with the language `und`.
- `SubscriptionRequest` gets the optional field `settingsType`, and the server reads the settings with it, so the manager checks the settings type of each request (D247, Recommended). Commit 3 holds this change.
- `RoutingVerificationRules.Check` runs first in each verifier, because the endpoint rejected an empty language and the in-process verifier did not (D248, Recommended). The endpoint keeps its check of the body, with the same message. Commit 4 holds this change, and `RoutingVerifierTestsBase` gets the test of an empty language.
- The test bases of phase 26 run over HTTP on a `WebApplication` with a `TestServer`. The server removes `IStartupValidator`, because the in-process host of the test bases builds its services with no setup check, and some test setups have no transport. The template client host has `MsgHatchRoles.None` and only the client. The subscription client host has the services of the server and the client, so it has the settings types and the trace type of the server.
- `IHttpClientFactory` logs the query as `*` (step 5), so the setup methods call no `RemoveAllLoggers()`.
- Section 4.7 gets the setup methods of both client packages, and sections 4.17, 4.18, and 4.22 get D246 to D248.

Done when: the build has no errors and no warnings, all unit tests pass, and `pwsh ./coverage.ps1` passes.

### Phase 28: admin UIs

Goal: MsgHatch ships the list and edit pages of the subscriptions and the templates as Razor class libraries (D239). A host adds them in its setup, selects the HTTP clients or the in-process implementations (D235), and adds its own authorization. The template text has the editor of its engine, and the Liquid editor is a package of its own (D240, D242).

1. Check first, before the code:
   - A Razor class library with `IsAotCompatible` builds with no warning, and a WebAssembly host with `PublishTrimmed` renders its components with no trim warning, also with `DynamicComponent` and a registered editor type.
   - The current version and the license of bUnit.
2. Add `src/MsgHatch.AdminUI` (`Microsoft.NET.Sdk.Razor`). It references `MsgHatch.Api.Contracts` and the package `Microsoft.AspNetCore.Components.Web`. It has the parts of both admin UIs: the list layout with the states loading, error, and empty, the pager, the delete dialog, and the error text of each error type (D239).
3. Add `src/MsgHatch.Templates.AdminUI`. It references `MsgHatch.Templates.Api.Contracts` and `MsgHatch.AdminUI`:
   - The template list, with filters for the message type and the company, and a new template from the declared parts of `GetTemplatePartsAsync` (D240).
   - The edit page of one part: the editor of its engine, the version, a save with the expected version, and a delete after a question. `TemplateInvalid` shows its message next to the text, and `Conflict` loads the stored version again.
   - `TemplateEditorBase`, the plain editor, `AddTemplateEditor<TEditor>(string engine)`, and the setup method `AddMsgHatchTemplatesAdminUI()` (D240).
   - The public route builders `TemplateAdminRoutes`, with the pages under `/msghatch/templates`.
4. Add `src/MsgHatch.Templates.Liquid.AdminUI` (D242). It references `MsgHatch.Templates.AdminUI` only:
   - `LiquidTokenizer`, `LiquidTemplateEditor` with its JavaScript module for the scroll, and `AddLiquidTemplateEditor()`.
   - The editor marks an open `{{` or `{%` and a `{% liquid %}` tag with no call. It calls `CheckTemplateAsync` 500 ms after the last key and before a save, and marks the line and the column of each issue (D241).
5. Add `src/MsgHatch.Subscriptions.AdminUI`. It references `MsgHatch.Subscriptions.Api.Contracts` and `MsgHatch.AdminUI`:
   - The message types with their states, the subscriptions with filters for the owner level, the message type, and the channel, and the edit page of a subscription with a create, an update, and a delete.
   - The page edits the settings of a channel as JSON text, with the entry `Settings:{channel}` of the content type registry, or as `JsonChannelSettings` (D229).
   - A page runs a routing verification and shows its report (D163).
   - The public route builders `SubscriptionAdminRoutes`, with the pages under `/msghatch/subscriptions`, and the setup method `AddMsgHatchSubscriptionsAdminUI()`.
6. Add `examples/AdminUiExample`, an Interactive Server host. It uses `AddMsgHatch` with `MsgHatchRoles.None`, `UseSubscriptionApiClient`, `UseTemplateApiClient`, both admin UIs, and the Liquid editor. It calls the APIs of `ExampleApp` with a token from its configuration, which git ignores. It loads Bootstrap 5.3 from a pinned CDN address, so the repository holds no copy and no step downloads a file. It has no sign-in, and listens only on localhost.
7. Add the section 4.24 "Admin UIs" to the design, with the editor extension point. Sections 4.2 and 5.1 get the packages, and `AGENTS.md` gets the command that runs the example.

Tests, with bUnit, in `tests/MsgHatch.AdminUI.Tests.Unit`, `tests/MsgHatch.Templates.AdminUI.Tests.Unit`, `tests/MsgHatch.Templates.Liquid.AdminUI.Tests.Unit`, `tests/MsgHatch.Subscriptions.AdminUI.Tests.Unit`, and `tests/AdminUiExample.Tests.Unit`:
- Each page with substitutes of the interfaces of phase 26: the rows of a list, the empty state, and the text of `Unavailable` and `Rejected`. A save sends the expected version. `Conflict` loads the stored version again. `ValidationFailed` and `TemplateInvalid` show their message next to the form. A delete runs only after the question.
- The editors: a part with one engine and a registered editor gets that editor. A part with two engines, or an engine with no editor, gets the plain editor. No page type of a library sets a render mode.
- The Liquid editor: a row of `LiquidTokenizer` for each kind of span. The marks of an open delimiter and of a `{% liquid %}` tag come with no call. An issue with a line and a column gets its mark, and an issue with no position shows only its message. The editor calls the check one time after a pause in typing. A test that references `MsgHatch.Templates.Liquid` checks that the engine name of the editor equals `LiquidTemplates.Engine.Name`.
- The example host resolves each interface to its HTTP client.
- Each new test fails first, or fails with a break of the code that it covers.

Checked facts (2026-11-18). The probes are projects in a scratch folder, outside the repository, on Windows 11 build 26300 with the .NET SDK 10.0.404 and `Microsoft.AspNetCore.Components.Web` 10.0.14:

| Fact | Source |
|---|---|
| bUnit 2.15.2 is the current version, from 2026-11-10, with the license MIT. For `net10.0` it needs `Microsoft.AspNetCore.Components`, `Microsoft.AspNetCore.Components.Web`, and the packages of WebAssembly and authorization 10.0.13 or later, and AngleSharp, which has the license MIT. | The NuGet registration of `bunit` |
| A Razor class library with `IsAotCompatible` and `TreatWarningsAsErrors` builds with no warning, also with a setup method whose type parameter has `DynamicallyAccessedMembers(All)`, except IL2111 for each `DynamicComponent` in its markup: the generated `OpenComponent<DynamicComponent>` keeps all members of `DynamicComponent`, and its setter of `Type` has `DynamicallyAccessedMembers`. `UnconditionalSuppressMessage` for IL2111 on a partial class of the page removes the warning (D250). | The build of a probe library |
| A Blazor WebAssembly host with `PublishTrimmed` publishes with no .NET workload, because the runtime pack comes from the NuGet restore. Its default `TrimMode` is `partial`, so the trimmer trims each assembly with `IsTrimmable`, which `IsAotCompatible` sets. The trimmed probe library had no page type and no plain editor, and the router of the host gave "Not found". | `dotnet msbuild -getProperty:TrimMode`, the linked assembly of the probe, and the page in the browser pane |
| With `DynamicDependency(All)` of the page and of the plain editor on the setup method of the probe library, the trimmed host published with no trim warning, and the page showed the registered editor and the plain editor through `DynamicComponent` (D250). | The publish output, and the DOM of the page in the browser pane, from a static file server on `127.0.0.1` |
| A `[SupplyParameterFromQuery]` property of .NET 10 is no component parameter, so a bUnit test sets its value through the address of `BunitNavigationManager`. | The bUnit documentation through Context7, and `Render_CompanyInTheQuery_ReadsTheTemplateOfTheCompany` |
| The event triggers of bUnit return while an event handler waits for the pause of `FakeTimeProvider`. After `Advance`, the rest of the handler runs on the renderer, and `WaitForAssertion` sees the check of the Liquid editor. | `Input_ThreeChangesAndAPause_CallsTheCheckOneTimeWithTheLastText` |
| In the example host, each selection of a client has its logging wrapper (D244), so the services of `TenantKey.None` resolve to `SubscriptionQueriesLoggingWrapper` and the other wrappers, and not to the internal client types. So the test of the example host checks that the call of each interface goes over HTTP to the prefix of its API with the token. | The first run of `AdminUiExample.Tests.Unit` |
| `MapStaticAssets` and `@Assets` work in the example host on a `TestServer` in the environment `Testing`, and the prerender of an admin page calls the API clients: with no API, the template list shows the text of `Unavailable`. | `Get_TemplateListWithNoApi_ShowsTheTextOfUnavailable` |
| The 34 unit test projects pass 4,460 tests and skip 1 test: the path limit test of the named pipe signal on Windows. `MsgHatch.AdminUI.Tests.Unit` passes 24 tests, `MsgHatch.Templates.AdminUI.Tests.Unit` 45, `MsgHatch.Templates.Liquid.AdminUI.Tests.Unit` 42, `MsgHatch.Subscriptions.AdminUI.Tests.Unit` 36, and `AdminUiExample.Tests.Unit` 3. The line coverage of the production code is 98.8 %: `MsgHatch.AdminUI` 100 %, `MsgHatch.Subscriptions.AdminUI` 97.0 %, `MsgHatch.Templates.AdminUI` 96.2 %, and `MsgHatch.Templates.Liquid.AdminUI` 98.3 %. The lines with no test are the loading state of the edit pages, the guards of a save or a delete with no loaded item, a part index and a channel name that the selects cannot give, the save of company recipients, the badge of a skipped check, the catch of a closed circuit, and the result of a check that a new key cancelled. `AdminUiExample` is not part of the coverage, because the report includes only the assemblies `MsgHatch.*`. | `pwsh ./coverage.ps1` on the content of `ac7e30c` |
| Each new test failed with a break of the code that it covers: the shared parts (24 tests, with an empty markup, a render mode, and an empty error text), the template admin UI (44 tests, with empty pages, a render mode, other route templates, a route builder with no values, and no checks of the setup method), the later editor of one engine (1 test, with the first editor), the Liquid editor (38 tests, with an empty editor, a render mode, another engine name, and a tokenizer with no spans and a wrong issue), the pause of the check (1 test, with no pause), the subscription admin UI (34 tests, with empty pages, a render mode, other route templates, and a route builder with no id), the example host (3 tests, with no start check of the options, no token handler, and no admin assemblies), and the 6 tests of commit 7 (the class of each span kind, a position after the end of the text, no guard of a tag at the end of a text, the text of the company recipients, and no guard of empty settings). Two tests of a null service collection passed with the break, because the DI method that follows throws the same exception, so the phase removed them. | The breaks, each one reverted after its run |

Checks that the phase did not do:
- A trimmed WebAssembly host of the four real admin packages. The probe used a library of its own with the same patterns, and the real packages build with `IsAotCompatible` and no warning.
- A run of `AdminUiExample` against a live `ExampleApp`. The phase starts no host, and the test of the example host runs the host on a `TestServer` with a fake API.
- The integrity hash of Bootstrap 5.3.3 in `App.razor` comes from the Bootstrap documentation. The phase downloads no file, so it did not compute the hash. A browser checks it when it loads the page, and refuses the style sheet when the hash is wrong.

Commits:
1. "Add the shared parts of the admin UIs"
2. "Add the admin UI of the templates with the editor extension point"
3. "Add the Liquid editor of the template admin UI"
4. "Add the admin UI of the subscriptions"
5. "Add the example host of the admin UIs"
6. "Describe the admin UIs in the design"
7. "Test the behaviors of the admin pages that the coverage showed"
8. "Record the commits and the checked facts of phase 28 in the plan"
9. "Record phase 28 as done in README and AGENTS.md"

Commit 1 holds item 2 (`b6fbe83`), commit 2 item 3 (`811ddd2`), commit 3 item 4 (`3d40f59`), commit 4 item 5 (`2e105e1`), commit 5 item 6 (`bf71132`), commit 6 item 7 (`fd721b6`), and commit 7 the tests of the lines that the first coverage report showed with no test (`ac7e30c`). Item 1 is a check, and its results are in the checked facts. Commits 1 to 5 add their projects and test projects to `MsgHatch.slnx`, and commit 1 adds `Microsoft.AspNetCore.Components.Web` 10.0.14 and `bunit` 2.15.2 to `Directory.Packages.props`. The unit test project of each new package passed at its commit, and the solution build with `--no-incremental` had no errors and no warnings at commit 6 and at commit 7.

The implementation changed the plan in these points:
- A library page with `DynamicComponent` gives IL2111, and a trimmed WebAssembly host removes the pages of a library with `IsAotCompatible`. So each setup method keeps the pages of its package with `DynamicDependency`, and the template edit page suppresses IL2111 with its reason (D250, Recommended).
- The pages resolve the services of `TenantKey.None` through the `IServiceProvider` of their scope, because `[Inject]` takes only a constant key, and the plan has no tenant selection (D249, Recommended).
- The save button is on the edit page, so `TemplateEditorBase` has `CanSaveAsync`, which the page calls before each save, and the protected `CheckAsync`. The Liquid editor runs its check before a save there (D251, Recommended).
- The setup methods `AddMsgHatchTemplatesAdminUI`, `AddTemplateEditor<TEditor>`, `AddLiquidTemplateEditor`, and `AddMsgHatchSubscriptionsAdminUI` extend `IServiceCollection`, as their names start with `Add`, so a host adds the pages with no MsgHatch builder.
- `SubscriptionQuery` has no channel, so the subscription list applies the filter of the channel to the result in the page. The lists page their rows in the page, 25 rows on one page, because the APIs give each list in full.
- The template edit page reads the template with no fallback, and `NotFound` gives a new template with the version 0, so the new template of the list opens the same edit page.
- The subscription edit page decides the type of the settings with `ChannelDescriptor.SettingsType` of `GetChannelsAsync`: a settings type of the host gives `SettingsJson.Read`, and `ChannelSettings` gives `JsonChannelSettings` (D229). So the page never calls `SubscriptionRequest.From` with typed settings that the client host does not register.
- The links and the navigation of the pages use the addresses of the route builders with no leading `/`, so a host with a base path keeps its path.
- The span kind of a quoted text is `LiquidSpanKind.Quoted`, because the analyzer rule CA1720 rejects a member name that is a type name, for example `String`.
- The styles of the Liquid editor are scoped CSS, so a host loads its bundle `{assembly of the host}.styles.css`, and the JavaScript module is a static web asset under `_content/MsgHatch.Templates.Liquid.AdminUI/`.
- The example host reads the token from the required option `AdminUiExample:ApiToken`, and `ApiTokenHandler` adds it to each request. The example has no sign-in, so the handler needs no circuit services accessor of section 4.23.
- The first coverage report showed lines of the admin packages with no test, for example the class of each span kind and the save of an opt-out. So commit 7 adds their tests, and the commits of the plan and of README and AGENTS.md follow it as commits 8 and 9.
- The test of the example host checks the calls over HTTP, because each selection wraps its client in its logging wrapper (D244). The test uses a hand-written fake primary HTTP handler, as the Resend tests do, because NSubstitute cannot configure the protected `SendAsync` (section 5.5).

Done when: the build has no errors and no warnings, all unit tests pass, and `pwsh ./coverage.ps1` passes.

### Phase 29: template preview

Goal: an operator sees a template as it renders, with sample data from the setup of the host, in a read-only view and next to the editor (D254 to D257). The template pages use less vertical space.

Starting point: after the tag `phase-28`, the commits `168e0a8` to `1ee5060` added the development settings and the README of the example app, the console titles of the example apps, the HTML highlight of each html part (D252), and the Liquid password reset email of the example app (D253).

1. Check first, before the code:
   - The placeholder engine and the Liquid engine can render a draft text with a payload through the code of their prepare and render, with no store. If they cannot, the plan changes before the code.
   - An `iframe` with `sandbox=""` and `srcdoc` shows an HTML text with no script in an Interactive Server host, and bUnit renders its `srcdoc` attribute.
2. Add the render of a draft to the engines (D255):
   - `ITemplateEngine<TPayload>.RenderAsync(TemplateLookup lookup, string text, TPayload payload, CancellationToken ct)` gives `ErrorOr<string>`. It renders with the culture of the language of the lookup, the html part with the HTML encoder, and a text part with no encoder (D210). A text that does not parse gives `Errors.TemplateInvalid` with the first issue.
   - The placeholder engine and the Liquid engine implement it. The logging wrapper of an engine logs each render with a new event id, and never logs the text or the payload.
3. Add the sample payload of a registration (D254):
   - `WithTemplateSample<TEvent, TPayload>(this PayloadBuilder<TEvent, TPayload> payload, TPayload sample)` in `MsgHatch.Templates` stores the sample for the template registrations of the payload type and the message type. A second sample of one payload type and one message type gives a setup error (D62).
   - `TemplatePartInfo` gets `HasSample`, so a page shows "No sample data" with no call. `TemplatePartResponse` gets the same field.
4. Add the preview query (D256):
   - `ITemplateQueries.PreviewTemplateAsync(TemplateLookup lookup, string text, CancellationToken ct)` gives `ErrorOr<IReadOnlyList<TemplatePreview>>`, with `TemplatePreview(string Registration, string Text)` for each registration of the part with a sample. It runs `TemplateLookupRules` first (D234), and saves nothing, so it is a query, as the check of D241 is.
   - A part with no sample gives an empty list. A text that does not parse gives `Errors.TemplateInvalid`.
   - The logging wrapper logs it with the next event id after D244. `TemplateQueriesTestsBase` gets its use cases.
5. Add the preview to the HTTP API and the client:
   - `POST {prefix}/{messageType}/{language}/{part}/{kind}/preview` with the text as the body gives a list of `TemplatePreviewResponse` of `MsgHatch.Templates.Api.Contracts`. `TemplateApiRoutes.Preview` holds the route.
   - `ITemplateApi` gets the method, and `HttpTemplateQueries` maps it.
6. Show the metadata of a template on one line: the edit page shows the message type, the language, the company, the part with its kind, and the version in one row of small labels and values, in place of five rows (D257).
7. Add the read-only view page and the buttons of the list (D257):
   - Each row of the template list has the buttons View and Edit.
   - `TemplateViewPage` at `/msghatch/templates/{messageType}/{language}/{part}/{kind}/view?companyId=`, with the route builder `TemplateAdminRoutes.View(TemplateLookup)`, shows the one-line metadata and the stored text read-only with the highlight of D252. It has the buttons Edit and Back. `AddMsgHatchTemplatesAdminUI` keeps the page with `DynamicDependency` (D250).
8. Show the live preview on the view page and the edit page (D257):
   - On a wide screen, the text is on the left and the preview on the right. On a narrow screen, the preview is under the text.
   - The edit page calls the preview 500 ms after the last change of the text, through `TimeProvider`, and a new change cancels the call before it. The view page calls it one time for the stored text.
   - An html preview is an `iframe` with `sandbox=""` and `srcdoc`, so no script of a template runs. A text preview is a `pre`. A part with more than one registration with a sample gets a select of the registration. A part with no sample shows "No sample data". An error of the preview shows its text in place of the preview.
9. Give each email of the example app a sample with `WithTemplateSample`, and add the preview to the README of the example app.
10. Describe the preview in the design: sections 4.10, 4.23, and 4.24.

Tests:
- The engines: a render of a draft with each engine gives the values of the payload, the HTML encoding of an html part, and the culture of the language. A text that does not parse gives `TemplateInvalid`.
- The sample: a second sample of one payload type and one message type gives the setup error. `HasSample` of each part, also over HTTP.
- The preview query, in `TemplateQueriesTestsBase`: a preview for each registration with a sample, an empty list with no sample, `TemplateInvalid`, `ValidationFailed` before any engine call, and no change of the store.
- The HTTP preview: the route, the body, the errors of D233, and the parity of the in-process and the HTTP implementations.
- The pages, with bUnit:
  - The View and Edit buttons of a row, and the addresses of their route builders.
  - The view page with the read-only text and the preview.
  - The edit page with one preview call after a pause.
  - The `sandbox` and the `srcdoc` of the iframe, the select of two registrations, "No sample data", and the error of a preview.
  - The one-line metadata.
- The example app: the preview of the password reset email gives the link of its sample.
- Each new test fails first, or fails with a break of the code that it covers.

Checked facts (2026-11-18), on Windows 11 build 26300 with the .NET SDK 10.0.404:

| Fact | Source |
|---|---|
| The placeholder engine and the Liquid engine render a draft with a payload through the code of their prepare and render, with no store: `TemplateParser.Parse` and `PlaceholderTemplate`, and `LiquidTemplateCache.Parse` and `LiquidTemplate`. A parse error of a draft has the message of the first issue of the check, because the check gives the message of the parse error as its issue. | The code of both engines, and `RenderAsync_DraftThatDoesNotParse_GivesTemplateInvalidWithTheMessageOfTheFirstIssue` |
| In a Blazor Interactive Server app, an `iframe` with `sandbox=""` and `srcdoc` showed the HTML of each version of its text, also after an update of `srcdoc` through the renderer. The script in the HTML did not run: the text that it writes did not show, the title of the page stayed the same, and the page cannot read the document of the frame. bUnit renders `sandbox` with the empty value and `srcdoc` with the text. | A probe app of `dotnet new blazor --interactivity Server` in a scratch folder on `localhost`, in the browser pane, and `Render_HtmlPart_ShowsThePreviewInAnIframeWithAnEmptySandboxAndTheTextAsSrcdoc` |
| The HTTP client implements `ITemplateQueries`, so a new member of the interface needs the client in the same commit, and the use cases of `TemplateQueriesTestsBase` run on the client against the endpoints. | The build of `MsgHatch.Templates.Api.Client` and `HttpTemplateQueriesUnitTests` |
| `TemplateEndpointJson` escapes `é` and `<` in a response, for example `Rendelés`, so the test of the preview route reads the body with the JSON context. | `PostPreview_PathQueryAndBody_Gives200WithThePreviewsOfTheLookupAndTheText` |
| The solution build with `--no-incremental` gave 3 warnings in the new tests of the template admin UI: xUnit1051 two times and CA1826 one time. Commit 8 removes them. | `dotnet build MsgHatch.slnx --no-incremental` at commit 7 |
| The 34 unit test projects pass 4,614 tests and skip 1 test: the path limit test of the named pipe signal on Windows. At `1ee5060`, they passed 4,496 tests. `MsgHatch.Templates.Tests.Unit` passes 398 tests, `MsgHatch.Templates.Api.Client.Tests.Unit` 132, `MsgHatch.Templates.AdminUI.Tests.Unit` 104, `MsgHatch.Templates.Liquid.Tests.Unit` 98, `MsgHatch.Templates.AspNetCore.Tests.Unit` 79, `ExampleApp.Tests.Unit` 43, and `MsgHatch.Templates.Api.Contracts.Tests.Unit` 18. The line coverage of the production code is 98.8 %: `MsgHatch.Templates` 100 %, `MsgHatch.Templates.Liquid` 100 %, `MsgHatch.Templates.Liquid.AdminUI` 99.3 %, `MsgHatch.Templates.Api.Contracts` 97.2 %, `MsgHatch.Templates.AdminUI` 96.7 %, `MsgHatch.Templates.AspNetCore` 95.0 %, and `MsgHatch.Templates.Api.Client` 89.9 %. The solution build with `--no-incremental` has no errors and no warnings at commit 8. | `pwsh ./coverage.ps1` on the content of commit 9, and `dotnet build MsgHatch.slnx --no-incremental` |
| Each new test failed with a break of the code that it covers: the render of a draft in both engines and its logging wrapper (20 tests), the sample and `HasSample` (7 tests, in two runs), the preview query, its wrapper, its route, its bodies, and its client (74 test rows, in three runs), the metadata line (1 test), the view page, its route builder, the buttons of the list, and `ReadOnly` (13 tests, in two runs), the preview pane and the pages with the preview (15 tests, in three runs), and the samples of the example app (2 tests). | The breaks, each one reverted after its run |

Checks that the phase did not do:
- A run of `AdminUiExample` against a live `ExampleApp`. The probe app checked the `iframe`, and the bUnit tests check the pages with substitutes.
- A trimmed WebAssembly host of the template admin UI. `AddMsgHatchTemplatesAdminUI` keeps `TemplateViewPage` with `DynamicDependency`, as phase 28 checked for the other pages.

Commits:
1. "Render a draft text with a payload in the template engines"
2. "Add the sample payload of a template registration"
3. "Add the preview of a template draft to the queries, the HTTP API, and the client"
4. "Show the metadata of a template on one line"
5. "Add the read-only view page and the View and Edit buttons of the template list"
6. "Show the live preview on the template view page and edit page"
7. "Give the emails of the example app their samples"
8. "Remove the analyzer warnings of the template admin UI tests"
9. "Describe the template preview in the design"
10. "Record the commits and the checked facts of phase 29 in the plan"
11. "Record phase 29 as done in README and AGENTS.md"

Commit 1 holds item 2 (`a09761d`), commit 2 item 3 (`6267027`), commit 3 the items 4 and 5 (`7f8c3f7`), commit 4 item 6 (`0383a21`), commit 5 item 7 (`271dd81`), commit 6 item 8 (`9a5c885`), commit 7 item 9 (`5a318a9`), commit 8 the fix of the warnings of the commits 5 and 6 (`aab863f`), and commit 9 item 10 (`0d6f603`). Item 1 is a check, and its results are in the checked facts. The unit test projects of the changed code passed at each commit. The phase adds no project and no package.

The implementation changed the plan in these points:
- The plan had the preview query and its HTTP API in two commits. The HTTP client implements `ITemplateQueries`, so the build needs the new member in the client in the same commit, and the shared use cases run on the client against the endpoints. So commit 3 holds both items, and each commit stays green. Commit 8 removes the analyzer warnings that the first solution build showed, so the phase still has 11 commits.
- The preview query logs with the events 2029 and 2030, the next ids after D244, and the render of a draft in the engine wrapper with the events 2031 and 2032.
- Both wrappers log `Errors.TemplateInvalid` of a draft at the Information level, because the edit page asks for a preview after each pause in typing (D258, Recommended).
- `ITemplateEngine<TPayload>.RenderAsync` gives a `CultureNotFoundException` for a language that is not the name of a culture, as an argument error, because the template queries check the language before they call an engine.
- A part with no sample gives an empty list with no parse of the text. The check of the editor shows the problems of such a text.
- `TemplatePartInfo` and `TemplatePartResponse` get `HasSample` as a required positional field, so the JSON of a part has the required property `hasSample`.
- The preview pane asks for the first text at once, and for each later text after the pause. So the view page calls one time, and the edit page shows the preview of the stored text with no wait.
- The view page reads the parts of the message type for `HasSample`, so an error of the parts is an error of the page, as on the edit page. A missing template shows the text of `NotFound` and the link back to the list.
- The view page shows the stored text in `HighlightedTextArea` with the new parameter `ReadOnly`, so the text has the same look and the same scroll as in the editor. A text part is one segment with no class. The internal `TextSegments` gives the segments to the plain editor and the view page.
- `TemplateMetadata` and `TemplatePreviewPane` are public components of `MsgHatch.Templates.AdminUI`, as `HighlightedTextArea` is, so the view page and the edit page share them, and a host can use them on a page of its own.

Done when: the build has no errors and no warnings, all unit tests pass, and `pwsh ./coverage.ps1` passes.

### Phase 30: layout of the admin pages

Goal: the admin pages of the subscriptions and the templates have one layout (D259 to D262). A list scrolls in its own area, a new item opens in a dialog from a button above the list, each dialog and each page has Cancel or Back on the bottom left and its main action on the bottom right, and a delete shows the details of the item before it runs.

1. Check first, before the code:
   - A Razor component can be internal. If it cannot, the create form of a subscription is one public component with the id of the subscription as its parameter, because the form model `SubscriptionForm` is internal.
   - In a host whose layout has the height of the window, a list page with a fixed header and a table area with `overflow-y: auto` and `min-height: 0` scrolls only the table, and a `th` with `position: sticky` stays visible.
2. Add the shared parts to `MsgHatch.AdminUI` (D261):
   - `AdminActions` puts its `Start` content on the bottom left and its `End` content on the bottom right.
   - `AdminDialog` is a Bootstrap modal with a title, a body, and a footer of `AdminActions`.
   - `DeleteDialog` uses `AdminDialog`, with Cancel on the left and Delete on the right.
3. Put the actions of the pages into `AdminActions` (D261): the view page has Back on the left and Edit on the right. The template edit page and the subscription edit page have Back on the left, and Delete and Save on the right.
4. Show the details of an item in the delete question (D262): `DeleteDialog` gets the content `Details`, and says that the delete cannot be undone. The template pages show the metadata line of the template and what a send uses after the delete. The subscription page shows the owner, the message type, the kind, the channel, the recipients, the mandatory flag, and the revision.
5. Add the list layout (D259):
   - `AdminListPage` of `MsgHatch.AdminUI` has a header with the title, a toolbar with the filters on the left and the actions on the right, and the list.
   - `AdminList` puts its table in an area that scrolls by itself, with a sticky row of column names, and its pager under the area.
   - The template list, the subscription list, and the message types page use `AdminListPage`.
   - `AdminUiExample` gets a layout with the height of the window, and its main area fills the rest.
6. Open a new template in a dialog (D260): the button "+ New template" on the right of the toolbar opens a dialog with the part, the language, and the company. Cancel is on the left, and "Open the editor" on the right.
7. Create a subscription in a dialog (D260):
   - The public component `SubscriptionEditor` holds the form, the save, and the delete of one subscription. The edit page shows it, and the button "+ New subscription" on the right of the toolbar shows it in a dialog with Cancel on the left and Create on the right.
   - A create in the dialog closes the dialog and loads the list again.
   - `SubscriptionAdminRoutes.New` gives the address of the list with the query `new=true`, which opens the dialog. The page address `/msghatch/subscriptions/new` goes away. The HTTP API does not change.
8. Describe the layout in section 4.24 of the design, with the layout that a host gives its main area.

The owner added these items during the phase:
9. The titles "Editor" and "Live preview" over the two columns of the template edit page.
10. Delete on each row of the template list and of the subscription list, with the question of D262 (D263).
11. The actions of an edit page and of the view page on screen: `AdminEditPage` with a body that scrolls by itself and the bar of the actions under it (D264).
12. The filters of a list in a framed bar with small uppercase labels (D265).
13. The dark mode of the example host, and a white background of the html preview (D266).
14. `ITemplateStore.RenderVersion`, so the store that knows the format of its versions gives the text of a version for a user, through the HTTP API too (D267).

Tests, with bUnit:
- `AdminActions`, `AdminDialog`, and `DeleteDialog`: the content on the left and on the right, the title, and the details. A delete runs only after the user confirms it.
- The buttons of each page in their places, with their addresses.
- The details of a template and of a subscription in the delete question.
- `AdminList` and `AdminListPage`: the scroll area, the pager under it, the toolbar with the actions on the right.
- The dialog of a new template: the navigation to the edit page, Cancel, and the error of a lookup that breaks a rule.
- The dialog of a new subscription: a create closes the dialog and loads the list, Cancel, an error of the create, and the query `new=true`.
- Each new test fails first, or fails with a break of the code that it covers.

Checked facts (2026-11-18), on Windows 11 build 26300 with the .NET SDK 10.0.404:

| Fact | Source |
|---|---|
| A Razor component is always public. A code-behind `internal partial class` gives CS0262, and a public parameter of an internal type gives CS0053. So the create form of a subscription is the public component `SubscriptionEditor` with the parameter `Guid? Id`, and `SubscriptionForm` stays internal. | The build of a probe component in a scratch folder |
| In a window of 916 px, a layout of one column with `vh-100`, a main area with `flex-grow: 1`, `min-height: 0`, and `overflow-y: auto`, a list page column, and a table area with `overflow-y: auto` and `min-height: 0` scrolled only the table area, with a height of 743 px. The document and the main area did not scroll, the header stayed under the navigation bar, and the pager ended at the bottom of the window. After a scroll of 2,000 px, the `th` with `position: sticky` stayed at the top of the table area. | A static page of the probe app on `localhost`, measured in the browser pane |
| In the probe app, the script of the color mode set `data-bs-theme` from the system setting when the local storage had no choice (dark on the probe machine). A click switched to light and stored it, a new apply kept the stored choice, and a second click gave dark. | A static page of the probe app on `localhost`, in the browser pane |
| A test server in the environment `Testing` answers 200 with an empty body for the fingerprinted address of `theme.js`, because the static files of the app project are not in the content root of the test. So the test of the example host checks the page: the script tag before the styles of Bootstrap, and the button of the color mode. | `Get_Page_LoadsTheThemeScriptBeforeTheStylesAndHasTheThemeSwitch` |
| The 34 unit test projects pass 4,670 tests and skip 1 test: the path limit test of the named pipe signal on Windows. At `224c695`, they passed 4,614 tests. `MsgHatch.Templates.Tests.Unit` passes 409 tests, `MsgHatch.Templates.Api.Client.Tests.Unit` 134, `MsgHatch.Templates.AdminUI.Tests.Unit` 118, `MsgHatch.Subscriptions.AdminUI.Tests.Unit` 48, `MsgHatch.AdminUI.Tests.Unit` 40, and `AdminUiExample.Tests.Unit` 4. The line coverage of the production code is 98.8 %: `MsgHatch.AdminUI` 100 %, `MsgHatch.Templates` 100 %, `MsgHatch.Templates.Api.Contracts` 97.2 %, `MsgHatch.Templates.AdminUI` 97.0 %, `MsgHatch.Subscriptions.AdminUI` 96.1 %, `MsgHatch.Templates.AspNetCore` 95.0 %, and `MsgHatch.Templates.Api.Client` 89.9 %. The solution build with `--no-incremental` has no errors and no warnings at commit 15. | `pwsh ./coverage.ps1` on the content of commit 15, and `dotnet build MsgHatch.slnx --no-incremental` |
| Each new test failed with a break of the code that it covers, or with the code before its commit: the shared dialog and bottom bar (6 tests, in two runs), the buttons of the pages (3 tests), the details in the delete question (4 tests), the list layout (6 tests, in two runs), the dialog of a new template (2 tests), the dialog of a new subscription (7 tests, in two runs), the column titles (1 test), the delete on a list row (8 tests), the actions on screen (6 tests), the filter bar (2 tests), the dark mode (1 test), and the version text (14 tests and test rows). | The breaks, each one reverted after its run |

Checks that the phase did not do:
- A run of `AdminUiExample` against a live `ExampleApp`. The probe app checked the scroll of the layout and the script of the color mode, and the bUnit tests check the structure of the pages.
- The bar of the editor in the wide dialog of a new subscription with Bootstrap in a browser. The body of `AdminDialog` is a flex column, and the structure is the one that the probe checked for a page.
- The look of each page in the dark mode in a browser. The parts use only the color variables of Bootstrap.

Commits:
1. "Plan phase 30: the layout of the admin pages"
2. "Add the shared dialog and the bottom bar of the admin UIs"
3. "Put the actions of the admin pages into the bottom bar"
4. "Show the details of an item in the delete question"
5. "Scroll only the list on the admin list pages"
6. "Open a new template in a dialog from the button above the list"
7. "Create a subscription in a dialog from the button above the list"
8. "Describe the layout of the admin pages in the design"
9. "Add the titles Editor and Live preview on the template edit page"
10. "Delete a template or a subscription from its row of the list"
11. "Keep the actions of the edit pages on screen"
12. "Set the filters of a list apart in a framed bar"
13. "Add the dark mode to the example host of the admin UIs"
14. "Render the version of a template in the store that knows its format"
15. "Describe the later additions of the owner to phase 30 in the design"
16. "Record the commits and the checked facts of phase 30 in the plan"
17. "Record phase 30 as done in README and AGENTS.md"

Commit 1 holds the plan and the decisions (`674003a`), commit 2 item 2 (`bf82497`), commit 3 item 3 (`be85e26`), commit 4 item 4 (`3bb4179`), commit 5 item 5 (`2f751f8`), commit 6 item 6 (`0b1fea4`), commit 7 item 7 (`71abdcf`), commit 8 item 8 (`4179493`), commit 9 item 9 (`f92f3b4`), commit 10 item 10 (`534d38a`), commit 11 item 11 (`b020bcc`), commit 12 item 12 (`df1365c`), commit 13 item 13 (`5d36aee`), commit 14 item 14 (`05caa12`), and commit 15 the decisions D263 to D267 in the design (`91aa64b`). Item 1 is a check, and its results are in the checked facts. The unit test projects of the changed code passed at each commit. The phase adds no project and no package.

The implementation changed the plan in these points:
- `AdminDialog` has no footer when a dialog gives it no `Start` and no `End`, because `SubscriptionEditor` has its own bar of `AdminActions`, also in the dialog.
- `SubscriptionEditor` has the parameters `Cancelled` and `Created`. With no `Cancelled`, its left button is the link back to the subscriptions. With no `Created`, a create opens the edit page of the new subscription. The edit page gives neither, and the dialog gives both.
- The parameter `Id` of `SubscriptionEditPage` is a `Guid` now, because the page has only the address with an id. The two tests of a create moved from the edit page to the dialog of the list.
- A Cancel of the dialog that the query `new=true` opened gives the list address with no query, so a reload of the page does not open the dialog again.
- The bar of the editor shows Save and Delete only when the load had no error, because the editor makes an empty form also after a load error.
- The link back to the subscriptions of the message types page is a link next to its title. The Edit link of a subscription row is a small button, as on the template list. The filters of the toolbars use the small Bootstrap controls.
- The verification page keeps its layout, because it is a form with a report and no list page. The scroll area of `AdminList` grows only in a flex column, so on that page it is a normal block.
- The dialog of a new template shows an error of the parts, or no declared part, in its body, and then has no "Open the editor".
- The owner added the items 9 to 14 during the phase, so the phase has 17 commits in place of 10.
- The view page has no column titles, because the owner asked for them on the editor pages.
- `ITemplateStore.RenderVersion` is a default interface member, so a store of a host needs no change, and gives the number. The logging wrappers forward it, because the default of a wrapper would hide the text of the inner store.
- A new template on the edit page shows the version 0, because no store rendered a text for it.

Done when: the build has no errors and no warnings, all unit tests pass, and `pwsh ./coverage.ps1` passes.

### Phase 31: registry queries and pages

Goal: an operator sees the channels of a host with the message types of each channel, and the list of the message types, in the admin UI (D268 to D273). The registry gets a query interface, contracts, and an HTTP client, in the same way as the subscriptions and the templates in phases 26 to 28.

Starting point: after the tag `phase-30`, the commits `094e690` to `79c69d5` (tag `phase-30.1`) added these changes:
- A border on the list area of the admin pages.
- The mark of the open page in the menu of the example host.
- More list filters: the language, the part, and the kind of a template, and the kind and the mandatory flag of a subscription. The filters apply to the loaded list in the page.

The owner decided that the lists need no filters and no paging on the server, because each filter works in the page.

1. Check first, before the code:
   - `MsgHatch.AspNetCore` references only `MsgHatch.Domain`. A reference to `MsgHatch.Api.Contracts` gives no cycle, and the generator of D236 finds the list properties of `RegistryEndpointJson` in the contracts assembly. If it does not, the plan changes before the code.
   - The router of a host finds the pages of `MsgHatch.AdminUI` when the host adds its assembly to `AdditionalAssemblies`, and the shared parts with no route do not change.
2. Add the registry query interface (D268, D269):
   - `IRegistryQueries`, `RegistryChannel`, and `RegistryMessageType` in `MsgHatch.Domain.Management`.
   - `RegistryManager` joins `IMessageRegistry` with the revisions of `IRegistryWriter.GetEntriesAsync`. With no writer, each revision is 0. An error of the writer is the error of the query.
   - The setup registers it with `AddDefaultTenantService` and a selection method of D231.
   - The logging wrapper logs each call with its count or its error and its duration, as the other query wrappers do (D244). It uses the events 2909 to 2912, the next ids after 2908.
3. Move the registry bodies and route paths to the shared contracts (D270):
   - The six registry bodies and `RegistryEndpointJson` move to `MsgHatch.Api.Contracts`, with public `From` methods. The two GET bodies get their mappings back to the domain types.
   - `RegistryApiRoutes` in `src/Shared` holds `/channels`, `/channels/{channel}`, `/message-types`, and `/message-types/{type}`. `MsgHatch.AspNetCore` compiles it.
   - The GET routes call `IRegistryQueries` of `TenantKey.None`. The paths and the bodies do not change.
4. Write the errors of the registry routes with `ErrorProblems` (D233):
   - The problem body gets `errorType` and `detail`.
   - `ValidationFailed`, `NotFound`, and `Conflict` keep their status codes. The other error types get the status of `ErrorProblems.StatusCodeOf` in place of 500, for example 503 for `Unavailable`.
5. Add the client package `MsgHatch.Api.Client` (D271):
   - `IRegistryApi` with `[HttpApi(typeof(RegistryEndpointJson))]`, the adapter `HttpRegistryQueries`, `RegistryApiClientOptions` with its validator, and `UseRegistryApiClient`.
   - The package has only the package `Microsoft.Extensions.Http`, and sets `IsAotCompatible` (D237).
   - The solution gets the package and `tests/MsgHatch.Api.Client.Tests.Unit`.
6. Add the channel pages to `MsgHatch.AdminUI` (D272):
   - `RegistryAdminRoutes`, `ChannelListPage` with `AdminListPage`, and `ChannelViewPage` with `AdminEditPage`.
   - The features show as Bootstrap badges, and the enabled flag as "Yes" or "No".
   - `AddMsgHatchAdminUI` keeps both pages in a trimmed host with `DynamicDependency` (D250).
7. Add the message types page to `MsgHatch.AdminUI` (D273):
   - `MessageTypeListPage` at `/msghatch/message-types`, with the route builder `RegistryAdminRoutes.MessageTypes`, uses `AdminListPage`.
   - Each row shows the message type, its display name, its description, its enabled flag, and its channels. Each channel links to its view page, and a channel in `DisabledChannels` has the mark "disabled".
   - `AddMsgHatchAdminUI` keeps the page with `DynamicDependency` (D250).
8. Rename the subscription page "Message types" to "Applied subscriptions" (D273): its title, the link on the subscription list, and the links of the example host. The address, the class `MessageTypesPage`, and `SubscriptionAdminRoutes.MessageTypes` stay.
9. Show the registry pages in the example admin host:
   - `UseRegistryApiClient` with the token handler, and the section `MsgHatch:RegistryApiClient` with `http://localhost:5080/` and the prefix `/api/registry`.
   - `AddMsgHatchAdminUI`, and the assembly of `MsgHatch.AdminUI` in `AdminAssemblies`.
   - The menu and the home page get the links "Channels" and "Message types" after "Templates". The menu marks "Channels" on the list and on the view page.
   - The registry routes of `ExampleApp` need the role `RegistryAdmin` (D170). So the token command in the README of the example app gets `--role RegistryAdmin`, and its table of problems names the 403 of a token with no such role. AGENTS.md says the same.
10. Describe the phase in the design: the sections 4.20, 4.23, and 4.24, and the filters of `phase-30.1` in section 4.24.

Tests:
- `RegistryQueriesTestsBase<T>` in `MsgHatch.Domain.Tests.Unit`:
  - The channels and the message types of the code, each with the revision 0.
  - The stored display name, description, and enabled flag, with their revisions.
  - The disabled channels of a message type.
  - The error of the writer.
  - `RegistryManager` gets the base with one subclass, and the HTTP client against the registry routes gets it with another one.
- The logging wrapper: one event for each call, with the count or the error type.
- The contracts: each GET body maps to its domain type and back, and a name that is not valid gives `SerializationFailed`.
- The registry routes: the problem body of D233 for each error type, and the GET routes through `IRegistryQueries`. The existing tests of the paths and the status codes stay green.
- The client: the options and their validation, the setup error of a second selection, `Unavailable` for a network error, and `Rejected` for 401 and 403.
- The pages, with bUnit:
  - The rows of the list, with the address of View, the empty state, and the texts of `Unavailable` and `Rejected`.
  - The fields and the badges of the view page, and the three states of a message type on the channel.
  - `NotFound` with the link back, and the addresses of `RegistryAdminRoutes`.
  - The rows of the message types page, the links of the channels, and the mark of a disabled channel.
  - The title "Applied subscriptions" of the subscription page, and its link on the subscription list.
- The example host: the registry client sends its calls with the token, and the menu marks each new page and "Applied subscriptions".
- Each new test fails first, or fails with a break of the code that it covers.

Checked facts (2026-11-18), on Windows 11 build 26300 with the .NET SDK 10.0.404:

| Fact | Source |
|---|---|
| `MsgHatch.AspNetCore` referenced only `MsgHatch.Domain`, and `MsgHatch.Api.Contracts` references only `MsgHatch.Domain`, so the new reference gives no cycle. The generator of D236 found the properties `IReadOnlyList<RegistryChannelResponse>` and `IReadOnlyList<RegistryMessageTypeResponse>` of a public JSON context in a referenced assembly: a client project with `IsAotCompatible` and an `[HttpApi]` interface built with no warning. With one list removed from the context, it gave `MSGHATCH010`. | Two probe projects in a scratch folder with the SDK of `global.json`, and the project files |
| A Blazor Interactive Server host on .NET 10 found the page of each of two libraries in `AdditionalAssemblies`, also of the library that holds the shared parts. A shared part with no route showed on a page of the host and on the pages of both libraries, and an unknown address gave 404. In the example host, the addresses `/msghatch/channels`, `/msghatch/channels/Email`, and `/msghatch/message-types` show their pages with the menu link of the page marked. | A probe app in a scratch folder on `localhost`, and `Get_Page_MarksOnlyTheMenuLinkOfItsPageAsActive` |
| A scratch folder with no `global.json` uses the SDK 11.0.100 of the machine, and `dotnet new blazor` then makes a `net11.0` project. So the probes ran again with the `global.json` of the repository. | `dotnet --list-sdks` and the project file of the probe |
| Over HTTP, an error of the registry writer gives 503 with the member `errorType` of `Unavailable`, and the client gives `Errors.Unavailable`. So the use cases of `RegistryQueriesTestsBase` give the same result for `RegistryManager` and for the client. | `Queries_WriterError_GiveTheErrorOfTheWriter` in `RegistryManagerUnitTests` and in `HttpRegistryQueriesUnitTests` |
| The Razor compiler removes the white space between the elements of a loop, so the text of a cell with two channel links is `EmailSms`. The margin class `me-2` separates them on screen, and the test of the message types page reads the channel elements. | `Render_MessageTypes_ShowsEachRowWithItsDisplayNameItsDescriptionItsEnabledFlagAndItsChannels` |
| A copy of `AdminUiExample` on port 5091 called a copy of `ExampleApp` on port 5081 with a token of the roles `TemplateAdmin` and `RegistryAdmin`. The channel list showed Email, SignalR, and Broker with their features as badges and 6, 2, and 2 message types. The view page of Email showed its six message types. After a PUT that disabled the message type B, and a PUT that disabled the channel Email for C, the view page showed "Message type disabled" for B and "Disabled on this channel" for C, and the message types page showed the mark "disabled" on Email of C. A channel link of the message types page opened the view page of SignalR. The menu marked "Channels" on the list and on the view page, "Message types" on the message types page, and "Applied subscriptions" on the renamed subscription page. | The browser pane, on copies of both apps that ran from a scratch folder with their own SQLite file |
| The 35 unit test projects pass 4,779 tests and skip 1 test: the path limit test of the named pipe signal on Windows. The phase adds 99 tests and test rows. `MsgHatch.Domain.Tests.Unit` passes 1,467 tests, `MsgHatch.AspNetCore.Tests.Unit` 121, `MsgHatch.Api.Contracts.Tests.Unit` 72, `MsgHatch.AdminUI.Tests.Unit` 58, `MsgHatch.Subscriptions.AdminUI.Tests.Unit` 51, `MsgHatch.Api.Client.Tests.Unit` 25, and `AdminUiExample.Tests.Unit` 13. The line coverage of the production code is 98.7 %: `MsgHatch.AdminUI` 100 %, `MsgHatch.Api.Contracts` 100 %, `MsgHatch.Domain` 99.6 %, `MsgHatch.AspNetCore` 97.9 %, `MsgHatch.Subscriptions.AdminUI` 96.2 %, and `MsgHatch.Api.Client` 80.5 %. Each new class has 100 %. The lines with no test in `MsgHatch.Api.Client` are the shared sources `ErrorProblems.StatusCodeOf` and `ApiResults.One`, which have no caller in the package, as `ErrorProblems` has 29.4 % in `MsgHatch.Templates.Api.Client` too. The solution build with `--no-incremental` has no errors and no warnings at commit 10. | `pwsh ./coverage.ps1` on the content of commit 10, and `dotnet build MsgHatch.slnx --no-incremental` |
| The new tests failed with a break of the code that they cover, or before the code of their commit existed. The breaks failed: the registry queries, the default, the selection, and the logging wrapper (12 tests, in three runs), the mappings of the bodies and the GET routes through the queries (8 tests and test rows), the problem bodies of the registry routes (26 test rows, in two runs), the client (20 tests and test rows, in two runs), the channel pages (6 tests), and the message types page (5 tests). The title "Applied subscriptions" (3 tests) and the example host (4 tests and test rows) failed with the files before their commit. | The breaks, each one reverted after its run |

Checks that the phase did not do:
- A trimmed WebAssembly host of the core admin UI. `AddMsgHatchAdminUI` keeps the three pages with `DynamicDependency`, as phase 28 checked for the other packages, and a test checks that the attributes name each routed page of the assembly.
- The registry pages in the dark mode in a browser. They use only the color variables of Bootstrap.
- A token with no role `RegistryAdmin` against a live `ExampleApp`. The bUnit tests check the text of `Rejected`, and the client tests check that 403 gives `Rejected`.

Commits:
1. "Plan phase 31: the registry queries and pages"
2. "Add the registry query interface"
3. "Move the registry bodies and route paths to the shared contracts"
4. "Write the errors of the registry endpoints with ErrorProblems"
5. "Add the HTTP client of the registry queries"
6. "Add the channel list and view pages to the core admin UI"
7. "Add the message types page to the core admin UI"
8. "Rename the subscription message types page to Applied subscriptions"
9. "Show the registry pages in the example admin host"
10. "Describe the registry queries and pages in the design"
11. "Record the commits and the checked facts of phase 31 in the plan"
12. "Record phase 31 as done in README and AGENTS.md"

Commit 1 holds the plan and the decisions (`2d8a9bc`), commit 2 item 2 (`cc72c44`), commit 3 item 3 (`fc69e91`), commit 4 item 4 (`486a0f3`), commit 5 item 5 (`bdaa806`), commit 6 item 6 (`2b7a438`), commit 7 item 7 (`4679f4a`), commit 8 item 8 (`5016572`), commit 9 item 9 (`d36332c`), and commit 10 item 10 (`62d5ee6`). Item 1 is a check, and its results are in the checked facts. The unit test projects of the changed code passed at each commit. The phase adds the package `MsgHatch.Api.Client` and the test project `MsgHatch.Api.Client.Tests.Unit`.

The implementation changed the plan in these points:
- `RegistryQueriesTestsBase<T>` starts its hosts through `IRegistryQueriesTestHost<T>` of the domain tests, and not through a type with the name `IManagementTestHost<T>`. The template tests import the test types of the domain and of the templates, so a second type of that name gives an ambiguous name. `RegistryTestHost.AddRegistry` adds the registry of the tests to a given service collection, so the web server of the client tests uses the same registry.
- The default of `AddMsgHatch` and the selection of `UseRegistryQueries` are scoped tenant services, as the template queries are, because the HTTP client needs a DI scope for its handlers.
- The events 2909 to 2912 are in a second part of the class `Log` of the namespace `MsgHatch.Domain.Management`, in `Log.Registry.cs`, because the wrapper is in that namespace.
- `DeliveryRoutes.WithBodyAsync` gets an optional writer of the problem, so the 400 of a registry body that is not valid JSON also has `errorType`. The delivery routes keep their problems until their own phase.
- Item 4 changes the status of `Unavailable` from 500 to 503. So three tests of the registry routes changed: the rows of the error statuses have each error type, `Delete_WriterError_GivesItsStatus` expects 503, and `Get_WriterError_Gives500` is now `Get_WriterUnavailable_Gives503`. The tests of the paths and of the other status codes did not change.
- The view page finds the channel of its address in the result of `GetChannelsAsync` by its name, because the queries have no read of one channel. An unknown channel gives a `NotFound` error that the page makes. A page that needs the channels and the message types calls both queries, and an error of either query is the error of the page.
- A disabled message type has the state "Message type disabled" also when the channel is in its `DisabledChannels`, because a disabled message type sends on no channel.
- A channel with no feature shows no badge. A test checks that the `DynamicDependency` attributes of `AddMsgHatchAdminUI` name each routed page of the assembly, so a new page with no attribute fails the test.
- Commit 10 also changes the sections 4.2, 4.7, and 5.1 of the design and the list of the AOT packages, because they name the packages, the setup methods, and the files.

Done when: the build has no errors and no warnings, all unit tests pass, and `pwsh ./coverage.ps1` passes.

### Phase 32: channel state and page order

Goal: an operator sees if each channel is up or down, since when, and the state changes of the last 30 days. The admin menu has the order Channels, Message types, Subscriptions, Templates, and Verification. The message types page shows the number of subscriptions of each owner level, and the page "Applied subscriptions" goes away (D274 to D279).

Starting point: the commits of phase 31, `2d8a9bc` to `df73dab`. The owner looked at the pages of phase 31 and asked for these changes:
- "change page order to: Channels, Message Types, Subscriptions, Templates and Verification".
- The applied subscriptions as a column of the message types page, with "one number per each SubscriptionLevel".
- An up or down indicator, an "up since" time with the time since the last down state, and a button History with the down and restored events "in a dialog, or a timeline with segments colored red where the channel was down".

The owner chose "Registry queries" for the channel state, and "Timeline and list" for the history. Then the owner asked to "add the count links to the filtered subscription list and the channel state to the channel view page". So the plan has two commits: `0a0b933` and the commit after it.

The check of item 1 found two limits. MongoDB has no transaction on a standalone server ("MongoDB standalone deployments do not support transactions", the MongoDB manual), and the stores use no transaction (D186). A file system cannot change two files in one atomic step. The owner chose: "detect whether the Mongo connection supports transactions and use the best option for the circumstances, transaction if available". Items 3 and 6 and D277 have the result.

Then the owner changed the design of the channel state: "Refactor the ChannelDown in a way that we have ChannelState records, each recorded only if the state changed from the previous one, so when it got down or get back up. By such an insert-only table we can track history of each channel." And: "Each channel state change record should store not just the timestamp, but the duration from the previous state change event of the same channel, so it would be easy to query and report the availability time percentage." The owner chose to update `NextCheckAtUtc` on the latest row, to keep all rows, to do no data migration ("the library is still in beta"), and to rebuild the commits of the phase after `aeb6c50`. So items 2 to 9 and D277 to D279 describe the insert-only rows, and no store needs a transaction.

1. Check first, before the code:
   - Each of the five outbox stores can write or remove the down state of a channel, and add its event, in one atomic operation: the in-memory store, the file store, a SQLite transaction, an EF Core transaction on SQL Server, and MongoDB. For MongoDB, the check finds how the store keeps two writes atomic now, also on a server with no replica set. If a store cannot do it, the plan changes before the code.
   - The SQLite and EF Core stores add a table to an existing database with their current schema setup, and keep the data. The check names the step of the schema version that each store needs.
   - Bootstrap 5.3.3 of the example host has the class `progress-stacked`.
2. Keep the state of a channel as insert-only rows in the outbox dispatch store (D277):
   - `ChannelState` and `ChannelStateKind` with `Up` and `Down` in `MsgHatch.Domain.Dispatch`. A row has the channel, its version, the kind, `SinceUtc`, `PreviousKind`, `PreviousDuration`, and for `Down` the reason, `NextCheckAtUtc`, the error code, the error name, and the note. `ToDown()` gives the `ChannelDown` of a `Down` row. `ChannelDown` stays as the view of the current down state.
   - The version of the first row of a channel is 1, and each next row has the version of the latest row plus 1. A store keeps a unique key on the channel and the version. `PreviousKind` is the kind of the previous row, and `PreviousDuration` is `SinceUtc` minus the `SinceUtc` of the previous row in whole milliseconds, 0 for a negative difference. Both are null for the version 1. So a report of the up time is `SUM(PreviousDurationMs) WHERE PreviousKind = 'Up'`.
   - A store adds a row only when the state changes (`ChannelState.AfterDown` and `ChannelState.AfterUp`): `SetChannelDownAsync` with the reason `Errors` only on an up channel, with the reason `Operator` unless the channel is paused, and `SetChannelUpAsync` only on a down channel with the given reason. `SetChannelUpAsync` gets `DateTime upAtUtc`.
   - A change is one insert. When another caller added the same version first, the insert adds nothing, and the store reads the new latest row and tries the change again, at most `ChannelState.ChangeTries` times, 3 (`ChannelState.Change` and `ChangeAsync`). After the last try, it gives `Errors.Conflict`.
   - The current state is the latest row. `ClaimDueAsync` and `GetDownChannelsAsync` read it. `TryStartChannelCheckAsync` updates only `NextCheckAtUtc` of the latest row.
   - The new `GetChannelStatesAsync(DateTime fromUtc, CancellationToken ct)` gives the rows of all channels at or after `fromUtc`, and the row of each channel that was current at `fromUtc`, newest first. A store keeps all rows.
   - The in-memory store, `TenantOutboxDispatchStore`, the logging wrapper with the events 1320 and 1321, `OutboxChannelWork`, and `DeliveryManager` change. `TenantOutboxDispatchStore` calls the system store. The callers give the time of `TimeProvider`.
3. Keep the channel states in the file store: one file `{RootPath}/outbox/{channel}/states/{version:D10}.json` for each row, which the store creates with `FileMode.CreateNew`, so of two parallel inserts of one version only the first succeeds. The file `down.json` goes away (D277).
4. Keep the channel states in the SQLite store, in the new table `MsgHatchChannelState` with the primary key `(Channel, Version)` and `INSERT OR IGNORE`. The table `MsgHatchChannelDown` goes away (D277).
5. Keep the channel states in the EF Core store, in the same table on SQL Server, with an insert `WHERE NOT EXISTS` with `UPDLOCK, HOLDLOCK` (D277).
6. Keep the channel states in the MongoDB store, in the new collection `MsgHatchChannelState` with the unique index `UX_MsgHatchChannelState_Version` on `(channel, version)` and one `insertOne`, with no transaction. The collection `MsgHatchChannelDown` goes away (D277).
7. Add the channel state to the registry queries (D278):
   - `RegistryChannel` gets `ChannelState? Current`, the latest row of the channel, with its kind, its start, and the kind and the duration of the previous state. It is null for a channel with no row, which was never down.
   - `IRegistryQueries.GetChannelHistoryAsync(ChannelId channel, CancellationToken ct)` gives the rows of one channel in the window of `ChannelState.HistoryWindow`, 30 days, and the row that was current at the start of the window, newest first. A channel that is not registered gives `Errors.NotFound`.
   - `RegistryManager` reads `GetChannelStatesAsync` of `IOutboxDispatchStore` with the start of the window from `TimeProvider`. An error of the store is the error of the query.
   - The logging wrapper logs the history call with the events 2913 and 2914.
8. Add the channel history to the registry contracts, route, and client (D278):
   - `ChannelDownResponse` moves from `MsgHatch.AspNetCore` to `MsgHatch.Api.Contracts`, with a public `From` and the mapping back. The delivery body `ChannelStatusResponse` keeps it.
   - `RegistryChannelResponse` gets `current`, the latest row as a `ChannelStateResponse`. The new `ChannelStateResponse`, with `previousKind`, and `previousDurationMs` as a number, and its list go into `RegistryEndpointJson`.
   - `RegistryApiRoutes.ChannelHistory` holds `/channels/{channel}/history`. The GET route calls `IRegistryQueries` of `TenantKey.None`, in the registry group with its policy.
   - `IRegistryApi` and `HttpRegistryQueries` get the history call.
9. Show the channel state and history on the channel pages (D279):
   - The column "State" shows the badge "Up" with `text-bg-success`, or "Down" with `text-bg-danger`. A down channel also shows its reason: the error name for `Errors`, or "Paused" and the note for `Operator`.
   - The column "Since" shows the start of the current state in UTC and the time since then, for example "2026-11-18 13:32 UTC (3 d 4 h)", and the previous state with its duration, for example "after Down for 4 min". A channel with no row shows "No down state recorded".
   - The page reads the current time from `TimeProvider`. `AddMsgHatchAdminUI` adds `TimeProvider.System` when the host has none.
   - The button "History" opens `AdminDialog`. The dialog shows a timeline bar of the last 30 days with `progress-stacked`: red with `bg-danger` where the channel was down, and green with `bg-success` where it was up. Under the bar, it lists the rows, newest first: the time, "Down" or "Up", the reason, the error name, the note, and the duration of the previous state.
   - The row that was current at the start of the window gives the state at the start of the bar. With no such row, the channel was up. An error shows in the dialog, and the dialog has Close on the left (D261).
   - `ChannelViewPage` shows the same "State" and "Since" fields. "History" is the main action on the right of `AdminActions`, and opens the same dialog. The list and the view page use the same parts for the state and the dialog.
   - `AddMsgHatchAdminUI` keeps the pages with `DynamicDependency` (D250).
10. Add message type columns and the subscription counts of each level (D275, D276):
    - `MsgHatch.AdminUI` gets `IMessageTypeColumn` with `Header` and `GetCellsAsync(CancellationToken ct)`, which gives `ErrorOr<IReadOnlyDictionary<MessageType, IReadOnlyList<MessageTypeCellItem>>>`. `MessageTypeCellItem(string Text, string? Href)` is one badge of a cell. `AddMessageTypeColumn<TColumn>()` registers a column, with `DynamicallyAccessedMembers(PublicConstructors)` on `TColumn`.
    - `MessageTypeListPage` shows each registered column after its own columns, in the order of registration, and each item of a cell as a badge. An item with an address is a link. An error of a column shows above the list with its header. The cells of that column stay empty, and the other columns still show.
    - `MsgHatch.Subscriptions.AdminUI` gets the internal column "Subscriptions". It counts the stored subscriptions of each message type for each `SubscriptionLevel`, with one call `GetSubscriptionsAsync(new SubscriptionQuery())` of `TenantKey.None`. Each cell has "Default N", "Company N", and "User N", also for 0. `AddMsgHatchSubscriptionsAdminUI` registers it.
    - Each count links to the subscription list with the filters of its message type and its level. `SubscriptionAdminRoutes.ListOf(MessageType type, SubscriptionLevel level)` gives the address, for example `/msghatch/subscriptions?type=OrderShipped&level=Company`, with each value escaped.
    - `SubscriptionListPage` reads the query parameters `type` and `level`, and starts with these filters. A value that is not valid gives no filter for that value. The query parameter `new` of D260 still opens the dialog.
11. Remove the page "Applied subscriptions" (D274): `MessageTypesPage`, `SubscriptionAdminRoutes.MessageTypes`, its `DynamicDependency`, and its link on the subscription list. `ISubscriptionQueries.GetMessageTypesAsync`, its HTTP route, and its client stay.
12. Put the admin pages of the example host in the new order (D274):
    - The menu and the home page have the order Channels, Message types, Subscriptions, Templates, and Verification. "Subscriptions" keeps its mark on the subscription pages, and the menu has no "Applied subscriptions".
    - The README of the example app names the channel state, the history, and the subscription counts, and no longer names the page "Applied subscriptions".
13. Describe the phase in the design and the store guide:
    - The design: the sections 4.19, 4.20, 4.23, and 4.24.
    - `docs/stores.md`: the format of the state rows in each store, the unique key on the channel and the version, the new parameter of `SetChannelUpAsync`, and what a store for another storage must do.
    - AGENTS.md: the exception of D275 to the rule of the admin pages.

Tests:
- `OutboxStoreTestsBase<TStore>`:
  - A written down state adds a `Down` row with the version 1, and no `PreviousKind` and `PreviousDuration`. A down state with the reason `Errors` on a down channel, and a pause on a paused channel, add no row.
  - An up change of a down channel adds an `Up` row with the next version, the `PreviousKind` `Down`, and the `PreviousDuration` of the down state. A call on an up channel, or with another reason, adds no row.
  - `GetChannelStatesAsync` gives the rows of each channel at or after `fromUtc` and the row that was current at `fromUtc`, newest first. Rows older than 30 days stay.
  - Two parallel changes of one channel add one row. `TryStartChannelCheckAsync` keeps the version of the latest row.
  - The in-memory, file, and SQLite stores get these tests in the unit tests, and the EF Core and MongoDB stores in their integration tests. The MongoDB tests pass on a replica set, for example dev-mongo, and on a standalone server, for example the container of Testcontainers.
- `ChannelState.Change` and `ChangeAsync`: a lost insert reads the latest row again, and the third lost insert gives `Errors.Conflict`.
- A golden-file sample of the row format of the file store (section 1.8 of `docs/stores.md`).
- `OutboxChannelWork` and `DeliveryManager` give the time of `TimeProvider` to `SetChannelUpAsync`, and the logging wrapper logs the new store call.
- `RegistryQueriesTestsBase<T>`:
  - A channel with no row, and a down channel with each reason.
  - `Current` is the latest row of each channel, also before the window, with the kind and the duration of the previous state.
  - The history of one channel with the row before the window, newest first, `NotFound` for a channel that is not registered, and the error of the store.
  - `RegistryManager` and the HTTP client get the new rows.
- The logging wrapper of the registry queries: one event for each history call, with the count or the error type.
- The contracts: `ChannelDownResponse` and `ChannelStateResponse` map to their domain types and back, and a name that is not valid gives `SerializationFailed`.
- The pages, with bUnit and `FakeTimeProvider`:
  - The cells "State" and "Since" of an up channel, of a down channel with each reason, of a row with a previous state, and of a channel with no row.
  - The dialog "History": the widths and the colors of the segments of a known list of rows, the start of the bar from the row before the window, the rows of the list, and the text of an error.
  - The view page: the fields "State" and "Since", and "History" opens the dialog.
  - The message types page: the items of a registered column as badges, an item with an address as a link, and the error of a column above the list.
  - The column "Subscriptions": the counts of each level, also 0, and the address of each count.
  - `SubscriptionAdminRoutes.ListOf` escapes its values. The subscription list starts with the filters of `type` and `level`, and a value that is not valid gives no filter.
  - The subscription list has no link to "Applied subscriptions", and the test of the `DynamicDependency` attributes still names each routed page.
- The example host: the menu order, and the mark of each menu link on its page.
- Each new test fails first, or fails with a break of the code that it covers.

Checked facts (2026-11-18), on Windows 11 build 26300 with the .NET SDK 10.0.404:

| Fact | Source |
|---|---|
| The check of item 1 found that the MongoDB stores use no transaction and no session (D186), and that MongoDB has no transaction on a standalone server ("MongoDB standalone deployments do not support transactions", the MongoDB manual). The Testcontainers fallback of the MongoDB tests starts a standalone server, and only the local dev-mongo is a replica set (`rs0`). A file system cannot change two files in one atomic step. The in-memory, SQLite, and EF Core stores can change two rows in one lock or transaction. So the owner changed the design to the insert-only rows, which need no transaction. | A search for `StartSession`, `WithTransaction`, and `IClientSession` in the MongoDB stores, the MongoDB manual, and `MongoDbFixture.cs` |
| `SqliteSchema.Core` runs `CREATE TABLE IF NOT EXISTS` at the first connection of each connection string. So a database file of phase 31 gets the table `MsgHatchChannelState`, keeps its data, and keeps the schema version 1, as the registry tables of phase 16 did. The old table `MsgHatchChannelDown` stays, and the store does not read it. The EF Core package creates no schema, so a host adds a migration of `MsgHatchDbContext` for the new table. | `SetChannelDownAsync_DatabaseWithADownRowOfPhase31_AddsTheStateTableAndReadsNoDownState`, and section 4.1 of `docs/stores.md` |
| Bootstrap 5.3.3 has the classes `progress-stacked` and `progress-stacked>.progress>.progress-bar`. | The file `bootstrap.min.css` of Bootstrap 5.3.3 on jsDelivr |
| A SQLite store serializes its writes, so two parallel changes of one channel in one process never tried to add the same version. A break of `INSERT OR IGNORE` to an insert that replaces a row passed the parallel rows of the contract. A second connection that adds the version after the read of the store and before its insert finds the break. | `SetChannelDownAsync_VersionThatAnotherConnectionInsertsAfterTheRead_KeepsThatRowAndAddsNoRow` |
| On SQL Server, the insert of a state row has the condition `WHERE NOT EXISTS` with `UPDLOCK, HOLDLOCK`. With the condition removed, the second of two parallel inserts gave a key error, and a parallel row failed in each of three runs. So the parallel rows of the contract also check that no change gives an error. | The parallel rows of `OutboxStoreTestsBase` on dev-mssql |
| The MongoDB store passes 356 tests on dev-mongo, a replica set, and 356 tests on the standalone server `mongo:8.3` of Testcontainers. The standalone run uses a copy of the test project with no `appsettings.Local.json`. With the unique index `UX_MsgHatchChannelState_Version` removed, the parallel rows of the contract fail. | `dotnet test` of the test project and of its copy in `artifacts/container-tests` |
| The EF Core integration tests pass 338 tests of the stores and 48 tests of the subscription store on dev-mssql. The MongoDB integration tests pass 50 tests of the subscription store. The solution build with an artifacts path in a scratch folder has no errors and no warnings at commit 15. | `dotnet test` of each integration project, and `dotnet build MsgHatch.slnx --artifacts-path` |
| The 35 unit test projects pass 4,940 tests and skip 1 test: the path limit test of the named pipe signal on Windows. The phase adds 161 tests and test rows. `MsgHatch.Domain.Tests.Unit` passes 1,527 tests, `MsgHatch.Data.FileSystem.Tests.Unit` 340, `MsgHatch.Data.Sqlite.Tests.Unit` 355, `MsgHatch.AspNetCore.Tests.Unit` 125, `MsgHatch.Api.Contracts.Tests.Unit` 85, `MsgHatch.AdminUI.Tests.Unit` 79, `MsgHatch.Subscriptions.AdminUI.Tests.Unit` 58, `MsgHatch.Api.Client.Tests.Unit` 35, and `AdminUiExample.Tests.Unit` 13. The line coverage of the production code is 98.7 %: `MsgHatch.AdminUI` 100 %, `MsgHatch.Api.Contracts` 100 %, `MsgHatch.Data.FileSystem` 99.8 %, `MsgHatch.Data.Sqlite` 99.8 %, `MsgHatch.Domain` 99.6 %, `MsgHatch.AspNetCore` 97.9 %, `MsgHatch.Subscriptions.AdminUI` 96.4 %, and `MsgHatch.Api.Client` 80.8 %, with the same shared sources with no caller as in phase 31. The first report showed lines with no test in `ChannelTimeline` (81.4 %) and in `ChannelState.ChangeAsync`, so commit 16 adds two tests. After commit 16, each new class has 100 %. The example apps of the owner ran from `examples/*/bin/Debug`, so the run used a copy of `coverage.ps1` that builds with `--artifacts-path` in a scratch folder, with no other change. | The copy of `pwsh ./coverage.ps1` on the content of commit 16 |
| The new tests failed with a break of the code that they cover, or before the code of their commit existed. The breaks failed: the domain rows of the versions, the previous state, the window, and the tries, the rows of the file, SQLite, EF Core, and MongoDB stores, the write of `PreviousKind` in each store, the registry queries with the window and the store read, the mappings of `Current` and `previousKind` in the contracts, the history route and client, the state and the dialog of the channel pages, the columns of the message types page, and the subscription counts. In commit 16, the breaks of the success path of `ChangeAsync`, of the merge of two neighbor parts of the timeline, and of the skip of a part with no duration each failed one new test. The test of the menu order failed with the files before commit 14. | The breaks, each one reverted after its run |

Checks that the phase did not do:
- A browser check of the channel pages and the dialog. The bUnit tests check the cells, the segments of the bar, and the rows of the dialog with `FakeTimeProvider`.
- The pages in the dark mode in a browser. They use only the color variables of Bootstrap.
- A database of phase 31 on SQL Server or MongoDB. The EF Core host adds the table with its own migration, and the MongoDB store makes the new collection and its unique index at their first use.

Commits:
1. "Plan phase 32: the channel state and the order of the admin pages"
2. "Record the transaction check of MongoDB in the plan of phase 32"
3. "Plan the insert-only channel states of phase 32"
4. "Keep the channel states as insert-only rows in the outbox dispatch store"
5. "Keep the channel states in the file store"
6. "Keep the channel states in the SQLite store"
7. "Keep the channel states in the EF Core store"
8. "Keep the channel states in the MongoDB store"
9. "Add the channel state and history to the registry queries"
10. "Add the channel history to the registry contracts, route, and client"
11. "Show the channel state and history on the channel pages"
12. "Add message type columns and the subscription counts of each level"
13. "Remove the Applied subscriptions page"
14. "Put the admin pages of the example host in the new order"
15. "Describe the channel state and the new page order in the design"
16. "Test the lines that the coverage report of phase 32 showed with no test"
17. "Record the commits and the checked facts of phase 32 in the plan"
18. "Record phase 32 as done in README and AGENTS.md"

Commit 1 holds the plan and the decisions (`0a0b933` and `fd1d082`), commit 2 the result of the check of item 1 (`aeb6c50`), commit 3 the plan of the insert-only rows (`2b4f71b`), commit 4 item 2 (`1a891b5`), commit 5 item 3 (`7f35192`), commit 6 item 4 (`f821227`), commit 7 item 5 (`d09f9db`), commit 8 item 6 (`b481e01`), commit 9 item 7 (`531b694`), commit 10 item 8 (`12d8616`), commit 11 item 9 (`5d7ac89`), commit 12 item 10 (`d8da986`), commit 13 item 11 (`726eb9d`), commit 14 item 12 (`33859db`), commit 15 item 13 (`90c9038`), and commit 16 the tests of the lines that the first coverage report showed with no test (`42c375d`). Item 1 is a check, and its results are in the checked facts. The unit test projects of the changed code passed at each commit before the rebuild of the history. After the rebuild, the tree of commit 15 was the same as before it, and the commits 4 and 9 built again. The phase adds no project and no package. Commit 5 adds the golden-file sample `ChannelState.v1.json` as an embedded resource of `MsgHatch.Data.FileSystem.Tests.Unit`.

The first commits of items 2 to 6 wrote a down state row and a second table of events in one transaction, and a transaction on MongoDB only when the server is a replica set. The owner replaced this design with the insert-only rows, and the history of the phase was built again from `aeb6c50`. The branch `backup/phase-32-event-table` kept the old commits until the owner deleted it after the phase.

The implementation changed the plan in these points:
- The file store writes a state row to a temporary file, and moves it to the path of its version with no overwrite (`MessageFiles.TryCreate`), not with `FileMode.CreateNew`. So a reader never sees half a file.
- The SQLite tests add a test with a second connection, because the parallel rows of the contract do not find a lost insert in one process (checked facts).
- The badge of the state is `ChannelStateBadge`, because a component with the name `ChannelState` hides the domain type `ChannelState` in the pages.
- The column "Subscriptions" also reads the message types of `IRegistryQueries`, so a message type with no subscription gets the counts 0.
- The README of the example app names the pause route `POST /api/delivery/channels/{channel}/pause` with the role `DeliveryAdmin`.
- Commit 15 also changes the sections 4.5, 4.6, and 5.5 of the design, because they describe the store contract, the store tables, and the contract tests.
- Commit 16 is new. The first coverage report showed two behaviors with no unit test, so the phase has 18 commits.

Done when: the build has no errors and no warnings, all unit tests pass, the integration tests of the EF Core and MongoDB outbox stores pass, also the MongoDB tests on a standalone server, and `pwsh ./coverage.ps1` passes.

### Phase 33: channel check

Goal: an operator checks the health of one channel from the channel list. A check that finds a new state adds a state row, and a check that finds the same state adds no row (D283).

Starting point: the commits of phase 32, `0a0b933` to `136cad8`. The owner asked: "Add a new "Check" or "Test" action to the Channels list rows for checking a channel for up/down and register a state change if the channel state changed. It will be good for testing the UI. The test should not save a new ChannelState record if the state did not change." The owner chose "Registry command" for the API, "Mark it down" for a failed check of an up channel, and "New phase 33". The owner had no preference for a switch that makes a transport of `ExampleApp` unhealthy, so the phase adds none.

1. Check first, before the code:
   - `ITransport.CheckHealthAsync` of each transport of the repository returns an `Error` and does not throw, and `TransportLoggingWrapper` logs each check.
   - The transports of `ExampleApp` return no error from their check. So in the example host, a check only brings a channel up, and a pause gives a down state.
   - The registry group of `ExampleApp` has the policy `RegistryAdmin` for each route, so a POST route in the group needs the same token as the registry pages.
2. Add the channel check command to the registry (D283):
   - `IRegistryCommands` in `MsgHatch.Domain.Management` with `ValueTask<ErrorOr<ChannelCheck>> CheckChannelAsync(ChannelId channel, CancellationToken ct)`.
   - `ChannelCheck(ChannelId Channel, int? ErrorCode, string? ErrorName, bool Changed)`. `Healthy` is true when the check gave no error.
   - `RegistryManager` implements it. It finds the transport of the channel in the `ITransport` services of the host, and calls `CheckHealthAsync`. With no error, it calls `SetChannelUpAsync` with the reason `Errors` and the time of `TimeProvider`, and a new `Up` row notifies the dispatch signal of the channel. With an error, it calls `SetChannelDownAsync` with the reason `Errors`, the code and the name of the error, and `NextCheckAtUtc` after `CheckInterval` of the `ChannelDownOptions` of the channel.
   - The store adds no row when the state does not change (D277): a check with no error of an up channel, a failed check of a channel that is down for errors, and each check of a paused channel. So a check never ends a pause (D106).
   - A channel that is not registered, or that has no transport in the host, gives `Errors.NotFound`. An error of the store is the error of the command.
   - `AddMsgHatch` adds the in-process command as the default scoped tenant service, and `MsgHatchBuilder.UseRegistryCommands` selects another implementation. The logging wrapper logs each check with the events 2915 and 2916: the channel, the error name of the check, and the change, or the error type of the command.
3. Add the channel check to the registry contracts, route, and client (D283):
   - `RegistryApiRoutes.ChannelCheck` holds `/channels/{channel}/check`. The POST route calls `IRegistryCommands` of `TenantKey.None` in the registry group, so the policy of the group applies. It gives 200 with `ChannelCheckResponse`, 400 for a name that is not valid, and the status of `ErrorProblems` for an error of the command.
   - `ChannelCheckResponse(string Channel, int? ErrorCode, string? ErrorName, bool Changed)` with `From` and the mapping back goes into `RegistryEndpointJson`. A name that is not valid gives `Errors.SerializationFailed`.
   - `IRegistryApi` gets the call, and `HttpRegistryCommands` implements `IRegistryCommands`. `UseRegistryApiClient` selects the HTTP client for the queries and for the commands.
4. Add the Check button to the channel list (D283):
   - Each row of `ChannelListPage` gets the button Check, between History and View. A click calls `CheckChannelAsync` of `TenantKey.None`, disables the button until the result, and then reads the channels again.
   - An alert above the list shows the result. A check with no error shows "{channel}: healthy. The channel is up again." or "{channel}: healthy. The state did not change." with `alert-success`. A failed check shows "{channel}: {error name}. The channel is down now." or "{channel}: {error name}. The state did not change." with `alert-warning`. An error of the command shows the text of `AdminErrors` with `alert-danger`.
5. Describe the phase in the design: the sections 4.20, 4.23, and 4.24.

Tests:
- `RegistryCommandsTestsBase<T>`, with the in-process host and the HTTP client:
  - A check with no error of an up channel adds no row, and gives `Changed` false.
  - A check with no error of a channel that is down for errors adds an `Up` row, and gives `Changed` true.
  - A failed check of an up channel adds a `Down` row with the reason `Errors`, the error of the check, and the next check time, and gives `Changed` true.
  - A failed check of a channel that is down for errors, and each check of a paused channel, add no row.
  - A channel that is not registered, and a channel with no transport in the host, give `NotFound`. An error of the store is the error of the command.
- `RegistryManager`: a check that adds an `Up` row notifies the dispatch signal of the channel, and a check with no change does not.
- The logging wrapper of the registry commands: one event for each check, with the error type for an error.
- The route: 200 with the body, 400 for a name that is not valid, and 404 for a channel that is not registered.
- The contracts: `ChannelCheckResponse` maps to `ChannelCheck` and back, and a name that is not valid gives `SerializationFailed`.
- The page, with bUnit: the button of each row, the four texts of the alert with their classes, the text of an error of the command, the second read of the channels, and the disabled button during a check.
- Each new test fails first, or fails with a break of the code that it covers.

Checked facts (2026-11-18), on Windows 11 build 26300 with the .NET SDK 10.0.404:

| Fact | Source |
|---|---|
| The health checks of the SES and Resend clients catch each exception and return an `Error`, and a cancelled token gives `Errors.OperationCancelled`. `TransportLoggingWrapper` logs each check with its error. The transports of `ExampleApp`, `LogEmailClient`, SignalR, and Wolverine, return no error from their check. The registry group of `ExampleApp` has `RequireAuthorization(RegistryAdmin)`, so the POST route needs the token of the registry pages. | `AmazonSimpleEmailServiceClient.cs`, `ResendEmailClient.cs`, `TransportLoggingWrapper.cs`, `LogEmailClient.cs`, and `ExampleApplication.cs` |
| The web server of the HTTP client tests uses the system clock, so `RegistryCommandsTestsBase` adds the fake clock to the services of the server, as the query tests do. With no fake clock, two use cases failed on the HTTP client: the rows had the order of the real time, and the down row had the real time. | `HttpRegistryCommandsUnitTests` before and after the change |
| A path with no route also gives 404, so the route row "Unknown gives 404" passed before the route existed. The use case `CheckChannelAsync_ChannelThatIsNotRegistered_GivesNotFound` of the HTTP client goes through the route, and failed with no route. | `RegistryEndpointsUnitTests.Commands.cs` and the run of the client tests with the selection of the commands removed |
| The 35 unit test projects pass 4,987 tests and skip 1 test: the path limit test of the named pipe signal on Windows. The phase adds 47 tests and test rows. `MsgHatch.Domain.Tests.Unit` passes 1,542 tests, `MsgHatch.AspNetCore.Tests.Unit` 128, `MsgHatch.Api.Contracts.Tests.Unit` 91, `MsgHatch.AdminUI.Tests.Unit` 88, `MsgHatch.Api.Client.Tests.Unit` 49, and `AdminUiExample.Tests.Unit` 13. The line coverage of the production code is 98.7 %: `MsgHatch.AdminUI` 100 %, `MsgHatch.Api.Contracts` 100 %, `MsgHatch.Domain` 99.6 %, `MsgHatch.AspNetCore` 97.9 %, and `MsgHatch.Api.Client` 83.3 %, with the same shared sources with no caller as in phase 31. Each new class has 100 %. The run used the copy of `coverage.ps1` that builds with `--artifacts-path` in a scratch folder, because the example apps of the owner lock `examples/*/bin/Debug`. The solution build with `--no-incremental` had two warnings at commit 5, and has no errors and no warnings at commit 6. | The copy of `pwsh ./coverage.ps1` on the content of commit 5, and `dotnet build MsgHatch.slnx --no-incremental --artifacts-path` |
| The new tests failed before the code of their commit existed, or with a break of the code that they cover. The breaks failed: the notify of the signal on each check, the guard of a cancelled check, the match of the transport, the reason of the up change, the check interval, and the change flag of the result and of the log record (commit 2), the route path, the change flag of the body, the check of the error pair, the error code of the mapping, the selection of the commands, and the check of the body in the client (commit 3), and the disabled buttons, their end, the second read, the class of an error, the texts of the new state, and the class of a failed check (commit 4). Two breaks did not build at first, so they ran again with a break that builds. | The breaks, each one reverted after its run |

Checks that the phase did not do:
- A browser check of the button Check on copies of the example apps. The bUnit tests check the button, the alert, and the second read.
- A failed check in a live host. The transports of the example give no error, and the owner had no preference for a switch that makes one unhealthy.

Commits:
1. "Plan phase 33: the channel check"
2. "Add the channel check command to the registry"
3. "Add the channel check to the registry contracts, route, and client"
4. "Add the Check button to the channel list"
5. "Describe the channel check in the design"
6. "Fix the warnings of the new channel check tests"
7. "Record the commits and the checked facts of phase 33 in the plan"
8. "Record phase 33 as done in README and AGENTS.md"

Commit 1 holds the plan and the decision D283 (`27b35ee`), commit 2 item 2 (`8354fe5`), commit 3 item 3 (`85b3835`), commit 4 item 4 (`9a8a070`), commit 5 item 5 (`21ccf82`), and commit 6 the fix of two warnings of the new tests (`637a5f8`). Item 1 is a check, and its results are in the checked facts. The unit test projects of the changed code passed at each commit. The phase adds no project and no package.

The implementation changed the plan in these points:
- The session of the solution design had an uncommitted plan of the open allocations as phase 33 in the same files. The owner chose the channel check as phase 33, so commit 1 holds only the lines of this plan, and the other plan became phase 34 (`7351cdc`).
- A cancelled check gives `Errors.OperationCancelled` and changes nothing, because a cancel is not an outage. The plan did not name this case.
- `HttpRegistryCommandsUnitTests` runs the use cases on the client host of `HttpRegistryHost`, through a small host of the commands. `IRegistryQueriesTestHost<T>` is generic, so the commands use it too.
- During a check, the Check buttons of all rows are disabled, so one page never runs two checks at the same time.
- Commit 5 also changes the sections 4.2 and 4.7 of the design, which name the setup methods, and the README of the example app, which tells that a check there only ends a down state for errors.
- Commit 6 is new. The solution build at commit 5 had two warnings `xUnit1051`: two checks of no call passed the default cancellation token. So the phase has 8 commits.

Done when: the build has no errors and no warnings, all unit tests pass, and `pwsh ./coverage.ps1` passes.

### Phase 34: open allocations

Goal: the hot path allocates only what section 4.11 permits. The phase removes the avoidable allocations of the event path, of a send, of a subscription cache hit, and of the subscription resolution. Then it measures the three benchmark projects again and records the new budget (D159, D280 to D282).

Starting point: the commits of phase 33. The probe of the plan ran on the production code of phase 32, so item 1 measures again after phase 33. The owner asked to "do the investigation and plan the changes for the open allocations improvements". The owner also said that a template store in a database is not needed yet, because the file template store is enough for now.

The investigation of the plan used a probe outside the repository. The probe ran the benchmark classes of the three performance projects in Release on the production code of phase 32, with an `EventListener` in the process on the event `AllocationSampled` of the .NET 10 runtime (keyword `0x80000000000`). The event samples about one object in each 100 KB at random, so the share of the samples of a type is its share of the bytes. Each scenario had 2,600 to 4,600 samples. The probe found these bytes:

| Scenario | Bytes | Avoidable part |
|---|---|---|
| `ProcessAsync`, 1 route | 3,472 B | About 2.5 KB: the closures of the boundary, the measurement, the route selection, and the registry lookup, the LINQ iterators of the store step, the dictionary and the lists of `RouteGrouping`, the copies of the routes, the set of the topics, and 3 boxed `TenantKey` values |
| `ProcessAsync`, each further route, from 1 to 100 routes | 776 B | The closure of `FindChannel` 94 B, the topic string 46 B, two copies of the routes and the list of the checked routes 212 B, the growth of the set of the targets about 80 B, and the second result array 24 B. The outgoing message, 231 B, stays. |
| `DispatchPendingAsync`, each message of a batch of 100 | 254 B | The `SemaphoreSlim` waiter of `Parallel.ForEachAsync` 88 B, and two boxed `PublishOutcome` values 45 B. The publish try, 91 B, stays. |
| `RunAsync`, each message of a batch of 100 | 237 B | Two boxed `PublishOutcome` values 46 B. The read operation of the work queue, 82 B, stays (D281), and the publish try, 91 B, stays. |
| Subscription cache hit for the defaults, one company, and one user | 448 B | Two owner part strings, about 55 B, two boxed enumerators 60 B, and the empty list of the missing owners 30 B |
| `SubscriptionResolver.Resolve`, each further user of an opt-out, from 1 to 100 users | 1,739 B | The skipped subscriptions in a list, a second list, and an array for each user 728 B, the growth of the opt-out list 218 B, the growth of the pair index 144 B, the candidate list of each pair 153 B, the list of the matching subscriptions 89 B, and a boxed enumerator of the channels 42 B. The result is about 180 B. |

So the 150 B of each route that phase 13 did not attribute are the closure of `FindChannel` and the topic string. The 150 B of each send are the waiter, the two boxes, and the state machines of each batch.

1. Measure the start: run the three benchmark projects at the last commit of phase 33, one at a time, and keep the results for item 10. The probe numbers above give the attribution, and the benchmark numbers give the budget.
2. Remove the closures from the event path (D280):
   - `MessageRegistration.FindChannel` is a loop with no closure.
   - `Boundary` gets the overload `RunAsync<T, TState>(Func<TState, ValueTask<ErrorOr<T>>> run, TState state, CancellationToken ct)`, and `NotificationService` calls it with a static lambda.
   - `NotificationPipeline.MeasureAsync` and the route selection of `ProcessInSpanAsync` get their values as arguments, with no closure.
   - `RouteGrouping` builds its groups with loops, with no closure and no LINQ. `ByChannel` and `ByLanguageAndScope` keep their results.
3. Notify each channel of an event one time (D280):
   - `IChannelRegistration` gets `Topic`, which the registration makes one time with `DispatchTopic.ForChannel`.
   - `StoreAsync` makes the message array and the error array in one loop, each with its exact size. It notifies the topic of each channel with a stored message one time, in the order of the first stored message of the channel, with no `Distinct`.
4. Compose the routes of each channel with no copies of the route list (D280):
   - A first pass checks each route, writes the error of a failed route into the result array, and counts the routes of each channel in a small list of the channels of the event.
   - When all routes are on one channel, no route failed, and the router gave a `MessageRoute[]`, the composer gets that array. Else one new array holds the routes of all channels, in groups by channel and in the order of the router, and one `int[]` holds the position of each route.
   - `IChannelRegistration.ComposeAsync` gets the result array and the positions of its group, writes each outgoing message at the position of its route, and gives `Error?`.
   - The set of the targets gets the capacity of the route count, and one route needs no set.
5. Resolve the tenant services with boxed keys made one time (D280):
   - The internal `TenantKeyBoxes.Of(TenantKey key)` gives one box for `TenantKey.None`, and one box for each tenant, for at most 1,024 tenants. After the limit, each call makes a new box. A `ponytail:` comment names the limit.
   - The keyed lookups of `NotificationPipeline` and `ChannelRegistration` use it, because they run for each event or for each channel of an event.
6. Record the outcome tags of a send with no boxing (D281): a switch gives the literal name of each `PublishOutcome` to `RecordTry` and `RecordSendDuration`.
7. Run the batch of `DispatchPendingAsync` with `Parallel.ForAsync(0, count, ...)` in place of `Parallel.ForEachAsync` over `Enumerable.Range` (D281).
8. Build the subscription cache keys with no owner part strings (D282):
   - `SubscriptionCacheKeys.Key` and `OwnerTag` write the owner part into the key in one interpolation. The text of each key and tag stays the same.
   - `FindForDeliveryAsync` makes the list of the missing owners at the first miss, and reads the companies and the users by index.
9. Reduce the scratch allocations of the subscription resolution (D282):
   - `ResolutionRun` reads its lists by index.
   - `ResolveOverridable` makes no list of the matching subscriptions. A second pass over the subscriptions writes the skipped subscriptions into an array of their exact size.
   - A pair keeps its first candidate in a field, and makes a list only for a second candidate. A pair with one candidate keeps the skipped array of that candidate. A pair with more candidates makes one array of the exact size, in level order.
   - `Merge` counts the deliveries and the opt-outs first, and makes two arrays of their exact sizes.
   - The pair index, the pair list, the set of the seen users, and the list of the scope users get the capacity of the scope.
10. Record the measured budget in the design (D159):
    - Run the three benchmark projects after item 9, one at a time.
    - Section 4.11: the measured table with the numbers of item 1 and of this run, the new budget lines, the line of the wait of a worker (D281), and the line of the resolution (D282). The numbers of the placeholder engine and of the composer replace those of phase 13, which are from before D214.
    - Each open item of section 4.11 goes away, or stays with its measured cause.

Targets, for the benchmark runs of item 10. Each target holds after item 9, or the phase records the measured value and its cause (D159):

| Benchmark | Probe of the plan | Target |
|---|---|---|
| `ProcessAsync`, 1 route | 3,472 B | At most 1,000 B |
| `ProcessAsync`, each further route | 776 B | At most 330 B |
| `DispatchPendingAsync`, each message | 254 B | At most 110 B |
| `RunAsync`, each message | 237 B | At most 190 B |
| Subscription cache hit | 448 B | At most 300 B |
| `SubscriptionResolver.Resolve`, each further user | 1,739 B | At most 500 B |

Tests:
- The use-case tests of the pipeline, the dispatcher, the caching wrapper, and the resolver stay as they are, and pass at each commit, because the behavior does not change.
- The pipeline, with new rows where a new path starts:
  - Routes on one channel as an array and as a `List<MessageRoute>`, routes on two channels in turns, and a failed route in the middle: each outcome keeps the position of its route, and each composer gets the routes of its channel in the order of the router.
  - A second route for the same target on one channel, among three routes, gives `Errors.Conflict` at its position. One route gives no conflict.
  - Routes on two channels notify the topic of each channel one time, in the order of the first stored message. A channel whose routes all fail gets no notify.
- The tenant services: the services of two tenants and of no tenant resolve with the same results at the first call and at later calls.
- The send: a theory over each `PublishOutcome` value. The tag `msghatch.outcome` of `msghatch.publish.tries` and of `msghatch.send.duration` is the name of the value.
- `DispatchPendingAsync` with `MaxDegreeOfParallelism` 4 and 20 messages sends each message one time, with at most 4 sends at the same time.
- The caching wrapper: the key and the tags of a default, of a company with `:` in its id, and of a user keep their text. A hit for one company and one user reads no store.
- The resolver: a unit test calls `Resolve` for 100 users of an opt-out after one call to warm it up. It allows at most the target of each user with `GC.GetAllocatedBytesForCurrentThread()`. The rows of the domain examples check the order of the skipped subscriptions of a pair with one candidate and of a merged pair.
- Each new test fails first, or fails with a break of the code that it covers.

Checked facts of the plan (2026-11-18), on Windows 11 build 26300 with the .NET SDK 10.0.404 and .NET 10:

| Fact | Source |
|---|---|
| The bytes and the types of the investigation table. The probe measured with `GC.GetAllocatedBytesForCurrentThread()` on the thread of the call, and with `GC.GetTotalAllocatedBytes(true)` for the dispatch loops, after 3 seconds of calls to warm up. | The probe `allocprobe` in a scratch folder, with project references to the three performance projects |
| `Parallel.ForEachAsync` over `Enumerable.Range(0, 100)` with 8 workers allocated 99.9 B for each item with a body that completes at once, and 207.7 B with `Task.Yield`. `Parallel.ForAsync(0, 100)` allocated 4.1 B and 118.3 B. | A second probe in a scratch folder, 20,000 batches after 20,000 batches to warm up |
| The waiter of `Parallel.ForEachAsync` is `TaskNode` of `SemaphoreSlim`. The read operation of the work queue is `WaitingReadAsyncOperation` of `System.Threading.Channels`. | The type names of the samples |
| `TenantKey` is a `readonly record struct`, and a keyed service lookup takes `object? serviceKey`, so each lookup boxes the key. | `TenantKey.cs`, and the samples of the type `TenantKey` |
| `PublishOutcome.ToString()` boxes the value in `RecordTry` and `RecordSendDuration`. | The samples of the type `PublishOutcome`, and `MsgHatchMetrics.cs` |
| `MessageRegistration.FindChannel` calls `List<T>.Find` with a lambda that captures the channel, so each route allocates a closure and a delegate. `DispatchTopic.ForChannel` makes a new string at each call, and `StoreAsync` calls it for each stored message. | `MessageRegistration.cs`, `DispatchTopic.cs`, `NotificationPipeline.cs`, and the samples of `Predicate<T>` and `String` |

Checks that the phase does not do:
- The integration tests of the stores, because no store changes.
- An allocation test of an async path in the unit tests, because the Debug build makes each async state machine a class (section 4.11). The benchmarks measure these paths.
- A check of each commit in its own worktree. The unit test projects of the changed code passed at each commit in the working tree, and each commit holds only its own files.
- A probe of the 28 B for each message that the benchmark of `RunAsync` counts more than the probe in the process. Section 4.11 says that its cause is not confirmed.

Checked facts of the build (2026-11-19), on Windows 11 build 26300 with the .NET SDK 10.0.404 and .NET 10.0.15:

| Fact | Source |
|---|---|
| Item 1 ran the three benchmark projects at `adf32d1`, the last commit of phase 33, in a detached worktree of that commit, so the edits of the phase did not change the measured code. Each log has build paths only in that worktree. The first try started in a folder with no `global.json`, so `dotnet` took the SDK 11.0.100, and the build of the generated project failed with `MSB4216`. The second try started in the root of the worktree. The computer slept during the Domain runs of item 1 and of item 10. The deviations of the times are small, and the bytes do not depend on the time. | The logs of the runs, and `git worktree add --detach` |
| The bytes of each benchmark come from the line `// GC:` of its log: the total bytes divided by the operations. They match the column `Allocated`, which BenchmarkDotNet rounds to 0.01 KB above 1 KB. Item 1 matched the probe of the plan: 3,472 B for one route, 775 B for each further route, 254 B for each message of `DispatchPendingAsync`, 1,739 B for each further user, and 448 B for a cache hit. `RunAsync` gave 261 B, and the probe of the plan 237 B. | The logs of item 1 and of item 10 |
| Each target holds except `RunAsync`: one route 688 B (at most 1,000 B), each further route about 304 B (at most 330 B), each message of `DispatchPendingAsync` 108 B (at most 110 B), a cache hit 288 B (at most 300 B), and each further user about 401 B (at most 500 B). `RunAsync` gave 212 B (at most 190 B). A probe in the process with the event `AllocationSampled`, 30,000 batches after 20,000 batches to warm up, gave 184 B for each message: `PublishTry` 89 B, `WaitingReadAsyncOperation` 81 B, and about 15 B of state machines and waits of each batch. | The runs of item 10, and the probe `probe` in a scratch folder with a project reference to `MsgHatch.Domain.Tests.Performance` |
| `default(ErrorOr<T>)` has `IsError` true, and its `Error` gives a new `Uninitialized` error at each read. So the grouping of item 4 cannot find the routes that passed the check from the result array. A checked route holds one shared `Uninitialized` error until its composer writes its outcome. | `ErrorOr.cs` |
| The 35 unit test projects pass 5,007 tests and skip 1 test: the path limit test of the named pipe signal on Windows. The phase adds 20 tests and test rows. `MsgHatch.Domain.Tests.Unit` passes 1,559 tests, and `MsgHatch.Subscriptions.Tests.Unit` 421. The line coverage of the production code is 98.7 %: `MsgHatch.Domain` 99.6 %, `MsgHatch.Subscriptions` 100 %, and `MsgHatch.Email` 100 %. Three lines of the changed classes have no test: the read of a box that another thread added first in `TenantKeyBoxes`, and the names of `Cancelled` and of an unknown value in `MsgHatchMetrics`, which no send records. The solution build with `--no-incremental` has no errors and no warnings at the commits 9 and 11. | The copy of `pwsh ./coverage.ps1` that builds with `--artifacts-path` in a scratch folder, because the example apps of the owner lock `examples/*/bin/Debug`, and `dotnet build MsgHatch.slnx --no-incremental --artifacts-path` at the commits 9 and 11 |
| The new tests failed before the code of their commit existed, or with a break of the code that they cover. The allocation test of the resolution failed on the code before commit 9. The breaks failed: a notify of each channel of the registration (commit 3), the position of an outcome, a missing set for one route, the reverse order of a group, and a conflict check that passes each route (commit 4), the box of another tenant on a hit, and the box of no tenant after the limit (commit 5), two swapped outcome names (commit 6), no limit in the parallel options (commit 7), the owner tag of the default, a company id with no encoding, and an empty list of the missing owners on a hit (commit 8), the reverse order of the skipped subscriptions of one candidate and of a merged pair (commit 9), and a count that does not grow for a repeated channel after the first, and a merged pair that drops the skipped subscriptions of its candidates (commit 11). | The breaks, each one reverted after its run |

Commits:
1. "Plan phase 34: the open allocations of the hot path"
2. "Remove the closures from the event path of the pipeline"
3. "Notify each channel of an event one time with its stored topic"
4. "Compose the routes of each channel with no copies of the route list"
5. "Resolve the tenant services with boxed keys made one time"
6. "Record the outcome tags of a send with no boxing"
7. "Run the batch of DispatchPendingAsync with Parallel.ForAsync"
8. "Build the subscription cache keys with no owner part strings"
9. "Reduce the scratch allocations of the subscription resolution"
10. "Record the measured allocation budget of phase 34 in the design"
11. "Test the lines that the coverage report of phase 34 showed with no test"
12. "Record the commits and the checked facts of phase 34 in the plan"
13. "Record phase 34 as done in README and AGENTS.md"
14. "Read each list item one time in the loops of phase 34"
15. "Read each outcome one time in the older loops of the pipeline"

Commit 1 holds the plan and the decisions D280 to D282 (`7351cdc`), commit 2 item 2 (`35cec23`), commit 3 item 3 (`1c7ccfc`), commit 4 item 4 (`d2a80b7`), commit 5 item 5 (`a68c925`), commit 6 item 6 (`b20e87d`), commit 7 item 7 (`86b10b2`), commit 8 item 8 (`d2a08c8`), commit 9 item 9 (`e43e7ec`), commit 10 items 1 and 10 (`86e0dab`), commit 11 the tests of two behaviors that the first coverage report showed with no test (`fca40ed`), commit 12 the record of the phase (`713f996`), commit 13 README and AGENTS.md (`02820dc`), commit 14 the item reads of the loops of the phase (`6521f76`), and commit 15 the outcome reads of two older loops of the pipeline (`f7e7955`). The tag `phase-34` is on commit 15, and the record of the commits 14 and 15 is a commit after the tag. The commit `47f0384` of the session of phase 32 is on `main` between the commits 3 and 4, and changes one line of the record of phase 32. The unit test projects of the changed code passed at each commit. The phase adds no project and no package.

The implementation changed the plan in these points:
- Item 2: `Boundary.RunAsync` with a delegate stays for the dispatch loops, the inbox work, and the stored registry, and calls the new overload with a state argument.
- Item 4: the channels of an event are in `EventChannels`, a struct that keeps the first channel in fields and makes a list only for a second channel, so an event on one channel allocates nothing for them. A checked route holds one shared `Uninitialized` error until its composer writes its outcome (see the checked facts).
- Item 5: a second new test checks that a tenant after the limit of 1,024 boxes gets its own services. The boxes are static, so the test of two tenants uses tenant ids that no other test uses.
- Item 6: the theory has the five outcomes that a send records. `Cancelled` is not a row, because only `CancelAsync` of a store records it.
- Item 8: the tests of the keys of a company with `:` in its id and of a user, and of a hit with no store call, existed. A new test checks the owner tag of the default, which no test asserted before.
- Item 9: the capacity of a company scope is the number of the loaded users, because the users of the company are not known before the run. When the second pass of the overridable part finds fewer subscriptions than the first pass, it gives an array of the found subscriptions. Only a duplicate instance in the subscriptions of a call can cause this. `Merge` gives arrays, so the subscription router gives the pipeline an array of routes, and the pipeline composes them with no copy.
- Item 10: the design also changes the sections 4.3, 4.8, and 4.9, which described the grouping with `RouteGrouping.ByChannel`, the notify of each channel, and `Parallel.ForEachAsync`. The target of `RunAsync` does not hold, so section 4.11 records the measured value and its cause (D159).
- Commit 11 is new. The first coverage report showed two behaviors with no test: an event with routes on three channels, and a merged pair whose overridable candidate also skipped a subscription. So the plan of 12 commits became 13.
- Commits 14 and 15 are new. After the review of the owner, each loop of the phase and two older loops of the pipeline read each item one time, in place of two or three reads with the indexer. The behavior and the bytes do not change, so these commits add no test. The unit tests of Domain (1,559), Subscriptions (421), and Email (92) passed. So the phase has 15 commits.

Item 1 is a measurement, and its results go into commit 10.

Done when: the build has no errors and no warnings, all unit tests pass, the three benchmark projects run, section 4.11 has the measured table of the phase, and `pwsh ./coverage.ps1` passes.

### Phase 35: registry commands

Goal: an operator changes the stored settings of a channel and of a message type in the admin UI (D118, D284 to D287). The settings are the display name, the description, the enabled flag, and the disabled channels of a message type. A change needs no deploy, so an operator can stop a message type or a channel from the admin pages. The code still defines each channel and each message type (D117). The example app also gets a past history of the channel states, so that the History dialog of phase 32 has data to show (D288).

Starting point: the tag `phase-34` (`f7e7955`) and the record of its loop commits (`bc4dd2b`). The owner asked: "What are registry commands for? Won't channels and message types be registered always from code using DI setup extensions?" After the answer, the owner chose "Registry commands" as phase 35, and then asked: "Include some code for seeding test data simulating channel down events to be able to test the history feature on the UI". The registry routes `PUT` and `DELETE` exist since phase 16, and call `IRegistryWriter`. `IRegistryCommands` has only the channel check of phase 33. So today a person changes a setting only with a raw HTTP call, and the registry pages are read-only.

1. Check first, before the code:
   - The generator of D236 writes a `PUT` call with a body and a `DELETE` call that gives `Task<Error?>` on `IRegistryApi`, as it does for the template and subscription clients.
   - `NavLink` of the menu of `AdminUiExample` matches by prefix. So "Channels" marks `/msghatch/channels/{channel}/edit`, and "Message types" marks `/msghatch/message-types/{type}/edit`.
   - `RegistryManager` reads the entries of the writer in `GetChannelsAsync` and `GetMessageTypesAsync`, so it gives each entry with no second store read.
   - The SQLite dispatch store of `ExampleApp` accepts state changes with times in the past. For a row 40 days old, `GetChannelHistoryAsync` gives the row that was current at the start of the 30-day window.
2. Make the stateless rules of a registry entry public (D285):
   - `RegistryEntryRules` in `MsgHatch.Domain.Routing` becomes public, with `Check(ChannelEntry entry)` and `Check(MessageTypeEntry entry)`. They check the display name, the description, and the revision. They also check that `DisabledChannels` is not null and has each channel one time.
   - The check that each disabled channel is a channel of the message type needs the registry. So it stays in `StoredRegistry`, after the rules.
3. Add the stored entry to the registry queries (D286):
   - `RegistryChannel` gets `ChannelEntry? Entry`, and `RegistryMessageType` gets `MessageTypeEntry? Entry`: the stored entry, or null for no entry. `RegistryManager` takes each entry from the entries that it reads for the revisions. With no writer, each entry is null.
   - `RegistryChannelResponse` gets `entry` as a `ChannelEntryResponse`, and `RegistryMessageTypeResponse` gets `entry` as a `MessageTypeEntryResponse`.
   - Both entry bodies get their mappings back, `ToEntry`. A name that is not valid gives `Errors.SerializationFailed`.
4. Add the save and reset commands to the registry (D284):
   - `IRegistryCommands` gets `SaveChannelAsync`, `SaveMessageTypeAsync`, `ResetChannelAsync`, and `ResetMessageTypeAsync`, with the parameters and the results of `IRegistryWriter`. The revision of an entry is the expected revision, and a save ignores `LastModifiedAtUtc`, as the writer does.
   - `RegistryManager` runs `RegistryEntryRules` first, and then calls the writer. With no writer, each of the four commands gives `Errors.NotFound` with the message "The host has no registry writer. A setup in code cannot change the registry settings."
   - The `PUT` and `DELETE` routes call `IRegistryCommands` of `TenantKey.None`, as the GET routes call the queries (D270). `MapMsgHatchRegistry` maps them in each host. So a host with no writer gives 404 with the problem body and the message of the command. The test `Change_NoWriter_HasNoRoute` becomes a test of that body.
   - The logging wrapper of the commands logs each save and each reset with the events from 2917 on: the channel or the message type, the tenant of its key, and the new revision or the error type. It never logs a display name or a description.
5. Add the save and reset calls to the registry client (D284, D285):
   - `IRegistryApi` gets `PUT` and `DELETE` of `RegistryApiRoutes.Channel` and of `RegistryApiRoutes.MessageType`.
   - `ChannelEntryRequest.From(ChannelEntry)` and `MessageTypeEntryRequest.From(MessageTypeEntry)` give the bodies.
   - `HttpRegistryCommands` runs `RegistryEntryRules` first, and gives the error of a rule with no HTTP call.
6. Add the channel edit page (D287):
   - `ChannelEditPage` at `/msghatch/channels/{channel}/edit`, with the route builder `RegistryAdminRoutes.ChannelEdit(ChannelId)`, uses `AdminEditPage`.
   - The fields are the display name, the description, and the enabled flag. An empty text field saves null, which is the value of the code. With no stored value, the field is empty, and its placeholder shows the value of the code. With a stored value, the placeholder tells that an empty field gives the value of the code.
   - A help text under the enabled flag tells three facts. A change applies to the next events. The outbox still sends the messages that it holds. Another process applies the change within its refresh interval.
   - The actions are Back on the left, and Reset and Save on the right, with Save last (D261). Reset shows only for a stored entry, and asks with `DeleteDialog`.
   - `DeleteDialog` gets the parameter `Action`, with the default "Delete". It is the title, the text of the button, and the noun of the warning, for example "The reset cannot be undone." The details of a reset name the stored values, and tell that a disabled channel is enabled after the reset.
   - A save shows "Saved." and the saved values. A `Conflict` reads the channel again and shows `AdminErrors.ConflictText`. A reset reads the channel again, and shows the values of the code.
   - The view page gets Edit as its main action on the right, after History. Each row of the channel list gets Edit after View.
   - `AddMsgHatchAdminUI` keeps the page with `DynamicDependency` (D250).
7. Add the message type edit page (D287):
   - `MessageTypeEditPage` at `/msghatch/message-types/{type}/edit`, with the route builder `RegistryAdminRoutes.MessageTypeEdit(MessageType)`, uses `AdminEditPage`. It has the fields, the help text, the actions, and the results of the channel edit page.
   - It also shows one check box "Send on {channel}" for each channel of the message type. A cleared box puts the channel into `DisabledChannels`.
   - Each row of the message type list gets Edit.
   - `AddMsgHatchAdminUI` keeps the page with `DynamicDependency` (D250).
8. Seed an example history of the channel states in the example app (D288):
   - `ExampleStartup.SeedChannelHistoryAsync` writes past state rows through `SetChannelDownAsync` and `SetChannelUpAsync` of the outbox dispatch store. The times come before the current time of `TimeProvider`.
   - `Program.cs` calls it at startup in the `Development` environment only, after `RunAsync`, next to the routing verification.
   - It writes rows only for a channel with no state row, that is, a channel whose `Current` in `IRegistryQueries` is null. So a second start adds no row, and a real history stays as it is.
   - Email gets a down state for errors 40 days ago that ends 2 hours later, so the dialog shows the row that was current at the start of the window. Then Email gets a down state for errors 12 days ago for 45 minutes, a pause 5 days ago for 3 hours with the note "Example maintenance", and a down state for errors 26 hours ago for 7 minutes.
   - SignalR gets one down state for errors 20 days ago for 6 hours. Broker gets no row, so the list shows "No down state recorded".
   - Each down state for errors has the error `Unavailable`. Each down state ends with an `Up` row, so each channel is up after the seed, and the example sends as before.
   - The README of the example app tells that a new database file gets this history in `Development`, and that a channel with a state row gets no seed.
9. Describe the phase in the design: the sections 4.20, 4.23, and 4.24, and the edit pages in the README of the example app.

Tests:
- `RegistryEntryRules`: one row for each rule of a channel entry and of a message type entry with `ValidationFailed`, and a valid entry with no error.
- `RegistryCommandsTestsBase<T>`, with `RegistryManager` and with the HTTP client:
  - A save of a new channel entry gives the revision 1. The queries then give the stored values and the entry.
  - A save with an expected revision that is not the stored revision gives `Conflict`.
  - A save of a channel or a message type that is not registered gives `NotFound`.
  - A save of a message type with a disabled channel that is not its channel gives `ValidationFailed`.
  - A reset deletes the entry. The queries then give the values of the code, the revision 0, and no entry.
  - A host with no writer gives `NotFound` with the message of the command for each of the four commands.
  - An error of the writer is the error of the command.
- `RegistryManager`: an entry that breaks a rule calls no writer.
- The client: an entry that breaks a rule gives `ValidationFailed` and sends no request.
- The logging wrapper: one event for each save and each reset, with the error type for an error, and no display name or description in a record.
- The queries: the entry of a channel and of a message type, and null with no entry or with no writer.
- The contracts: each entry body maps to its entry and back, and a name that is not valid gives `SerializationFailed`. The query bodies carry the entry.
- The routes: `PUT` and `DELETE` call the commands of `TenantKey.None`, and a host with no writer gives 404 with `errorType` and the message of the command.
- The pages, with bUnit:
  - The fields with the stored values, the placeholders of the values of the code, and an empty field that saves null.
  - The check box of each channel of a message type, and a cleared box that puts its channel into `DisabledChannels`.
  - The text "Saved.", the text of `Conflict` with the second read, and the text of an error of the save.
  - Reset only for a stored entry, the question with its details, and the second read after a reset.
  - The Edit buttons of the view page, of the channel rows, and of the message type rows, and the addresses of `RegistryAdminRoutes`.
  - `DeleteDialog` with the action "Reset": its title, its button, and its warning.
- The example host: the menu marks "Channels" on the channel edit page, and "Message types" on the message type edit page.
- The example app, on a SQLite file with the fake clock:
  - The seed writes the rows of Email and SignalR with their times, and no row of Broker.
  - After the seed, each channel is up, and the history of Email has the row before the window.
  - A second seed adds no row, and a channel with a state row gets no seed row.
- Each new test fails first, or fails with a break of the code that it covers.

Checked facts (2026-11-19 and 2026-11-20), on Windows 11 build 26300 with the .NET SDK 10.0.404:

| Fact | Source |
|---|---|
| The generator of D236 writes a `PUT` call with a `[HttpBody]` body and a `DELETE` call that gives `Task<Error?>`, as `ISubscriptionApi` and `ITemplateApi` show. The four new calls of `IRegistryApi` build with no warning. | `ISubscriptionApi.cs`, `ITemplateApi.cs`, `IRegistryApi.cs`, and the solution build at commit 4 |
| The links Channels and Message types of `AdminUiExample` are `NavLink` with no `Match`, so they match by prefix. The menu marks "Channels" on `/msghatch/channels/Email/edit`, and "Message types" on `/msghatch/message-types/A/edit`. | `MainLayout.razor` and the new rows of `Get_Page_MarksOnlyTheMenuLinkOfItsPageAsActive` |
| `RegistryManager` calls `IRegistryWriter.GetEntriesAsync` one time in `GetChannelsAsync` and one time in `GetMessageTypesAsync`. So it takes each entry from the entries of that read, with no second store read. | `RegistryManager.cs` |
| The SQLite dispatch store adds a state row with a time in the past, because `ChannelState.AfterDown` and `AfterUp` take the given time with no check. For the down state of Email 40 days ago, the history of Email has 7 rows, and the oldest is the `Up` row of 39 days and 22 hours ago, which was current at the start of the window. A copy of `ExampleApp` in `Development` on port 5081, with its own SQLite file in a scratch folder, wrote the 8 rows of Email and the 2 rows of SignalR at its start. | `SqliteOutboxStore.cs`, `ExampleStartupUnitTests`, and the state table of the copy |
| The 35 unit test projects pass 5,129 tests and skip 1 test: the path limit test of the named pipe signal on Windows. The phase adds 115 tests and test rows. `MsgHatch.Domain.Tests.Unit` passes 1,607 tests, `MsgHatch.AspNetCore.Tests.Unit` 129, `MsgHatch.AdminUI.Tests.Unit` 117, `MsgHatch.Api.Contracts.Tests.Unit` 104, `MsgHatch.Api.Client.Tests.Unit` 68, `ExampleApp.Tests.Unit` 47, and `AdminUiExample.Tests.Unit` 15. The line coverage of the production code is 98.8 %: `MsgHatch.AdminUI` 100 %, `MsgHatch.Api.Contracts` 100 %, `MsgHatch.Domain` 99.6 %, `MsgHatch.AspNetCore` 97.9 %, and `MsgHatch.Api.Client` 85 %, with the same shared source `ErrorProblems` with no caller as in phase 31. The first run at commit 9 gave 98.7 %, and 96.3 % for `MsgHatch.AdminUI`, so commit 10 covers the lines that it found. The runs used the copy of `coverage.ps1` that builds with `--artifacts-path` in a scratch folder, because the example apps of the owner lock `examples/*/bin/Debug`. The solution build with `--no-incremental` has no errors and no warnings at commits 9 and 10. | The copies of `pwsh ./coverage.ps1` at commits 9 and 10, and `dotnet build MsgHatch.slnx --no-incremental --artifacts-path` |
| The new tests did not build before the code of their commit, because their types and members did not exist, or they failed with a break of the code that they cover. The breaks failed: the check of a duplicate disabled channel, the maximum length of a display name, and the check of the registry that each disabled channel is a channel of the message type (commit 2); the entry of a channel and of a message type in `RegistryManager`, the entry of the channel body, and the errors of an entry body that is not valid (commit 3); the rules in `RegistryManager`, the error type with no writer, the mapping of the save and reset routes, the revision and the entry name of the log record, and the disabled channels of the client body (commit 4); the rules in the client, and the description and the disabled channels of `From` (commit 5); the save of an empty field as null, the second read after a `Conflict` and after a reset, Reset with no stored entry, the noun of the warning of `DeleteDialog`, the placeholder of a stored text, the address of Edit on the view page, and the dynamic dependency of the page (commit 6); the disabled channels of a save, the state of the check boxes, the pending changes of the boxes, the address of Edit on the list, the dynamic dependency, and the reset text of a disabled message type (commit 7); and the seed of each channel, the seed with no up rows, and the first down state of Email inside the window (commit 8); and the pending changes "Description" and "Enabled" of the message type page (commit 10). One break did not build at first, because the compiler found an unreachable branch, so it ran again with a break that builds. | The breaks, each one reverted after its run |

Checks that the phase did not do:
- A browser check of the edit pages on copies of the example apps. A token with the role `RegistryAdmin` comes from `dotnet user-jwts`, which writes into the user secrets of `ExampleApp`. The bUnit tests check the fields, the save, the conflict, the reset, and the buttons.
- A break of the new menu rows of `AdminUiExample`. They check the `NavLink` of the host, which the phase does not change.
- A trimmed WebAssembly host and the dark mode of the new pages.
- The EF Core and MongoDB integration tests. The commands call `IRegistryWriter`, and the phase changes no settings store.

Commits:
1. "Plan phase 35: the registry commands"
2. "Make the stateless rules of a registry entry public"
3. "Add the stored entry to the registry queries"
4. "Add the save and reset commands to the registry"
5. "Add the save and reset calls to the registry client"
6. "Add the channel edit page"
7. "Add the message type edit page"
8. "Seed an example channel history in the example app"
9. "Describe the registry commands and the example history in the design"
10. "Pass the item to the save and the reset of the registry edit pages"
11. "Record the commits and the checked facts of phase 35 in the plan"
12. "Record phase 35 as done in README and AGENTS.md"

Commit 1 holds the plan and the decisions D284 to D288 (`db97b3d`), commit 2 item 2 (`3e45196`), commit 3 item 3 (`a3d5aa6`), commit 4 item 4 (`ef7f926`), commit 5 item 5 (`a48b09e`), commit 6 item 6 (`95dad2e`), commit 7 item 7 (`059d88f`), commit 8 item 8 (`5c73e88`), commit 9 item 9 (`08c5cae`), and commit 10 the fix of the lines that the first coverage run found with no test (`82a2519`). Item 1 is a check, and its results are in the checked facts. The unit test projects of the changed code passed at each commit. The solution build had no warning at commits 3 to 7, and with `--no-incremental` at commits 9 and 10. The phase adds no project and no package.

The implementation changed the plan in these points:
- Commit 10 is new. The coverage run at commit 9 found lines of the edit pages with no test: the null checks of the save and the reset, which no user action reaches, and the pending changes "Description" and "Enabled" of the message type page. So the buttons pass the item to the save and the reset, and the test of the pending changes covers the two lines. The phase has 12 commits.
- The new members of `IRegistryCommands` break the build of `HttpRegistryCommands`, and the use cases of `RegistryCommandsTestsBase` run on the HTTP client too. So commit 4 also adds the `PUT` and `DELETE` calls of `IRegistryApi` and the four methods of the client, with the bodies made in the adapter. Commit 5 adds `From` of the bodies in place of them, and the rules before a call.
- Another session made two commits of the subscription form and the example app between commits 3 and 4: `68d067d` and `d850359`. The history stays as it is.
- The logging wrapper has two events, 2917 for a save or a reset and 2918 for an error, with the name of the operation as a field, as the events 2906 and 2907 of the writer. It does not have a pair of events for each command.
- `RegistryManager` runs the rules before it checks the writer. So an entry that breaks a rule gives `ValidationFailed` also in a host with no writer.
- The edit pages show the pending changes, because section 4.24 asks this of each edit page. The plan did not name them.
- The public component `RegistryEntryFields` holds the display name, the description, the enabled flag, and the help text of both edit pages. The message type page puts its check boxes into its child content.
- A save reads the item again, so "Saved." shows the values of a second read. Only a read gives the value of the code for the placeholder of a text that the save cleared.
- A text field with only white space saves null, as an empty field does. The rules would give `ValidationFailed` for it.
- On the view page of a channel, History becomes an outline button, because Edit is the main action. Back of an edit page goes to its list.
- The test host `RegistryHost` of the route tests gets the property `Services`, and `RegistryTestHost` gets `EntryCommandRows` and `RunEntryCommandAsync`, which the use cases and the logging tests share.
- Commit 9 also changes the line of `MsgHatch.AdminUI` in the project table and the sentence about the setup of `MsgHatch.AspNetCore`. The decisions D284 to D288 stay "Recommended", as D280 to D282 of phase 34 do.

Done when: the build has no errors and no warnings, all unit tests pass, and `pwsh ./coverage.ps1` passes.

### Phase 36: delivery API

Goal: the delivery management gets the same API boundary as the subscriptions, the templates, and the registry (D289 to D294). `IDeliveryManager` becomes a query interface and a command interface. The bodies move to the contracts package, the routes write their errors with `ErrorProblems`, and `MsgHatch.Api.Client` gets the delivery client. Phase 37 adds the admin pages on this client.

Starting point: the tag `phase-35` (`7269509`) and four commits after it, which the record of phase 35 does not name:
- `9ee0be0`: the action bar of the edit pages shows the pending changes and the alert "Saved.".
- `0a35632`: each changed field of the edit pages has a yellow border.
- `940264e`: the list filters apply at each change, with no Show button.
- `b5be5f4`: the text filters of the template list apply after a pause of 500 ms.

The owner chose the roadmap item "Delivery API", then "Two phases (Recommended)", and then all four groups of pages for phase 37. For the content of a message, the owner asked for the options with short examples, and then chose "JSON text" (D290).

1. Check first, before the code:
   - The generator of D236 reads a success body of type `JsonElement` from a property of the JSON context. So the client gives the content text with no content type registry.
   - `ExampleApp` maps both delivery route groups with the one prefix `/api/delivery`, so one prefix of the client serves both groups.
   - The callers of `IDeliveryManager` are `MsgHatch.AspNetCore`, the setup of `MsgHatch.Domain`, `NotificationHistory` of `WolverineExample.Consumer`, and their tests.
   - List the tests of the delivery routes that expect 500 for an error type that has its own status in `ErrorProblems`. These tests change with item 6.
2. Write lists and times as query values in the generator (D293):
   - A query value of type `IReadOnlyList<T>`, with `T` a type that the generator writes already, gives one `name=value` pair for each item. Null and an empty list give no pair.
   - A `DateTime` value has the ISO 8601 text of D57, which is the format `O` of a UTC time. A value of another type still gives the diagnostic of an unsupported value.
3. Add the public rules of a delivery request (D292):
   - `DeliveryRules` in `MsgHatch.Domain.Management` checks the limit of a page, the ids, the order of the created times, a bulk action with no filter, the length of a pause note, a global action of a tenant key, and a `maxCount` less than 1. Each failed rule gives `Errors.ValidationFailed` with the message that the manager gives today.
   - `DeliveryManager` calls the rules in place of its own checks. The check that `finishedBeforeUtc` is not after the current time stays in the manager, because the clock of a client can differ.
4. Split the delivery manager into a query interface and a command interface (D289, D290):
   - `IDeliveryQueries` and `IDeliveryCommands` in `MsgHatch.Domain.Management` get the methods of D289, and `IDeliveryManager` goes away. `DeliveryManager` implements both.
   - The two content methods give `MessageContent(Guid Id, string Json)`, with the JSON that `MsgHatchJson` writes with the content type registry. `OutgoingMessageDetail.Trace` becomes the JSON text of the route trace, written with the newest registered version of its type. An unknown id still gives `Errors.NotFound`.
   - `AddMsgHatch` registers both interfaces as scoped default tenant services with their logging wrappers. `UseDeliveryQueries` and `UseDeliveryCommands` select another implementation, and a second selection gives the setup error of D62. The two wrappers keep the event ids of `DeliveryManagerLoggingWrapper`.
   - The routes call the queries or the commands of the key of `tenantId`. The bodies and the status codes do not change in this item.
   - `NotificationHistory` of the Wolverine example reads the summaries through `IDeliveryQueries`, and the typed envelope through `IOutboxStore.GetAsync` of its tenant.
5. Move the delivery bodies to the shared contracts (D291):
   - The delivery bodies and `DeliveryEndpointJson` move to `MsgHatch.Api.Contracts`, with public `From` methods. The response bodies get their mappings back to the domain types. A name that is not valid gives `Errors.SerializationFailed`.
   - `src/Shared/DeliveryApiRoutes.cs` holds the route paths, the names of the query values, and the format and the parse of a cursor `{CreatedAtUtc}_{Id}`. `MsgHatch.AspNetCore` and `MsgHatch.Api.Client` compile it.
   - The paths and the JSON of the bodies do not change, so the existing tests of the bodies stay green.
6. Write the errors of the delivery routes with `ErrorProblems`, and add the route of the test data (D233, D291):
   - The problem body gets `errorType` and `detail`. `ValidationFailed` and `NotFound` keep 400 and 404. Each other error type gets the status of `ErrorProblems.StatusCodeOf`, for example 503 for `Unavailable`. A body that is not valid JSON also gives a problem with `errorType`.
   - `DELETE {prefix}/test-data` in the overview group calls `DeleteTestDataAsync` of `TenantKey.None`, and gives 200 with `inboxMessages` and `outboxMessages`. A `tenantId` gives 400, as for pause, resume, and purge.
7. Add the delivery client to `MsgHatch.Api.Client` (D294):
   - `IDeliveryApi` with `[HttpApi(typeof(DeliveryEndpointJson))]`, the adapters `HttpDeliveryQueries` and `HttpDeliveryCommands`, `DeliveryApiClientOptions` with its validator, and `UseDeliveryApiClient` with an overload with `Uri baseAddress`.
   - The adapter of a tenant key sends its tenant as `tenantId`. The global commands send no `tenantId`.
   - Each adapter runs `DeliveryRules` first, and gives the error of a rule with no HTTP call.
   - The content methods read the body as a `JsonElement`, and give its text.
8. Describe the phase in the design: the sections 4.2, 4.7, 4.19, and 4.23, and the list of the AOT packages if it changes. Change `docs/stores.md` if it names the delivery manager. The README of the example app gets the route of the test data.

Tests:
- The generator: a list query value with two items, with one item, empty, and null. A `DateTime` value in the format `O`. The diagnostic for a list of an unsupported type.
- `DeliveryRules`: one row for each rule with `ValidationFailed`, and a valid request with no error.
- `DeliveryQueriesTestsBase<T>` and `DeliveryCommandsTestsBase<T>`, with `DeliveryManager` and with the HTTP client. They take their use cases from the existing tests of the delivery manager:
  - The pages of the inbox and the outbox with each filter, the cursor of the next page, and the test flag.
  - The detail of an outgoing message with its trace as JSON text and its tries, and `NotFound` for an unknown id.
  - The content of a received message and of an outgoing message as JSON text, and `NotFound` for an unknown id.
  - The traffic counts and the channel states.
  - Pause, resume, retry, cancel, delete test data, and purge, with their counts and their errors.
  - A global command of a tenant key gives `ValidationFailed`.
  - An error of a store is the error of the method.
- The client: a request that breaks a rule gives `ValidationFailed` and sends no request. The tenant of a key goes as `tenantId`. The options and their validation, the setup error of a second selection, `Unavailable` for a network error, and `Rejected` for 401 and 403.
- The logging wrappers: one event for each call, with the event ids of the delivery manager.
- The contracts: each body maps to its domain type and back, a name that is not valid gives `SerializationFailed`, and a cursor text gives its cursor and back.
- The routes: the problem body of D233 for each error type, and the route of the test data with its 400 for a `tenantId`.
- The Wolverine example: the catch-up still gives the missed notifications.
- Each new test fails first, or fails with a break of the code that it covers.

Checked facts (2026-11-20), on Windows 11 build 26300 with the .NET SDK 10.0.404:

| Fact | Source |
|---|---|
| The generator of D236 reads a success body of type `JsonElement` from a property of a JSON context in a referenced assembly. A probe in a scratch folder, with `[JsonSerializable(typeof(JsonElement))]` in one assembly and `Task<ErrorOr<JsonElement>>` in an `[HttpApi]` interface of a second assembly, built with warnings as errors, and `GetRawText` gave the text of the body. So the client gives the content text with no content type registry. | The probe, and the build of `IDeliveryApi` at commit 7 |
| `ExampleApp` maps `MapMsgHatchDelivery` and `MapMsgHatchDeliveryContent` with the one prefix `/api/delivery`, so one prefix of the client serves both groups. | `ExampleApplication.cs` |
| The callers of `IDeliveryManager` were `DeliveryEndpoints`, `DeliveryRequestValues`, and `DeliveryRoutes` of `MsgHatch.AspNetCore`, `ServiceCollectionExtensions` of `MsgHatch.Domain`, `NotificationHistory` of `WolverineExample.Consumer`, and the tests `DeliveryEndpointsUnitTests`, `DeliveryHost`, `DeliveryManagerUnitTests`, `DeliveryManagerLoggingWrapperUnitTests`, and `NotificationHistoryUnitTests`. `docs/stores.md` does not name the delivery manager. | A search of `src`, `tests`, `examples`, and the documents |
| One test of the delivery routes expected 500 for an error type with its own status in `ErrorProblems`: the row `Unavailable` of `ManagerError_Type_GivesItsStatusCodeWithAProblemDetailsOfTheErrorName`. Commit 6 replaces it with a row for each status of `ErrorProblems`. No test of `ExampleApp` calls a delivery route. | `DeliveryEndpointsUnitTests.Errors.cs` and a search of `tests/ExampleApp.Tests.Unit` |
| `MsgHatchJson` escapes each character that is not ASCII, so the JSON text of a content is ASCII. A break that decoded the content as ASCII did not fail for this reason, and a break of the id of the content replaced it. | The break of commit 4 |
| The 35 unit test projects pass 5,384 tests and skip 1 test: the path limit test of the named pipe signal on Windows. The phase adds 231 tests and test rows: `MsgHatch.SourceGeneration.Tests.Unit` passes 70 tests, `MsgHatch.Domain.Tests.Unit` 1,681, `MsgHatch.AspNetCore.Tests.Unit` 141, `MsgHatch.Api.Contracts.Tests.Unit` 141, `MsgHatch.Api.Client.Tests.Unit` 168, and `WolverineExample.Consumer.Tests.Unit` 16. The use cases of `DeliveryQueriesTestsBase` and `DeliveryCommandsTestsBase` run on `DeliveryManager` and on the HTTP client. The client tests of the templates and the subscriptions, which use the changed generator, pass 134 and 123 tests. The line coverage of the production code is 98.8 %: `MsgHatch.Api.Contracts` and `MsgHatch.SourceGeneration` 100 %, `MsgHatch.Domain` 99.6 %, `MsgHatch.AspNetCore` 97.6 %, and `MsgHatch.Api.Client` 93.2 %. Each new class of the phase has 100 %. The only class with lines that no test covers in these packages is the shared source `ErrorProblems`, as in phase 31. The run used the copy of `coverage.ps1` that builds with `--artifacts-path` in a scratch folder, because the example apps of the owner lock `examples/*/bin/Debug`. The solution build with `--no-incremental` has no errors and no warnings at commit 8. | The copy of `pwsh ./coverage.ps1`, and `dotnet build MsgHatch.slnx --no-incremental --artifacts-path` |
| `ExecuteAsync_ConsumerAndEmailPublisherWithTheNamedPipeSignal_PublisherSendsTheEventOfTheConsumerWithNoPoll` of `ExampleApp.Tests.Unit` failed two times of three while the coverage run ran the same test in a second process. Alone, the project passed three times of three, and the coverage run passed it. The second process is not the cause, because each run makes its own pipe name prefix. The cause: the test read the first wait of the fake clock, which is the refresh timer of the stored registry, as the wait of the publisher loop. So the consumer could notify before the publisher served the pipe of the email topic, and that notification is lost (D192). Commit `70e3ba4` makes the test wait until the pipe exists. | The test runs of `ExampleApp.Tests.Unit`, and the diagnostic and fault injection runs of commit `70e3ba4` |
| The new tests did not build before the code of their commit, because their types and members did not exist, or they failed with a break of the code that they cover. Against the routes of commit 5, 28 new tests and rows of commit 6 failed. The breaks failed: a list of each item type, a list as a route value, the time format `s`, and only the first item of a list (commit 2); empty ids, `createdTo` as no filter of an outbox bulk action, and no rules in the outbox page of the manager (commit 3); no trace text, the content with an empty id, no logging wrapper of a selected command, no log of the outgoing content, each route with the key of no tenant, and no envelope read in the catch-up (commit 4); no check of the target id, no trace in a detail, no check of a tenant, a cursor with six digits, and a channel state with no down state (commit 5); and queries with no tenant, no rule of the traffic, a content of each JSON kind, a cursor that does not parse as the last page, a pause with no note, and the query name `createdFromUtc` (commit 7). | The breaks, each one reverted after its run |

Checks that the phase did not do:
- A run of `ExampleApp` with a call of the new route `DELETE /api/delivery/test-data`, and of the error bodies. A token with the role `DeliveryAdmin` comes from `dotnet user-jwts`, which writes into the user secrets of `ExampleApp`. The route tests and the client tests run both route groups on a `TestServer`.
- A GET with many ids on Kestrel. The default limit of a request line of Kestrel is 8 KB, and an id is 41 characters of the query, so a query with about 190 or more ids can get 414. `DeliveryRules` allows 1,000 ids, as the manager did before. `TestServer` has no such limit.
- A trimmed or an AOT host with the delivery client. The trim and AOT analyzers of the client and contracts packages give no warning.
- The EF Core, MongoDB, SES, Resend, and Wolverine integration tests. The phase changes no store, no email client, and no broker client.

Commits:
1. "Plan phases 36 and 37: the delivery API and pages"
2. "Write lists and times as query values of the generated clients"
3. "Add the public rules of a delivery request"
4. "Split the delivery manager into query and command interfaces"
5. "Move the delivery bodies and route paths to the shared contracts"
6. "Write the errors of the delivery endpoints with ErrorProblems"
7. "Add the HTTP client of the delivery API"
8. "Describe the delivery API in the design"
9. "Record the commits and the checked facts of phase 36 in the plan"
10. "Record phase 36 as done in README and AGENTS.md"

Commit 1 holds the plans of phases 36 and 37 and the decisions D289 to D298 (`847bb0e`), commit 2 item 2 (`7022a1d`), commit 3 item 3 (`8a4ac90`), commit 4 item 4 (`e6e1e52`), commit 5 item 5 (`620c1f3`), commit 6 item 6 (`ae2bd2b`), commit 7 item 7 (`0ee1068`), and commit 8 item 8 (`3c7f78b`). Item 1 is a check, and its results are in the checked facts. The unit test projects of the changed code passed at each commit, and the solution build had no warning at each commit. The phase adds no project and no package.

The implementation changed the plan in these points:
- Commit 8 also changes the sections 2, 4.4, 4.6, 4.9, 4.15, 4.18, 6, and 7 of the design, because they named `IDeliveryManager` or the status 500. The decision rows keep their old names. `docs/stores.md` and the list of the AOT packages do not change.
- `MsgHatchJson` gets the internal `SerializeTrace`, which writes the trace with the newest registered version of its type. A trace type that the host does not register gives `Errors.SerializationFailed`, where the route threw `InvalidOperationException` before.
- The delivery manager was a singleton tenant service. D289 makes it a scoped default of both interfaces. So `NotificationHistory`, a singleton, opens its own DI scope for the queries. A message that a purge removes between the page and the store read is left out of the catch-up, where it gave `NotFound` before.
- The use cases of the four files of `DeliveryManagerUnitTests` move to the two base classes, which reuse `IRegistryQueriesTestHost<T>` and `InProcessRegistryQueriesHost`. `DeliveryTestServices` holds the substitutes, the host setup, and the theory rows, because a generic class has no static members (CA1000). `DeliveryManagerLoggingWrapperUnitTests` becomes `DeliveryLoggingWrappersUnitTests`, with one row for each of the 15 calls. A route test checks the commands of a tenant too.
- Commit 5 moves the name checks of the contracts from `RegistryWireValues` to `WireValues` in a file of its own, with a check of a tenant id, so the registry and the delivery bodies share them. The descriptor of a channel state from a body has the settings type `ChannelSettings`, because the body has no settings type.
- Commit 6 moves the problem writer of the registry routes to `ApiProblems`, so both route groups write one body, and `WithBodyAsync` needs no problem writer of its caller. A body that is not JSON content still gives 415 with no `errorType`.
- Commit 7 also gives `Errors.SerializationFailed` for a content body that is not a JSON object, because the null check of the generator does not see the JSON null of a `JsonElement`. `HttpRegistryHost` of the client tests also maps both delivery route groups, and the test of the public types of the client package names the three new types.

Done when: the build has no errors and no warnings, all unit tests pass, and `pwsh ./coverage.ps1` passes.

### Phase 37: delivery pages

Goal: an operator sees the traffic, the outbox, and the inbox in the admin UI (D295 to D298). The operator looks at one message with its tries, its trace, and its content, retries and cancels messages, pauses and resumes channels, and deletes the test data. The pages use the delivery client of phase 36.

Starting point: the commits of phase 36. The owner chose four groups of pages: "Outbox and messages", "Pause and resume", "Inbox", and "Traffic and cleanup".

1. Check first, before the code:
   - `AdminList` pages its items in the page. A list of server pages needs the buttons Newer and Older in place of the page numbers.
   - The token of `AdminUiExample` needs the roles `DeliveryAdmin` and `DeliveryContent`. A token with no role `DeliveryContent` gets `Rejected` only for the content.
   - A page formats the JSON text with `Utf8JsonWriter` and `Indented`, with no reflection, so a trimmed host keeps it (D237).
2. Add the outbox list page (D295):
   - `OutboxListPage` at `/msghatch/outbox`, with the route builder `DeliveryAdminRoutes.Outbox`, uses `AdminListPage`. Its filters are the status, the message type, the channel, the target, the created times, the source message, and the test flag.
   - The filters go into `OutboxQuery`, and apply at each change. A text filter applies after a pause of 500 ms, as in `b5be5f4`.
   - `AdminList` gets the parameter `Pager`, which replaces its page numbers. The outbox list gives the buttons Newer and Older. Older reads the page after the `Next` cursor. Newer goes back to the cursor of the page before, which the page keeps.
   - Each row shows the created time, the status as a badge, the message type, the channel, the target, the tries as "3 / 8", the last error, and the button View.
   - The toolbar has "Retry filtered" and "Cancel filtered", which are disabled with no filter (D292). Each one asks with `AdminDialog`, and then an alert shows "Matched N, changed M.". After the action, the page reads the list again.
   - `DeliveryAdminRoutes.OutboxOf(...)` gives the address of the list with filters, so another page links to it.
3. Add the outgoing message page (D296):
   - `OutgoingMessagePage` at `/msghatch/outbox/{id}` uses `AdminEditPage`. It shows the summary, the tries in a table, and the trace as formatted JSON.
   - The content shows only after a click on "Show content", so the page reads it only on request. With no role `DeliveryContent`, the content area shows the text of `Rejected`, and the rest of the page stays.
   - The actions are Back on the left, and Cancel and Retry on the right. Each one acts on the query of the one id, and shows its result. "Changed 0" shows "The message did not change, because it is done, cancelled, leased, or expired."
   - A link opens the page of the received message of its source.
4. Add the inbox list page and the received message page (D295, D296):
   - `InboxListPage` at `/msghatch/inbox` has the filters of `InboxQuery`, the same pager, and "Retry filtered" and "Cancel filtered".
   - `ReceivedMessagePage` at `/msghatch/inbox/{id}` reads its summary with a page of its one id, and shows the event on request, as in item 3. A link opens the outbox list with the filter of its source message.
5. Add pause and resume to the channel pages (D297):
   - The channel list and the channel view page also read `IDeliveryQueries.GetChannelsAsync`. They show the pending messages, the pending test messages, and the oldest due time of each channel. An error of the delivery query shows above the list, and the registry columns still show.
   - The button Pause opens `AdminDialog` with a note of at most 500 characters. The button Resume shows for a down channel. After an action, the page reads the channels again, so the state shows the new row.
6. Add the traffic page (D298):
   - `TrafficPage` at `/msghatch/traffic` shows the counts of the inbox and of the outbox, with the filters of `TrafficQuery`. Each count links to its list with the filters of its row.
   - "Delete test data" asks with `DeleteDialog`, and shows the two counts after the delete.
   - "Purge" opens a dialog with the finish time and the maximum count, and shows the two counts after the purge.
7. Show the delivery pages in the example admin host (D298):
   - `UseDeliveryApiClient` with the token handler, and the section `MsgHatch:DeliveryApiClient` with `http://localhost:5080/` and the prefix `/api/delivery`.
   - The menu and the home page get "Traffic", "Outbox", and "Inbox" after "Verification".
   - The README of the example app and AGENTS.md tell that the token needs the roles `DeliveryAdmin` and `DeliveryContent`, and name the 403 of a token with no such role.
   - `AddMsgHatchAdminUI` keeps each new page with `DynamicDependency` (D250).
8. Describe the phase in the design: section 4.24.

Tests:
- The pages, with bUnit:
  - The outbox list: the rows, each filter in the query, the buttons Newer and Older with their cursors, the empty state, and the texts of `Unavailable` and `Rejected`.
  - "Retry filtered" and "Cancel filtered": disabled with no filter, the question, the counts, and the second read.
  - The outgoing message page: the summary, the tries, the trace, the content only after the click, the text of `Rejected` in the content area, Cancel and Retry with their results, and `NotFound` with the link back.
  - The inbox pages: the same use cases, and the link to the outbox list of the source message.
  - The channel pages: the pending counts, the error of the delivery query above the list, Pause with its note and its length check, Resume only for a down channel, and the second read.
  - The traffic page: the counts with their links, "Delete test data" with its question and counts, and "Purge" with its dialog and counts.
  - The addresses of `DeliveryAdminRoutes`, and a `DynamicDependency` for each new page.
- The example host: the delivery client sends its calls with the token, and the menu marks each new page.
- Each new test fails first, or fails with a break of the code that it covers.

Checked facts (2026-11-20), on Windows 11 build 26300 with the .NET SDK 10.0.404:

| Fact | Source |
|---|---|
| `AdminList` paged its items in the page with `PageOf` and `AdminPager`, so a list of server pages needed the parameter `Pager`. With a pager, the list shows each item that it gets, and shows the pager also under an empty page. | `AdminList.razor` before commit 1 |
| `ExampleApp` puts the policy `DeliveryAdmin` on the group of `MapMsgHatchDelivery`, and the policy `DeliveryContent` only on the group of `MapMsgHatchDeliveryContent`. The client of phase 36 gives `Rejected` for a 403 with no `errorType`. So a token with no role `DeliveryContent` gets `Rejected` only for the content. | `ExampleApplication.cs`, and the client tests of phase 36 |
| `JsonDocument.Parse` and `Utf8JsonWriter` with `Indented`, the new line `\n`, and `JavaScriptEncoder.UnsafeRelaxedJsonEscaping` give no warning of the trim and AOT analyzers of `MsgHatch.AdminUI`. The page writes the JSON text as HTML text, which escapes it. | The build of `MsgHatch.AdminUI` at commit 2 |
| An `OutboxQuery` or an `InboxQuery` with `IncludeTest` false does not match a test message, also with its id in `Ids`. So Cancel and Retry of one message, and the read of one received message, send the test flag. A new test found the missing flag before commit 2. | `QueryFilters.Match`, and the test of commit 2 |
| Razor removes the white space after an `@if` block, so the buttons Pause and History of a channel row touched. A test of the row text found it, and the row gets a space after the block, as `ChannelStateBadge` does. | The test of commit 4 |
| `dotnet test -v q` writes no names of the failed tests, only their counts. The break runs of this phase recorded the counts, and each break failed at least one test of its filter. | The break runs |
| The 35 unit test projects pass 5,510 tests and skip 1 test: the path limit test of the named pipe signal on Windows. The phase adds 116 tests and 4 test rows to `MsgHatch.AdminUI.Tests.Unit`, which passes 243 tests, and 1 test and 5 test rows to `AdminUiExample.Tests.Unit`, which passes 21 tests. The line coverage of the production code is 98.8 %, and `MsgHatch.AdminUI` has 100 %, so each new class of the phase has 100 %. The first coverage run, after commit 7, gave 99.4 % for `MsgHatch.AdminUI`, with two classes under 100 %, and commit 8 adds their tests. The run used the copy of `coverage.ps1` that builds with `--artifacts-path` in a scratch folder, because the example apps of the owner lock `examples/*/bin/Debug`. The solution build with `--no-incremental` has no errors and no warnings at commit 9. | The copy of `pwsh ./coverage.ps1`, and `dotnet build MsgHatch.slnx --no-incremental --artifacts-path` |
| `ExecuteAsync_ConsumerAndEmailPublisherWithTheNamedPipeSignal_PublisherSendsTheEventOfTheConsumerWithNoPoll` of `ExampleApp.Tests.Unit` failed in the second of three coverage runs, while other `dotnet` processes ran on the machine. Alone, the project passed 47 tests, and the other two coverage runs passed it. The phase changes no code of `ExampleApp`. The cause is a fault of the test, which commit `70e3ba4` fixes (see the checked facts of phase 36). | The coverage runs, and a run of `ExampleApp.Tests.Unit` alone |
| The new tests did not build before the code of their commit, because their types and members did not exist, or they failed with a break of the code that they cover. The breaks failed: the token handler and the prefix of the delivery client, the pager of `AdminList`, the escape of `OutboxOf`, the address of a message, the dynamic dependency, the texts of the tries, the time, and the result, the badge Test, each filter of the query, the times in UTC, the test flag of the address, the select that applies at once, the pause, the guard of an earlier read, Newer, Older, the first page after a change of a filter, the bulk actions with no filter, the action of the question, Back, the second read, the result after a change of a filter, the class of an error, an error of the query, an invalid filter, and the empty text (commit 1, 35 breaks); the source link, the address of a received message, the summary fields, the empty time, the order and the error of the tries, the indents, the escape and the text that is not JSON, the content at once, the key of the content panel, the error of the content, the query of one id, Retry, the second read, the reason of no change, the actions with no message, Back, the dynamic dependency, the route constraint, the texts of no tries and no trace, and the classes of the result (commit 2, 23 breaks); the type, the test flag, the times, and the check of the inbox query, the test flag of `InboxOf` and of the address, the status that applies at once, the bulk actions, Retry, the question, View, the empty text, the tries, Newer, the guard of an earlier read, the result after a change of a filter, the test flag, `NotFound`, the source link, the content, Retry, the second read, and the reason of the received page, the default text and the classes of `DeliveryResultAlert`, two dynamic dependencies, the route constraint, and the reason of the outgoing page (commit 3, 31 breaks); the pending counts, the oldest due time, the error of the delivery query, Pause or Resume, the second read after a pause and a resume, the error of a resume, the space after Pause, the trim of the note, an empty note, the rule check of the note, the error of a pause, the reset of the note, and Back, on the list and on the view page (commit 4, 21 breaks); the status, the times, and the test flag of the traffic query, the test flag, the channel, and the times of the links, the second read, the question, Cancel, the counts and the classes of a delete, the default maximum and the default time of the purge, its rule, its time check, its UTC time, its error, its second read, Back, the status that applies at once, the error of the report, the dynamic dependency, the address, and the rows of the inbox (commit 5, 25 breaks); the menu links of the delivery pages (commit 6, 2 breaks); and the classes of the badges Done and Cancelled, and the loading state of the traffic lists (commit 8, 3 breaks). Two breaks of commit 5 passed at first: the test flag of an inbox link, because the inbox row of the test had no test flag, and a status change with no read, because the test changed the other filters after it. The test data and a new test fixed both gaps, and both breaks then failed. | The breaks, each one reverted after its run |

Checks that the phase did not do:
- A run of `AdminUiExample` against `ExampleApp` with a token of the roles `DeliveryAdmin` and `DeliveryContent`, and with a token with no such role. A token comes from `dotnet user-jwts`, which writes into the user secrets of `ExampleApp`. The host tests render each new page on a `TestServer` with a recording handler, and the client tests of phase 36 check the 403 of each route group.
- A look at the pages in a browser: the light and the dark mode, the inputs `datetime-local`, the pager, and the dialogs. The bUnit tests check the markup, not the layout.
- A trimmed or an AOT host with the delivery pages. The trim and AOT analyzers of `MsgHatch.AdminUI` give no warning, and a test checks a `DynamicDependency` for each page.
- The EF Core, MongoDB, SES, Resend, and Wolverine integration tests. The phase changes no store, no email client, and no broker client.

Commits:
1. "Add the outbox list page"
2. "Add the outgoing message page"
3. "Add the inbox pages"
4. "Add pause and resume to the channel pages"
5. "Add the traffic page"
6. "Show the delivery pages in the example admin host"
7. "Describe the delivery pages in the design"
8. "Test the status badges and the loading state of the traffic page"
9. "Record the commits and the checked facts of phase 37 in the plan"
10. "Record phase 37 as done in README and AGENTS.md"

The plan of this phase is in commit 1 of phase 36 (`847bb0e`). Commit 1 holds item 2 and the first bullet of item 7 (`1fa32ae`), commit 2 item 3 (`9843e2f`), commit 3 item 4 (`b5950ec`), commit 4 item 5 (`ce3e66a`), commit 5 item 6 (`e259c5f`), commit 6 the rest of item 7 (`9657f51`), commit 7 item 8 (`b982382`), and commit 8 the two tests that the coverage run asked for (`c510c23`). Item 1 is a check, and its results are in the checked facts. The unit test projects of the changed code passed at each commit, and the solution build had no warning at each commit. The phase adds no project and no package.

The implementation changed the plan in these points:
- Commit 1 already selects the delivery client in `AdminUiExample`, with the token handler and the section `MsgHatch:DeliveryApiClient`, because the host maps the new pages from commit 1, and the channel pages need `IDeliveryQueries` from commit 4. The host has no outbox store, so the default `DeliveryManager` cannot serve them. Commit 6 adds the menu, the home links, the README, and AGENTS.md.
- Each page gets its `DynamicDependency` in the commit of the page, and the test of the dependencies checks each commit.
- A value of the address that is not valid goes into its field, and the list shows `ValidationFailed`, so the user sees the field to correct. The subscription list drops such a value.
- Commit 2 adds `DeliveryAdminRoutes.Inbox` and `ReceivedMessage(Guid)` for the link to the source message, and commit 3 adds `InboxOf` for the links of the traffic page.
- Commit 3 moves the state of a delivery list from `OutboxListPage` and `CursorPages` to `DeliveryListState`, so both lists share the cursors, the pause, the guard of an earlier read, and the result. `DeliveryResultAlert` shows the result on the four pages.
- Cancel and Retry of one message ask no question, because the plan names none. "Retry filtered" and "Cancel filtered" ask first.
- The pause dialog checks the note with `DeliveryRules.CheckPause`, and has no character counter, so the limit of 500 characters stays in one place. Pause and Resume follow the state of `ChannelStateBadge`, so a row shows the button of its badge.
- The traffic page reads no filters from its address, because no page links to it with filters.
- Commit 4 changes the expected rows of three channel tests, because the rows have the new cells and the button Pause.
- The first coverage run, after commit 7, found two behaviors with no test: the badge classes of the statuses Done and Cancelled, and the loading state of the traffic lists. The new commit 8 adds their tests, so the record and the README become commits 9 and 10.

Done when: the build has no errors and no warnings, all unit tests pass, and `pwsh ./coverage.ps1` passes.

### Phase 38: Amazon SNS broker client

Goal: the broker channel publishes to Amazon SNS with the first direct broker client, `MsgHatch.Broker.AmazonSns` (D134, D299 to D302). SNS fans out each message to the SQS queues and the other subscriptions of its topic. The client uses the AWS SDK of the SES client, so a host keeps one SDK version and one credential setup.

Starting point: the tag `phase-37` (`b2ecb6b`). The owner chose "AmazonSns client" as the next phase. A separate session looks for the cause of the unstable named pipe test of `ExampleApp.Tests.Unit`, which phases 36 and 37 recorded. It does not change the broker code.

The broker contract has no destination and no JSON of the payload: `BrokerPublish` holds the message id, the tenant, the payload object, and the headers. Wolverine selects its routes from the payload type and writes the payload itself, but a direct client must do both. So item 2 adds the content type and the JSON type information of the payload to `BrokerPublish` (D299).

1. Check first, before the code:
   - The version of `AWSSDK.SimpleNotificationService` that uses the same `AWSSDK.Core` as `AWSSDK.SimpleEmailV2` 4.0.106.7, and the version of `AWSSDK.SQS` for the integration tests, in `Directory.Packages.props`.
   - The request of the `Publish` action in the pinned version: `TopicArn`, `Message`, and `MessageAttributes` with `DataType` and `StringValue`, and `MessageId` of the response. Also the exception types of D302.
   - The limits in the documentation of SNS: at most 10 message attributes to an SQS subscription with raw message delivery, the rules of an attribute name, an empty attribute value, and the size limit of a message with its attributes. The plan knows 256 KiB for the size, and the check confirms or corrects it.
   - `ContentJsonTypeInfo.Options.GetTypeInfo(payload.GetType())` of the content type entry gives the type information of the payload for a source-generated context of a consumer, for example the context of `BrokerEnvelopeNotice` in `ExampleApp` and `BrokerTestJsonContext` of the broker tests.
2. Add the content type and the payload JSON to a broker publish (D299):
   - `BrokerPublish` gets `string ContentType`, the content type of the outgoing message, for example `Broker:DEvent`, and `JsonTypeInfo PayloadJson`, the JSON type information of the run-time type of the payload.
   - `BrokerTransport` gets `IContentTypeRegistry`, finds the entry of the content type and the version of the message, and takes `PayloadJson` from the options of its JSON type information. A message with no entry gives `Errors.ValidationFailed`.
   - `WolverineBrokerClient` does not change, because Wolverine writes the payload itself.
3. Add the package `MsgHatch.Broker.AmazonSns` (D300, D301):
   - `AmazonSnsBrokerClient : IBrokerClient` on `IAmazonSimpleNotificationService`. Each publish is one `Publish` call: the topic of the content type, the payload JSON as the message, and each header as a `String` message attribute. The message id of the response is the provider message id of the receipt.
   - `AmazonSnsOptions` in the section `MsgHatch:AmazonSns` holds `Region`, `DefaultTopicArn`, and `Topics`, a map from a content type to a topic ARN. A content type with no topic and no default topic gives `Errors.NotFound` with no call.
   - The client gives `Errors.ValidationFailed` with no call for a message with more than 10 headers, and for a header name or value that SNS does not accept as a message attribute.
   - The source-generated validator of the options checks the region, each ARN, and that at least one topic exists. It rejects a FIFO topic, an ARN that ends with `.fifo`.
   - `UseAmazonSnsBroker(Action<AmazonSnsOptions>? configure = null)` registers the options, the validator, the SDK client of the region, and the client with `UseBrokerClient`. The package references only `MsgHatch.Broker` and `AWSSDK.SimpleNotificationService`.
   - The solution gets the package and `tests/MsgHatch.Broker.AmazonSns.Tests.Unit`.
4. Map the errors and check the health of the SNS client (D302):
   - An `IExceptionClassifier<AmazonSnsBrokerClient>` of the host comes first, as for the SES client.
   - `ThrottledException` gives `Errors.RateLimited`. `InvalidParameterException` and `InvalidParameterValueException` give `Errors.Rejected`, also for a message over the size limit.
   - `NotFoundException`, `AuthorizationErrorException`, and the KMS exceptions give `Errors.Unavailable` and a Critical log, so the messages stay pending until an administrator acts.
   - `HttpRequestException`, `InternalErrorException`, and a service status of 500 or more give `Errors.Unavailable`. A cancelled token gives `Errors.OperationCancelled`. Each other exception gives `Errors.Exception`.
   - `CheckHealthAsync` calls `GetTopicAttributes` for each configured topic, and gives the first error.
5. Add the integration tests of the SNS client:
   - `tests/MsgHatch.Broker.AmazonSns.Tests.Integration` reads the region, the topic ARN, and the URL of an SQS queue with a raw message delivery subscription to the topic from `appsettings.Local.json`, which git ignores. `appsettings.json` next to it describes each key. With no settings, each test is skipped.
   - A publish arrives in the queue with the payload JSON as the body and each header as a message attribute. The test deletes each message that it receives.
   - The health check gives no error, and a topic that does not exist gives `Errors.Unavailable`.
   - The phase creates no AWS resource. The owner creates the topic, the queue, and the subscription.
6. Describe the phase in the design: the sections 4.2, 4.6, 4.7, 4.15, and 5.5, and the broker part of section 6. `AGENTS.md` gets the command of the SNS integration tests with its settings, as for the SES tests.

Tests:
- The broker transport: `BrokerPublish` carries the content type and the JSON type information of the payload, and a message with no entry gives `ValidationFailed`. The tests of the Wolverine client stay green.
- `AmazonSnsBrokerClientUnitTests` gets `BrokerClientTestsBase<AmazonSnsBrokerClient>`, with a driver on a substitute of `IAmazonSimpleNotificationService`.
- The request: the topic of the map, the default topic, the payload JSON as the message, each header as a `String` attribute, and the provider message id of the receipt.
- The limits: no topic gives `NotFound` with no call, 10 headers pass, 11 headers give `ValidationFailed` with no call, and an invalid attribute name or an empty value gives `ValidationFailed` with no call.
- The errors: one row for each exception of D302, the classifier first, a cancelled token, and the Critical log of the errors that need an administrator.
- The health check: one `GetTopicAttributes` call for each configured topic, and the first error.
- The options: one row for each rule of the validator, the binding of the section, and the registrations of `UseAmazonSnsBroker`.
- The integration tests of item 5, skipped with no settings.
- Each new test fails first, or fails with a break of the code that it covers.

Checked facts (2026-11-22), on Windows 11 build 26300 with the .NET SDK 10.0.404:

| Fact | Source |
|---|---|
| `AWSSDK.SimpleEmailV2` 4.0.106.7 needs `AWSSDK.Core` [4.0.102.14, 5.0.0). `AWSSDK.SimpleNotificationService` 4.0.100.22 and `AWSSDK.SQS` 4.0.100.21 need the same lower bound. The versions just before them need 4.0.102.13, and the versions after them need a later core. The restore of the SNS package and of its integration tests gives `AWSSDK.Core` 4.0.102.14. | The nuspec files on nuget.org, and `project.assets.json` |
| In 4.0.100.22, `PublishRequest` has `TopicArn`, `Message`, and `MessageAttributes`, `MessageAttributeValue` has `DataType` and `StringValue`, and `PublishResponse` has `MessageId`. `GetTopicAttributesRequest` has `TopicArn`. Each exception type of D302 exists. The error list of `Publish` in the API reference has `KMSThrottling`, and no `Throttled`. | The XML documentation of the package, and the API reference of `Publish` |
| SNS sends at most 10 message attributes to an SQS subscription with raw message delivery, and discards a message with more attributes there. A name has only `A-Z`, `a-z`, `0-9`, `_`, `-`, and `.`. It has no period at the start or at the end, and no two periods in a row. It has at most 256 characters, and does not start with `AWS.` or `Amazon.` in any case of the letters. A name, a type, and a value must not be empty. SNS does not support a surrogate in a value. | The SNS developer guide: message attributes, and raw message delivery |
| The size limit of a message with its attributes is the attribute `MaximumMessageSize` of the topic: 262,144 bytes (256 KiB) by default, and at most 1,048,576 bytes (1 MiB). A larger message gives `InvalidParameter`. So the 256 KiB of item 1 is only the default. | The API references of `Publish` and of `MessageAttributeValue` |
| SNS does not retry a delivery that fails with a client-side error, and discards the message, unless the subscription has a dead-letter queue. Section 6 of the design records this risk. | The SNS developer guide: dead-letter queues |
| `ContentJsonTypeInfo.Options.TryGetTypeInfo(payload.GetType())` gives the type information of the payload for `BrokerTestJsonContext`, and for the context of `BrokerEnvelopeNotice` of `ExampleApp`. The transport tests write the payload with it, and the broker test of `ExampleApplicationUnitTests` sends through the transport. | The tests of commit 2, and `ExampleApp.Tests.Unit` |
| The configuration key `MsgHatch:AmazonSns:Topics:Broker:DEvent` binds no topic, because the colon separates two sections. So the key of `Topics` is the name of the registration, for example `DEvent`. | A probe test, which the phase did not keep |
| A client that reads `IOptions<T>.Value` in its constructor gives the failure of invalid options two times at startup, in an `AggregateException`, because the setup check builds the transports. So the SNS client reads its options at the first call, as the SES client does. | The startup test of commit 3 |
| `[RegularExpression]` accepts an empty text. So `Validate` of the options checks `DefaultTopicArn`, and an empty value is not valid. | The options tests of commit 3 |
| xUnit gave a theory row with one surrogate in a text to the test with a changed text, and the client sent it. A `[Fact]` that makes the text in code checks one surrogate. | The first run of the tests of commit 3 |
| After D299, 5 tests of the broker and Wolverine setup methods sent a broker message whose content type the host did not register, and failed with `ValidationFailed`. They get a registry with the entry of `BrokerMessages`. | The run of the broker tests at commit 2 |
| The 36 unit test projects pass 5,627 tests and skip 1 test: the path limit test of the named pipe signal on Windows. The phase adds 3 tests to `MsgHatch.Broker.Tests.Unit`, which passes 49 tests, and the new `MsgHatch.Broker.AmazonSns.Tests.Unit` passes 109 tests. `MsgHatch.Broker.Wolverine.Tests.Unit` passes 39 tests, as before. The 3 SNS integration tests are skipped with no settings. The line coverage of the production code is 98.8 %, and `MsgHatch.Broker` and `MsgHatch.Broker.AmazonSns` have 100 %. No example app of the owner ran, so `pwsh ./coverage.ps1` ran as it is. The solution build with `--no-incremental` has no errors and no warnings at commit 6. | `pwsh ./coverage.ps1`, and `dotnet build MsgHatch.slnx --no-incremental` |
| The new tests did not build before the code of their commit, or failed before it, or failed with a break of the code that they cover. The 3 new transport tests failed against a transport with no lookup. The breaks of commit 3 failed: the topic map, the default topic, the prefix of the key, the limit of 10 headers in both directions, each rule of an attribute name, the empty value, the surrogate, the data type, the message, the receipt, the topic ARN, the mapping of `Rejected`, `Unavailable`, and a cancelled token, the error text, the FIFO topic, each rule of the options, the section, the client of the host, the region, the startup validation, the configuration in code, and the missing region (36 breaks). 25 new tests of commit 4 failed before its mapping, and its breaks failed: the health check, its distinct topics, its default topic, the topics of the registrations, its order, its cancelled token, the classifier and its order, each exception of D302, the 500 boundary, the Critical log, its event id, its level, its text, and the log of a rejected message (25 breaks). A break that failed only on a compile error ran again in a form that compiles. | The break runs |

Checks that the phase did not do:
- The SNS integration tests against a real topic and queue. With no settings, the 3 tests are skipped. The owner creates the topic, the queue, and the subscription with raw message delivery, and puts the settings into `appsettings.Local.json`.
- A message over the size limit of a real topic. A unit test checks that `InvalidParameterException` gives `Rejected`.
- The SES, Resend, Wolverine, EF Core, and MongoDB integration tests. The phase changes no store and no email client, and the Wolverine client does not change.
- The benchmarks. The broker transport adds one lookup in a frozen dictionary, and one type lookup that the JSON options cache, for each send.

Commits:
1. "Plan phase 38: the Amazon SNS broker client"
2. "Add the content type and the payload JSON to a broker publish"
3. "Add the Amazon SNS broker client"
4. "Map the errors and check the health of the Amazon SNS client"
5. "Add the integration tests of the Amazon SNS client"
6. "Describe the Amazon SNS broker client in the design"
7. "Record the commits and the checked facts of phase 38 in the plan"
8. "Record phase 38 as done in README and AGENTS.md"
9. "Wait the time that the provider asks for after a rate limit"
10. "Read the wait of the provider in the SES and Resend clients"

Commit 1 holds the plan and the decisions D299 to D302 (`8d69922`). Commit 2 holds item 2 (`f663036`), commit 3 item 3 (`0ef9c1a`), commit 4 item 4 (`3a60c5f`), commit 5 item 5 (`1442e8d`), and commit 6 item 6 (`cdef2da`). Item 1 is a check, and its results are in the checked facts. The unit test projects of the changed code passed at each commit. The phase adds the projects `MsgHatch.Broker.AmazonSns`, `MsgHatch.Broker.AmazonSns.Tests.Unit`, and `MsgHatch.Broker.AmazonSns.Tests.Integration`, and the packages `AWSSDK.SimpleNotificationService` 4.0.100.22 and `AWSSDK.SQS` 4.0.100.21.

The implementation changed the plan in these points:
- The key of `Topics` is the name of the broker registration, for example `DEvent`, and not the content type, because a configuration key cannot hold the colon of `Broker:DEvent`. D300 records it.
- `KMSThrottlingException` gives `RateLimited`, and not `Unavailable` with a Critical log, because a throttle needs no administrator. D302 records it.
- A payload type that the JSON options of the envelope do not know also gives `ValidationFailed`, as a message with no entry does.
- The tests of the broker and Wolverine setup methods with no message type register the content type of `BrokerMessages` with `BrokerHost.AddContentTypeOfBrokerMessages`, because the transport needs its entry.
- The contract tests of a broker client compare the payload by value, and give the header `msghatch-tenant` for a tenant, as the transport does. The SNS client writes the payload as JSON, and SNS has no field for the tenant.
- The SNS client also refuses a header value with a surrogate, because SNS does not support it.
- The health check calls `GetTopicAttributes` one time for each distinct topic, and stops at the first error.
- The design also gets `AmazonSnsOptions` in section 4.4, the SNS client in the list of classifiers of section 4.12, and two risks in section 6: the delivery of SNS to the subscriptions, and the limits of the message attributes.
- After the record, the owner asked for commit 9 (D306). `Error` gets the optional `RetryAfter`, and the next try time of the inbox and the outbox waits for it, at most `MaxDelay` after the try. The SNS client reads `x-amz-retry-after` and `Retry-After`, and gives `RateLimited` for the codes `Throttled`, `Throttling`, and `KMSThrottling` and the status 429. A real SNS client of SDK 4.0.100.22 on a fake HTTP handler showed that a throttle of `Publish` arrives as `AmazonSimpleNotificationServiceException` with the code `Throttled`, not as `ThrottledException`, so the mapping of commit 4 gave `Exception` for it. The SDK sent one request for the status 400 and for 429, with no retry of its own, and its throttle check gives false for the codes `Throttled` and `KMSThrottling`. The SDK uses `x-amz-retry-after` only when the environment variable `AWS_NEW_RETRIES_2026` is `true`. No source of this phase shows that SNS sends either header. The 10 new test rows of the SNS client and 4 of the 7 new test rows of the domain failed before the code. The other 3 rows, where the schedule wins or the error is permanent, failed with a break, and each of the 14 breaks of the code failed at least one test.
- Then the owner asked for commit 10: the SES client and the Resend client also read the wait of the provider (D306). The reader of the two AWS headers moves from the SNS client into `src/Shared/AwsRetryAfter.cs`, which the SNS package and the SES package compile. A real SES client of SDK 4.0.106.7 on a fake HTTP handler gave `TooManyRequestsException` for a 429 response and `LimitExceededException` for its code, so the SES mapping already gives `RateLimited`. The SDK sent 3 requests, because it retries these codes itself, and the inner `HttpErrorResponseException` gave both wait headers. The Resend API reference documents the headers `retry-after`, the seconds to wait before the next request, and `ratelimit-reset` on a 429 response, and the client reads only `retry-after`. 5 of the 12 new test rows failed before the code. The rows with no wait passed before it, and failed with a break. Each of the 9 breaks of the code failed at least one test. The Wolverine client gives no wait, because Wolverine does not give the response of its transport, and an exception classifier gives only an error type.

Done when: the build has no errors and no warnings, all unit tests pass, the SNS integration tests pass or are skipped with no settings, and `pwsh ./coverage.ps1` passes.

### Phase 39: MongoDB change stream signal

Goal: the dispatch loops of hosts on different machines wake when a message arrives, with a change stream of the MongoDB stores in place of a poll (D102, D303, D304). The signal needs no infrastructure other than the MongoDB replica set of the stores.

Starting point: the plan of phase 38 (`8d69922`). The owner asked how modular the step between the inbox and the routing is. `IDispatchSignal` already separates it, with `InProcessDispatchSignal` and `NamedPipeDispatchSignal`. The owner then chose "Plan the Mongo change stream as Phase 39 and the direct RabbitMQ as Phase 40". A change stream needs a replica set, for example the local `dev-mongo` with `replicaSet=rs0`.

1. Check first, before the code:
   - The collection names of the inbox and the outbox of `MongoDbStoreOptions`, and the field of the channel of an outgoing document, so that a change gives its topic.
   - With a store for each tenant, each tenant has its own database. A watch on the client, with a filter on the collection names, sees the changes of each tenant database.
   - `InProcessDispatchSignal` is public, so the new signal gives each change to a local instance, and keeps the wait rules of section 4.6 with no copy.
   - A change stream of the pinned driver on a server with no replica set gives an error that the signal can return.
2. Add the change stream signal to `MsgHatch.Data.MongoDb` (D303):
   - `MongoDbDispatchSignal : IDispatchSignal` watches the inserts into the inbox and the outbox. An insert into the inbox notifies `DispatchTopic.Inbox`, and an insert into the outbox notifies the topic of its channel.
   - The watch runs in one background task from the first `WaitAsync`, and feeds a local `InProcessDispatchSignal`. `WaitAsync` waits on that local signal.
   - A broken watch makes `WaitAsync` return an error, so a loop polls (section 4.9), and the signal starts the watch again after a pause. The signal keeps no resume token, because a missed change costs at most one poll.
3. Notify through a signal collection (D304):
   - `NotifyAsync` inserts a small document with the topic into the collection `msghatch_signals`, which has a TTL index of one minute. The watch also watches this collection. So a retry, a resume, or a channel check on one machine wakes the loops of each machine.
   - An insert of a message needs no notify, because its insert is the signal. The pipeline still calls `NotifyAsync`, and the extra document costs one small write.
4. Add the setup method (D303):
   - `UseMongoDbDispatchSignal()` selects the signal with the client and the database of the MongoDB stores. A setup with no MongoDB stores gives a setup error.
5. Add the integration tests: two signals on one database of the integration MongoDB, an insert through the store of the first signal, and the wake of a wait of the second signal, for the inbox, a channel, and a `NotifyAsync`.
6. Describe the phase in the design: the sections 4.2, 4.6, 4.7, and 4.9, and `docs/stores.md`.

Tests:
- Unit tests: the mapping of a change to its topic, and the setup error with no MongoDB stores.
- Integration tests of item 5, and a wait that returns an error when the watch breaks.
- The dispatch signal contract tests of phase 21, with the MongoDB signal as one more implementation.
- Each new test fails first, or fails with a break of the code that it covers.

Checked facts (2026-11-22), on Windows 11 build 26300 with the .NET SDK 10.0.404:

| Fact | Source |
|---|---|
| The collection names are constants of the stores, not options: `MongoDbInboxStore.CollectionName` is `MsgHatchInbox`, and `MongoDbOutboxStore.CollectionName` is `MsgHatchOutbox`. `MongoDbStoreOptions` has only `ConnectionString` and `DatabaseName`. An outgoing document has the name of its channel in the top-level field `channel`, which the claim filter and `IX_MsgHatchOutbox_Due` also use. | The source of the stores and of their options |
| The MongoDB stores have no storage for each tenant: only the SQLite package calls `UseStorageForEachTenant`. They keep each tenant in one database, with the field `tenant`. So a watch of the database of the stores sees each tenant. A watch of a database did not see an insert into `MsgHatchInbox` of another database, and a watch of the client sees each database of the server. So the signal watches the database, not the client. | The source, section 5.5 of `docs/stores.md`, and a probe on dev-mongo |
| `InProcessDispatchSignal` is a public sealed class of `MsgHatch.Domain`, so the signal gives each change to its own instance. | The source |
| On a `mongo:8.3` server with no replica set, `WatchAsync` of `MongoDB.Driver` 3.14.0 throws `MongoCommandException` with the code 40573: "The $changeStream stage is only supported on replica sets or mongos." It is a `MongoException`, so the signal gives `Errors.Unavailable`. | A probe on a container of the phase, which the phase removed |
| A watch of a database opens also when the database does not exist yet. With the filter of the signal, an update, an insert into `MsgHatchChannelState`, and a signal document of the own sender give no event. The first event came 140 ms after the open. | A probe on dev-mongo, and the tests of commits 2 and 3 |
| A `dropDatabase` while the watch is open ends the change stream: `MoveNextAsync` returns false after one batch, with no exception. `MoveNextAsync` with a cancelled token throws `TaskCanceledException`. So the test of a broken watch drops its database. | A probe on dev-mongo, and the tests of commit 2 |
| `WithReplicaSet()` of `MongoDbBuilder` in Testcontainers.MongoDb 4.16.0 starts a replica set with one member, and a change stream works on it. The container with no such call has no replica set, so the fixture of the MongoDB integration tests now calls it. | A probe with Testcontainers |
| The contract test `NotifyAsync_NoWaitingLoop_ReturnsAtOnceWithNoError` requires a `NotifyAsync` that completes at once. So a writer in the background inserts the signal document. | `DispatchSignalTestsBase` |
| A wait on `InProcessDispatchSignal` with a cancelled token returns at once and takes no notification. So `WaitAsync` needs no extra return for a broken watch: the link to the token of the watch gives the same result. A break of that return passed each test, so commit 2 removed it. | Break B6 of commit 2 |
| The build of the solution with `--no-incremental` has no errors and no warnings. The 36 unit test projects pass 5,656 tests and skip 1 test: the path limit test of the named pipe signal on Windows. The phase changes no unit test project, so these counts are the same as after phase 38. The line coverage of the production code is 98.8 %, and it does not measure `MsgHatch.Data.MongoDb`, because no unit test project loads it. `MsgHatch.Data.MongoDb.Tests.Integration` passes 380 tests, 24 more than before: 16 tests of the signal with the 6 contract tests, and 8 tests of the setup method. `MsgHatch.Subscriptions.Data.MongoDb.Tests.Integration` passes 50 tests. Each MongoDB run used dev-mongo, a replica set with one member. | `dotnet build MsgHatch.slnx --no-incremental`, `pwsh ./coverage.ps1`, and the two MongoDB integration projects |
| The new tests did not pass before the code of their commit, or failed with a break of the code that they cover. Commit 2: the 6 new tests failed against a signal that notified only its own process, and 10 of 11 breaks failed (the filter of the inserts, the inbox in the filter, the topic of the inbox, the topic of a channel, the cancel of the waits at a break, the error of a waiting loop, the new token after an open, the one Warning of an outage, the Information, and its condition). Commit 3: the 4 new tests failed before the code, and 11 breaks failed (the filter of the own documents, the signal collection in the filter, the topic of a signal document, the topic of the insert, its time, the TTL, the error of the last insert, the local notify, the wake of the writer, the topic of the writer, and the TTL index). Commit 4: 7 of the 8 new tests failed against a setup method that did nothing, the test of a valid setup failed with a break, and 7 breaks failed (the check of the stores in both directions, the poll of the inbox and of the outbox, the name of the setup method, the database, and the check at the start). | The break runs |

Checks that the phase did not do:
- A run of the MongoDB integration tests with no connection string, which starts the replica set container of the fixture. A probe started the same container, and a change stream worked on it.
- A sharded cluster, and a change of the primary of a replica set. The driver resumes a change stream itself one time after an error that it can resume.
- The SES, Resend, Wolverine, SNS, and EF Core integration tests. The phase changes no other store and no client.
- The benchmarks. The signal is not on the path of a send.

Commits:
1. "Plan phases 39 and 40: the MongoDB and RabbitMQ dispatch signals"
2. "Add the MongoDB change stream dispatch signal"
3. "Notify the MongoDB dispatch signal through a signal collection"
4. "Add the setup method of the MongoDB dispatch signal"
5. "Describe the MongoDB dispatch signal in the design"
6. "Record the commits and the checked facts of phase 39 in the plan"
7. "Record phase 39 as done in README and AGENTS.md"

Commit 1 holds the plans of phases 39 and 40 and the decisions D303 to D305 (`b5d4694`). Commit 2 holds item 2 (`05f900c`), commit 3 item 3 (`aa08024`), commit 4 item 4 (`7a31c58`), and commit 5 item 6 (`fa88b4b`). The integration tests of item 5 are in the commits of the code that they test. Item 1 is a check, and its results are in the checked facts. The phase adds no project and no package.

The implementation changed the plan in these points:
- The signal watches the database of the stores, not the client, because the MongoDB stores keep each tenant in one database. D303 records it.
- `MongoDbDispatchSignal` is public, with a public constructor, as the stores of the package are. The tests of commits 2 and 3 build it directly.
- `NotifyAsync` returns at once, as the contract requires. A writer in the background inserts the signal document, and many notifications before one insert become one document for each topic. `NotifyAsync` returns `Errors.Unavailable` while the last insert failed, so the pipeline logs it.
- A signal document holds the id of its signal instance in `sender`, and the watch skips the own documents, so a process does not wake itself a second time.
- `UseMongoDbDispatchSignal` requires `EnablePolling` true for the inbox and the outbox (D190), because a change between the end of a watch and the next open reaches a loop only through its poll.
- The unit tests of the plan, the mapping of a change to its topic and the setup error, are in the integration project, as the tests of the options validator and of the setup method of the stores are. The package has no unit test project, and `coverage.ps1` measures only the unit test projects. The mapping tests use the real change events of the server.
- The signal logs the events 2800, a Warning one time for each outage, and 2801, an Information at the next open. The pause before the next open is a fixed 5 seconds.
- The fallback container of the integration fixture is a replica set with one member.
- `AGENTS.md` had "Phases 0 to 37 are done" after phase 38. Commit 7 writes "Phases 0 to 39 are done".

Done when: the build has no errors and no warnings, all unit tests pass, the MongoDB integration tests pass, and `pwsh ./coverage.ps1` passes.

### Phase 40: RabbitMQ dispatch signal

Goal: the dispatch loops of hosts on different machines wake through a fanout exchange of RabbitMQ, with any store (D102, D305). The package uses `RabbitMQ.Client` directly, with no Wolverine.

Starting point: the commits of phase 39.

1. Check first, before the code:
   - The version of `RabbitMQ.Client` that `WolverineFx.RabbitMQ` 6.61.0 uses, so a host with both packages keeps one client version.
   - A server-named, exclusive, auto-delete queue of the pinned client is deleted when its connection closes.
2. Add the package `MsgHatch.Dispatch.RabbitMq` (D305):
   - `RabbitMqDispatchSignal : IDispatchSignal` publishes the topic name as the body of a message with no persistence to the fanout exchange of `RabbitMqSignalOptions.ExchangeName`, default `msghatch.dispatch`. `NotifyAsync` does not wait for a reader, and gives an error when the publish fails.
   - Each process declares one exclusive, auto-delete queue with a name from the server, bound to the exchange. Its consumer gives each topic to a local `InProcessDispatchSignal`, and `WaitAsync` waits on it.
   - A lost connection makes `WaitAsync` return an error, so a loop polls, and the signal connects again after a pause.
   - `RabbitMqSignalOptions` in the section `MsgHatch:RabbitMqSignal` holds `Uri` and `ExchangeName`, with a source-generated validator. `UseRabbitMqDispatchSignal()` selects the signal.
3. Add the integration tests: two signals on the RabbitMQ of `appsettings.Local.json`, which git ignores, and the wake of a wait of the second signal after a notify of the first. With no URI, each test is skipped.
4. Describe the phase in the design: the sections 4.2, 4.7, and 4.9, and `AGENTS.md` gets the command of the integration tests.

Tests:
- Unit tests: the options and their validation, and the selection of the signal.
- The dispatch signal contract tests, with the RabbitMQ signal, skipped with no URI.
- Integration tests of item 3, and a wait that returns an error after the connection closes.
- Each new test fails first, or fails with a break of the code that it covers.

Checked facts (2026-11-22), on Windows 11 build 26300 with the .NET SDK 10.0.404:

| Fact | Source |
|---|---|
| `WolverineFx.RabbitMQ` 6.61.0 needs `RabbitMQ.Client` 7.2.2 for `net10.0` and `net9.0`. So `Directory.Packages.props` pins 7.2.2, and the Wolverine integration tests and the new package resolve the same version. The license of 7.2.2 is `Apache-2.0 OR MPL-2.0`, and Apache-2.0 is allowed (section 5.5 of the design). | The nuspec files of both packages in the NuGet cache, and `project.assets.json` of both projects |
| The broker deletes an exclusive queue when the connection that declared it closes or is lost, also at the loss of the TCP connection. Another connection gets `RESOURCE_LOCKED`. A queue with an empty name gets a name from the server, which `QueueDeclareOk.QueueName` of the client 7.2.2 gives. | The RabbitMQ documentation "Queues", section "Exclusive Queues" |
| RabbitMQ does not implement the flag `no-local` of `basic.consume`, so a consumer also gets the messages of its own connection. So the signal skips its own messages by `AppId`. | The RabbitMQ documentation "Specification" |
| The broker deletes an auto-delete exchange when its last binding is removed. It discards a message with `mandatory` false that it cannot route. | The RabbitMQ documentation "Exchanges" and "Publishers" |
| The contract test `NotifyAsync_NoWaitingLoop_ReturnsAtOnceWithNoError` requires a `NotifyAsync` that completes at once, also before the first connect. So the connection publishes in the background. | `DispatchSignalTestsBase` |
| `Assert.SkipUnless` in the constructor of a test class gives a skipped test in xUnit v3. So the contract subclass skips with no URI, and `DispatchSignalTestsBase` does not change. | The run of the integration project with no URI |
| The build of the solution with `--no-incremental` has no errors and no warnings. The 37 unit test projects pass 5,682 tests and skip 1 test: the path limit test of the named pipe signal on Windows. The new `MsgHatch.Dispatch.RabbitMq.Tests.Unit` passes 26 tests, and the other projects pass the same counts as after phase 39. The line coverage of the production code is 98.4 %, 0.4 points less than after phase 39. `MsgHatch.Dispatch.RabbitMq` has 67.3 %, because only the integration tests run the path with a broker. The 10 tests of `MsgHatch.Dispatch.RabbitMq.Tests.Integration` are skipped with no URI. Each count is of the branch after the rebase onto phase 39. | `dotnet build MsgHatch.slnx --no-incremental`, `pwsh ./coverage.ps1`, and a run of each unit test project |
| The 10 integration tests pass against `dev-rabbit`, a local container of the owner with RabbitMQ 4.3.4 of the image `rabbitmq:4.3-management`, with the URI `amqp://localhost:5672` and the default user. With these tests, the line coverage of `MsgHatch.Dispatch.RabbitMq` is 93.5 %, and with the unit tests 95.1 %. `pwsh ./coverage.ps1` measures only the unit test projects, so it shows 67.3 %. The lines with no test are the wait after the dispose, a body with an invalid channel name, and the ends of the publish loop, which ends only with an exception. 6 breaks each failed at least one integration test: the skip of the messages of the other signals (4 tests), each body as the inbox (2), the inbox as the body of each publish (2), no binding of the queue (4), a shutdown that does not end the attempt (2), and no connect after the pause (1). | A run of the integration project with the URI in its `appsettings.Local.json`, the coverage of both test projects of the package, and the break runs |
| The new tests did not pass before the code of their commit, or failed with a break of the code that they cover. Commit 1: 14 of the 24 new unit tests failed against a stub, and 13 breaks failed (the length limit, the colon, and the dot of the exchange name, each of the two schemes, the configuration section, the configuration in code, the default exchange name, the name of the setup method, the one Warning of an outage, the pause, the error of `NotifyAsync`, and the error of `WaitAsync`). The pause test first passed the break of the pause by a race, so it now waits 300 ms of real time before it counts the connects. Commit 2: the 2 new test rows failed before the code. | The break runs |

Checks that the phase did not do:
- A broker with the scheme `amqps`, where the 2 tests of a lost connection are skipped, and a cluster of brokers. The integration tests ran on one broker with `amqp`.
- The SES, Resend, Wolverine, SNS, EF Core, and MongoDB integration tests. The phase changes no store and no client, and the Wolverine tests keep their client version.
- The benchmarks. The signal is not on the path of a send.

Commits:
1. "Add the RabbitMQ dispatch signal"
2. "Require polling with the RabbitMQ dispatch signal"
3. "Describe the RabbitMQ dispatch signal in the design"
4. "Record the commits and the checked facts of phase 40 in the plan"
5. "Record phase 40 as done in README and AGENTS.md"
6. "Record the run of the RabbitMQ integration tests in the plan"

The plan of this phase is in commit 1 of phase 39 (`b5d4694`). Commit 1 holds items 2 and 3 (`5660d20`), commit 2 the poll rule (`8f46fe6`), and commit 3 item 4 (`01f6ee0`). Item 1 is a check, and its results are in the checked facts. The phase adds the projects `MsgHatch.Dispatch.RabbitMq`, `MsgHatch.Dispatch.RabbitMq.Tests.Unit`, and `MsgHatch.Dispatch.RabbitMq.Tests.Integration`, and the package `RabbitMQ.Client` 7.2.2.

The implementation changed the plan in these points:
- `NotifyAsync` returns at once, as the contract requires, and the connection publishes in the background. So `NotifyAsync` does not give the error of a publish. It gives `Errors.Unavailable` in the pause after a failed connect or a lost connection. Many notifications before one publish become one message for each topic.
- RabbitMQ does not implement `no-local`, so each message has the id of its signal instance as `AppId`, and the consumer skips its own messages. D305 records it.
- The exchange is not durable and auto-delete, and the messages are transient. The client has no automatic recovery. The signal connects again itself after a fixed pause of 5 seconds, so `WaitAsync` returns an error for each outage.
- A body other than `inbox`, or `outbox:{Channel}` with a valid channel name, wakes no loop. So a foreign message on the exchange adds no topic.
- After commit 1, the owner asked for commit 2: `UseRabbitMqDispatchSignal` requires `EnablePolling` true for the inbox and the outbox (D190), as the named pipe signal and the MongoDB signal do.
- The signal logs the events 3200, a Warning one time for each outage, and 3201, an Information at the next connect. Section 4.16 of the design gives the signal the new range 3200 to 3299.
- The unit tests of the signal use a TCP listener in the test process that closes each connection. So they test the failed connect, the pause, and the log with no broker. The integration tests of a lost connection use a TCP forwarder to the broker in the test process, and are skipped with the scheme `amqps`.
- The design also gets `RabbitMqSignalOptions` in section 4.4, the error of `NotifyAsync` in section 4.6, the event range in section 4.16, the test projects in section 5.5, and the texts of sections 6 and 7.

Done when: the build has no errors and no warnings, all unit tests pass, the RabbitMQ integration tests pass or are skipped with no URI, and `pwsh ./coverage.ps1` passes.

### Phase 41: Versions and NuGet packages

Goal: each non-test project has one shared SemVer, and each library project is ready to be a NuGet package on nuget.org. A GitHub workflow builds and packs on each push, and publishes the packages on a version tag (D307 to D309).

Starting point: the commits of phases 39 and 40. The owner asked for a SemVer for all non-test projects, a README for each library project with a version history, release notes, and code examples, and the preparation of the NuGet packages. The owner chose one shared version, the first version 0.1.0, examples that the tests compile and run, and a GitHub workflow that also publishes. The owner then split the work into this phase and phase 42, which adds the READMEs.

Checked at plan time (2026-11-22), with a pack of `MsgHatch.Email.AmazonSES` and `MsgHatch.Api.Client` into a temporary folder:
- A pack turns each `ProjectReference` into a package dependency with the version of the pack: `MsgHatch.Email.AmazonSES` 0.1.0 depends on `MsgHatch.Email` 0.1.0 or later, and `MsgHatch.Api.Client` on `MsgHatch.Api.Contracts`. The project files keep their project references.
- The analyzer reference of `MsgHatch.Api.Client` to `MsgHatch.SourceGeneration`, with `ReferenceOutputAssembly="false"`, gives no package dependency.
- The SDK adds Source Link with no package: the nuspec has `repository` with the GitHub URL and the commit.

1. Check first, before the code:
   - What the generator assembly adds to the compilation of a consumer that references only `MsgHatch.Templates`. The assembly holds the placeholder payload generator and also the HTTP client generator.
   - The four admin UI packages hold their static web assets.
   - The current major versions of `actions/checkout`, `actions/setup-dotnet`, `actions/upload-artifact`, and `actions/download-artifact`.
   - Whether nuget.org accepts trusted publishing with the OIDC token of GitHub.
   - `coverage.ps1` and `pack.ps1` run on Linux. The check uses a temporary container of the image `mcr.microsoft.com/dotnet/sdk` of the SDK version, not a dev container.
2. Set one SemVer and the package metadata (D307, D308):
   - `Directory.Build.props` sets `VersionPrefix` `0.1.0` for each project. A build with no suffix gives `0.1.0`. The workflow adds a suffix to a build with no version tag.
   - The 6 example projects get the version, but they are not packages: `IsPackable` stays `false` for each project outside `src/`.
   - Each project under `src/` gets `Description` and `PackageTags` in its project file.
   - `Directory.Build.props` sets for the projects under `src/`: `PackageProjectUrl`, `Copyright`, `IncludeSymbols` with `SymbolPackageFormat` `snupkg`, `EnablePackageValidation`, and `ContinuousIntegrationBuild` when `GITHUB_ACTIONS` is `true`.
   - `MsgHatch.SourceGeneration` sets `IsPackable` `false`, because `MsgHatch.Templates` already packs the generator into `analyzers/dotnet/cs`.
   - `AnalyzerReleases.Shipped.md` gets the rules `MSGHATCH001` to `MSGHATCH005` under the release 0.1.0. `AnalyzerReleases.Unshipped.md` keeps only its header.
3. Add `pack.ps1` (D308):
   - It packs each packable project in Release into `artifacts/packages`, with an optional version suffix.
   - It fails when a package has another version, no license expression, no description, no repository commit, no symbol package, or a dependency on `MsgHatch.SourceGeneration`.
   - It fails when the count of packages is not the count of packable projects, and when a package comes from a project outside `src/`.
   - It checks the generator in `MsgHatch.Templates`, and the static web assets of the admin UI packages.
4. Add the GitHub workflow `.github/workflows/build.yml` (D309):
   - On each push and pull request: the SDK of `global.json`, `pwsh ./coverage.ps1`, `pwsh ./pack.ps1` with the suffix `ci.<run number>`, and the packages as a build artifact.
   - On a tag `v<version>`: the same steps with no suffix, and a check that the tag is `v` and `VersionPrefix`. Then `dotnet nuget push` sends the packages and the symbol packages to nuget.org with the secret `NUGET_API_KEY`, in the environment `nuget`. `--skip-duplicate` makes a second run safe.
   - The workflow runs no integration test.
5. Describe the phase in the design: a new section 5.6 for the versions and the packages, the sections 5.1 and 5.2, section 6 (the risk of a version that a phase does not raise), and section 7 (the line "The publication of NuGet packages" goes). `AGENTS.md` gets `pwsh ./pack.ps1` and the workflow in the section "Commands".

Tests:
- `pack.ps1` passes. It fails with each of these breaks: one description removed, a `Version` in one project file that differs from `VersionPrefix`, and `IsPackable` `true` in one example project.
- The unit tests of the changed projects stay green.
- Each new test fails first, or fails with a break of the code that it covers.

Checked facts (2026-11-22), on Windows 11 build 26300 with the .NET SDK 10.0.404:

| Fact | Source |
|---|---|
| `MsgHatch.Templates` carries the generator assembly, so the placeholder payload generator runs in each consumer. The assembly also holds the HTTP client generator, whose `RegisterPostInitializationOutput` adds the internal attributes of the namespace `MsgHatch.Http` to each consumer compilation. They are internal, so they do not conflict with another assembly, and a consumer does not use them. | `HttpApiClientGenerator.Initialize` and `PlaceholderPayloadGenerator.Initialize` |
| `MsgHatch.AdminUI`, `MsgHatch.Templates.AdminUI`, and `MsgHatch.Templates.Liquid.AdminUI` hold the bundle of their scoped CSS in `staticwebassets/`, and `MsgHatch.Templates.AdminUI` also holds `highlightedTextArea.js`. `MsgHatch.Subscriptions.AdminUI` has no `wwwroot` folder and no scoped CSS, so its package has no static web assets. So `pack.ps1` checks the assets of each project, not one rule for each admin UI package. | The file lists of the 4 packages, and the source folders |
| The latest major versions are `actions/checkout` v7, `actions/setup-dotnet` v6, `actions/upload-artifact` v7, `actions/download-artifact` v8, and `NuGet/login` v1. `setup-dotnet` with `global-json-file` takes the highest SDK that `rollForward` `latestFeature` allows. | `gh api repos/<action>/releases/latest`, and the README of `actions/setup-dotnet` |
| nuget.org accepts trusted publishing from GitHub Actions: `NuGet/login` exchanges the OIDC token of the job for a temporary API key, with the permission `id-token: write` and the nuget.org user name. Since 2026-08-17, a new nuget.org API key lives at most 30 days. So the workflow uses trusted publishing, and D309 records it. | Microsoft Learn "Trusted publishing", the .NET blog of 2026-08-03 on the API key lifetime, and the README of `NuGet/login` |
| `dotnet nuget push` with `--no-symbols` absent pushes the `.snupkg` of the same folder with each `.nupkg`. | The reference of `dotnet nuget push`, and Microsoft Learn "Creating symbol packages" |
| In a temporary container of the local image `mcr.microsoft.com/dotnet/sdk:10.0` (SDK 10.0.401), 35 of the 37 unit test projects pass. 3 tests fail: `FileSystemTemplateStoreWriterUnitTests.DeleteAsync_FileThatAReaderHoldsOpen_GivesUnavailableAndKeepsTheFile` and `SaveAsync_FileThatAReaderHoldsOpen_GivesUnavailableAndLeavesNoTemporaryFile`, which test a file lock of Windows, and `FileSystemOutboxStoreUnitTests.SetChannelDownAsync_TwoParallelCallsWithTheReasonErrors_AddOneRowWithNoError`. `coverage.ps1` stops at the first failed project. So the job `build` runs on `windows-latest`. | Two runs in the container, with the NuGet cache of the host as a read-only fallback folder |
| `pack.ps1` in the container found no repository commit, because the copy of the source had no `.git` folder. On GitHub, `actions/checkout` gives the `.git` folder. | The run of `pack.ps1` in the container |
| `pack.ps1` passes with 36 packages of version 0.1.0, and with `-VersionSuffix ci.1` with 36 packages of 0.1.0-ci.1. Each break fails: a description removed (`MsgHatch.Email has no description.`), a `Version` 0.2.0 in `MsgHatch.Broker` (its version, and the dependency of `MsgHatch.Broker.AmazonSns`), `IsPackable` `true` in `WolverineExample.Contracts` (no packable project under `src/`), the generator item removed from `MsgHatch.Templates`, and a `wwwroot` file and a scoped CSS file in `MsgHatch.Email`, which a package of `Microsoft.NET.Sdk` does not pack. A `Content Remove` of `highlightedTextArea.js` does not remove the static web asset, so it is no break. | The runs of `pack.ps1` |
| The tag check of the workflow passes with `v0.1.0`, and fails with exit code 1 with `v0.2.0`. | A local run of the step in PowerShell 7 |
| The build of the solution has no errors and no warnings. The 37 unit test projects pass 5,682 tests and skip 1 test, the same counts as after phase 40. The line coverage of the production code is 98.4 %. | `dotnet build`, `pwsh ./coverage.ps1` |

Checks that the phase did not do:
- The first run of the workflow on GitHub, which the push of the phase starts. The owner checks the run in the tab Actions of the repository.
- A publish to nuget.org. The owner creates the trusted publishing policy and the secret `NUGET_USER` first (section 5.6 of the design).

Commits:
1. "Plan phases 41 and 42: the NuGet packages and the package READMEs"
2. "Set one SemVer for all projects and the package metadata"
3. "Add the pack script that checks each package"
4. "Add the GitHub workflow that builds, packs, and publishes"
5. "Describe the versions and the packages in the design"
6. "Record the commits and the checked facts of phase 41 in the plan"
7. "Record phase 41 as done in README and AGENTS.md"

Commit 1 holds the plans of phases 41 and 42, the decisions D307 to D311, and the new rules of `AGENTS.md`.

The plan of this phase is in commit 1 (`f7d4dc0`). Commit 2 holds item 2 (`7d025a4`), commit 3 item 3 (`601f850`), commit 4 item 4 (`666f1cf`), and commit 5 item 5 (`2795da3`). Item 1 is a check, and its results are in the checked facts.

The implementation changed the plan in these points:
- The generator has the 10 rules `MSGHATCH001` to `MSGHATCH010`, not 5, and all 10 move to the shipped release 0.1.0.
- D309 uses trusted publishing, not the secret `NUGET_API_KEY`, because a nuget.org API key now lives at most 30 days. The job `publish` gets a temporary key from `NuGet/login`, with the permission `id-token: write` and the secret `NUGET_USER`, the nuget.org user name.
- The job `build` runs on `windows-latest`, because 3 unit tests fail on Linux. Its steps use PowerShell 7 and the expressions `github.run_number` and `github.ref_name`, so they run on each runner. The job `publish` runs on `ubuntu-latest`.
- The workflow runs on a push of a branch and on a tag `v*` only, so a tag `phase-N` starts no run.
- `pack.ps1` checks the static web assets of each package against the `wwwroot` folder and the scoped CSS of its project, because `MsgHatch.Subscriptions.AdminUI` has none. It also checks the tags, and the version of each dependency on a MsgHatch package.
- `MsgHatch.SourceGeneration` also gets a description and tags, because each project under `src/` has them. The copyright year is 2025, the year of the first commit.


Done when: the build has no errors and no warnings, all unit tests pass, `pwsh ./coverage.ps1` passes, and `pwsh ./pack.ps1` passes with 36 packages.

### Phase 42: Package READMEs

Goal: each library project has its own `README.md`, with the DI setup, the options, examples that the tests compile and run, and a version history with release notes. Each package carries its README (D310, D311).

Starting point: the commits of phase 41. Phase 42 changes only the READMEs, the tests, and the package content, so the version stays 0.1.0 (the rule of `AGENTS.md`). The first publish comes after this phase.

1. Check first, before the code:
   - How nuget.org shows a package README: a table, a fenced code block, an HTML comment, and a relative link. A relative link does not work there, so the READMEs use absolute GitHub links.
2. Add the README check of the test projects (D311):
   - The test source `tests/Shared/ReadmeCheck.cs` is linked into each unit and integration test project by `Directory.Build.props`. It reads `src/<Project>/README.md`.
   - Each C# block of a README follows a marker `<!-- snippet: name -->`. It equals the region `#region readme:name` of a test file, with the indent removed.
   - Each JSON block follows the same marker. It equals the test data file `Readme/name.json`, which a test binds.
   - A marker with no source fails. A source with no marker fails.
   - The options table of a README names each public settable property of its options class, and no other name.
3. Add a README to each library project (D310): 37 projects in 7 commits by area. Each README has these parts, in this order:
   - The name, and what the package does: the interfaces that it implements, and the packages that it needs.
   - The install command `dotnet add package <Id>`.
   - The DI setup: the `Use<Technology>` call or the setup method of the package, as a compiled example.
   - The options: the section of the configuration, a table with each option, its type, its default, and its rule, and a JSON example of `appsettings.json`. A package with no options says so.
   - Examples of the main uses, for example the registration of a message type for a channel, a store for each tenant, or a call of an API client.
   - The version history: one entry for each version that changed the package, newest first. An entry has the version, the date of the commit that sets the version, and the release notes. A breaking change starts with "Breaking:". The first entry is 0.1.0, with the features of the package.
   - `MsgHatch.SourceGeneration` gets a README that is not packed. It says which packages carry its generators.
   - The test project of each package gets `ReadmeUnitTests`. The 4 EF Core and MongoDB store projects have only integration test projects, so they get `ReadmeIntegTests`, which need no database. Each example runs as a test, and the README check runs.
4. Pack the README into each package (D310):
   - `Directory.Build.props` sets `PackageReadmeFile` `README.md` for the projects under `src/`, with the file in the package root.
   - `pack.ps1` also fails for a package with no README.
5. Describe the phase in the design: section 5.6 (the package README), and section 5.5 (the README tests). The root `README.md` links each package README.

Tests:
- The README check: a block equal to its region passes. A changed line, a marker with no region, a region with no marker, a missing option, and an unknown option each fail.
- The examples of each README run as tests: the DI setup resolves the main service of the package, and the JSON example binds and passes the options validation.
- `pack.ps1` passes, and fails when one README is removed.
- Each new test fails first, or fails with a break of the code that it covers.

Checked facts (2026-11-23), on Windows 11 build 26300 with the .NET SDK 10.0.404:

| Fact | Source |
|---|---|
| nuget.org renders a package README as CommonMark through Markdig. Its list of features has tables, links, code spans with syntax highlighting, and images from a list of allowed domains, but no HTML. An image with a relative path is not rendered. So the READMEs use absolute GitHub links and no image, and the markers are link reference definitions. | Microsoft Learn "Package readme on NuGet.org", updated 2026-07-07 |
| A pack with `PackageReadmeFile` puts `README.md` into the root of the package and the element `readme` into the nuspec, and gives no message "missing a readme" any more. A removed README stops the pack with `NU5019`, and an empty `PackageReadmeFile` in one project fails `pack.ps1` with `MsgHatch.Email has no README.` | The runs of `pack.ps1`, and the file list and nuspec of `MsgHatch.Data.Sqlite` 0.1.0 |
| The 11 tests of `ReadmeCheckUnitTests` pass. With the comparison of a snippet off, 1 test fails, and with no get-only collections in the options, 4 tests fail. | The runs of the tests of `MsgHatch.Domain.Tests.Unit` |
| The 37 README test classes pass 123 tests: 108 in unit test projects and 15 in the 4 integration test projects. A break run changed the first code line of each README, or added a C# block with no marker where the README has none, and removed the last row of the first options table: each of the 37 classes failed its snippet test, and each of the 17 classes with an options table failed its options test. | The runs of each test project, with a filter on the README test classes |
| A second `services.AddMsgHatch()` after `AddMsgHatch(o => o.DefaultLanguage = "hu")` gives the configuration value, not the value of the code. So the domain README test calls its extra `AddMsgHatch().UseStoredRegistry()` before the snippet. The cause is not checked. | The run of the domain README test |
| The build of the solution with `--no-incremental` has no errors and no warnings. The 37 unit test projects pass 5,801 tests and skip 1 test: 119 more than after phase 41, the 108 README tests and the 11 tests of the check. The line coverage of the production code is 98.4 %. | `dotnet build`, `pwsh ./coverage.ps1` |
| `pack.ps1` passes with 36 packages of version 0.1.0, each with its README. | The run of `pack.ps1` |

Checks that the phase did not do:
- The view of a README on nuget.org. The owner sees it in the preview of an upload, or after the first publish.
- The integration tests other than the README test classes. The phase changes no code of a store or a client.

Commits:
1. "Add the README check of the test projects"
2. "Add the READMEs of the core packages": `MsgHatch.Domain`, `MsgHatch.SourceGeneration`, `MsgHatch.AspNetCore`
3. "Add the READMEs of the template packages": `MsgHatch.Templates`, `MsgHatch.Templates.Liquid`, `MsgHatch.Templates.AspNetCore`
4. "Add the READMEs of the channel packages": `MsgHatch.Email`, `MsgHatch.Email.AmazonSES`, `MsgHatch.Email.Resend`, `MsgHatch.SignalR`, `MsgHatch.Broker`, `MsgHatch.Broker.Wolverine`, `MsgHatch.Broker.AmazonSns`
5. "Add the READMEs of the store and signal packages": `MsgHatch.Data.FileSystem`, `MsgHatch.Data.Sqlite`, `MsgHatch.Data.EFCore`, `MsgHatch.Data.MongoDb`, `MsgHatch.Dispatch.NamedPipes`, `MsgHatch.Dispatch.RabbitMq`
6. "Add the READMEs of the subscription packages": `MsgHatch.Subscriptions`, its 4 store packages, its 2 cache packages, and `MsgHatch.Subscriptions.AspNetCore`
7. "Add the READMEs of the API packages": the 3 contracts packages and the 3 client packages
8. "Add the READMEs of the admin UI packages": the 4 admin UI packages
9. "Pack the README into each package"
10. "Describe the package READMEs in the design"
11. "Record the commits and the checked facts of phase 42 in the plan"
12. "Record phase 42 as done in README and AGENTS.md"

The plan of this phase is in commit 1 of phase 41 (`f7d4dc0`). Commit 1 holds item 2 (`7802b67`), commits 2 to 8 hold item 3 (`2e6b900` to `2b63381`), commit 9 item 4 (`cad6d96`), and commit 10 item 5 (`71ea64d`). Item 1 is a check, and its results are in the checked facts.

The implementation changed the plan in these points:
- The markers are link reference definitions, `[//]: # (snippet: name)` and `[//]: # (options: Type)`, not HTML comments. nuget.org lists no HTML in its Markdown features, so an HTML comment can show as text there, and a link reference definition renders as nothing in each CommonMark renderer.
- A non-C# block equals the file `Readme/name.<language>`, so a JSON block equals `Readme/name.json`, as the plan says, and a Razor block can equal `Readme/name.razor`. No README has a Razor block yet.
- The options table names each property that the configuration binder can fill: a property with a public setter, or a get-only collection, for example `AmazonSnsOptions.Topics`.
- `ReadmeCheck.ReadSettings` turns a JSON file into the configuration keys of `TestHost.CreateServices`, so the test projects need no JSON configuration package.
- The READMEs of `MsgHatch.Email`, `MsgHatch.Broker`, `MsgHatch.SignalR`, `MsgHatch.Domain`, and `MsgHatch.Subscriptions` describe the registration of a message type in text, and link to `ExampleSetup.cs` of the example app. A compiled registration needs an event, a payload, a loader, a router, and a JSON context in each test project.
- The JSON example of `MsgHatch.Domain` uses the sections that the core binds with no message type: `Dispatch:Outbox`, `Retry:Inbox`, and `Registry`. The sections of a channel bind only for a message type of that channel.
- The admin UI READMEs show only the setup of the pages, because their test projects do not reference the client packages. The text names the clients.
- The test of `UseSignalR` builds the services with no validation, as a host does before it starts, because the SignalR server services need the services of a host.

Before the first publish, the owner creates the trusted publishing policy on nuget.org and the secret `NUGET_USER` (section 5.6 of the design), and then pushes the tag `v0.1.0`.

Done when: the build has no errors and no warnings, all unit tests pass, `pwsh ./coverage.ps1` passes, `pwsh ./pack.ps1` passes with 36 packages that each carry a README, and each of the 37 READMEs passes its README tests.

### Phase 43: Atomic state rows of the file store, and the build on Linux

Goal: of two parallel inserts of one version of a channel state row, only the first succeeds on each OS, and the job `build` of the GitHub workflow runs on `ubuntu-latest` (D312, D313).

Starting point: the commits of phase 42 and `ebcbc47`. In a temporary Linux container, 3 unit tests fail (phase 41). The owner asked for the reasons:
- The test of two parallel downs of the file store shows a race of the product. On Unix, .NET does `File.Move` with no overwrite as `lstat` of the target and then `rename`, and `rename` replaces a file that another caller created between the two calls. So both callers get true, and the second row replaces the first. In a probe with two threads at a barrier, both moves succeeded in 1,878 of 2,000 rounds. A read-back of the file after the move does not close the race.
- The 2 tests of the template store check a file lock that only Windows has.

The owner chose the fix with `link()`, and the version 0.1.1.

1. Check first, before the code:
   - The new contract test fails on Linux with the current `MessageFiles.TryCreate`. The check uses a temporary container of the image `mcr.microsoft.com/dotnet/sdk:10.0`, not a dev container.
   - A `LibraryImport` of `link` from `libc` builds with no warning.
2. Make `MessageFiles.TryCreate` atomic on each OS (D312):
   - On Linux and macOS, it writes the temporary file, and calls `link(temporary, path)`. 0 gives true, `EEXIST` gives false, and each other error throws an `IOException`, which `FileFailures.Run` maps as before. It deletes the temporary file in each case.
   - On Windows, it keeps `File.Move` with no overwrite, which is atomic there.
   - The internal class `UnixFiles` holds the `LibraryImport`. `MsgHatch.Data.FileSystem.csproj` sets `AllowUnsafeBlocks`.
   - There is no fallback to the move on Unix. A file system with no hard links gives `Errors.Unavailable`, not a silent race.
   - `VersionPrefix` becomes `0.1.1`, and the README of `MsgHatch.Data.FileSystem` gets the entry 0.1.1.
3. Skip the 2 file lock tests of `FileSystemTemplateStoreWriterUnitTests` unless the OS is Windows, with `Assert.SkipUnless`, as the ACL test does (D313). On Linux, a save or a delete over a file that a reader holds open succeeds, and the reader keeps the old content, so the store code does not change.
4. The job `build` of `.github/workflows/build.yml` runs on `ubuntu-latest` (D313). Its steps do not change.
5. Describe the phase in the design: section 5.6 (the runner), and section 2.4 of `docs/stores.md` (the hard link, and the file system that it needs).

Tests:
- The contract test `SetChannelDownAsync_PauseAndErrorsInParallelOnAnUpChannel_KeepThePause` runs 50 rounds. Each round runs a pause and an errors down of one up channel in parallel. Then the latest row is a down with the reason Operator, and a resume makes the channel up for the next round. It runs for each of the 6 outbox stores.
- The new test fails on Linux before item 2, and passes after it. A break run with the old move makes it fail again. On Windows, it passes before and after item 2, because the move is atomic there.
- On Linux in the container, `pwsh ./coverage.ps1` passes from start to end, and `pwsh ./pack.ps1` passes with 36 packages of 0.1.1.
- On Windows, the build has no warnings, and `coverage.ps1` and `pack.ps1` pass.
- The integration tests of the EF Core and MongoDB outbox stores pass on the dev containers.

Checked facts (2026-11-23). The Linux runs used a temporary container of the local image `mcr.microsoft.com/dotnet/sdk:10.0` (SDK 10.0.401) on a clone of the commits, with the NuGet cache of the host as a read-only fallback folder. The Windows runs used Windows 11 build 26300 with the .NET SDK 10.0.404:

| Fact | Source |
|---|---|
| .NET 10 has no API for a hard link: `System.IO.File` has only `CreateSymbolicLink` and `ResolveLinkTarget`. A `LibraryImport` of `link` with `StringMarshalling.Utf8` builds with no warning when the project sets `AllowUnsafeBlocks`. | The methods of `System.IO.File` on the runtime 10.0.15, and the build |
| A read-back of the file after the move does not close the race. In a probe with two threads at a barrier, both callers got true in 1,606 of 2,000 rounds with a read-back at once. With a wait of 50 ms before the read-back, no round of 200 failed, but two writers of equal rows cannot see the difference. | A probe in the container |
| Before the fix, the new contract test failed on Linux in each of 3 runs, with 4, 5, and 4 lost pauses in 50 rounds. The test of two parallel downs failed in 2 of the 3 runs. On Windows, the new test passed. | Runs of the file store tests |
| With no barrier, a break run with the old move failed in only 2 of 3 runs. With the barrier, the break run failed in each of 3 runs, with 6, 9, and 6 lost pauses. | Break runs on Linux |
| With the fix, the 4 parallel state tests of the file store pass in each of 10 runs on Linux, and the 345 tests of `MsgHatch.Data.FileSystem.Tests.Unit` pass in each of 3 runs. | Runs on Linux |
| The new test passes for the in-memory store, the tenant store, SQLite in 8 seconds, EF Core in 4 seconds, and MongoDB in 1 second. `MsgHatch.Data.EFCore.Tests.Integration` passes 343 tests on dev-mssql, and `MsgHatch.Data.MongoDb.Tests.Integration` passes 386 tests on dev-mongo. | The runs of the 4 test projects on Windows |
| On Linux at `15fc945`, the build of the solution has no errors and no warnings. `pwsh ./coverage.ps1` passes from start to end with the report step: the 37 unit test projects pass 5,803 tests and skip 3, the 2 file lock tests and the ACL test of `FileSystemTemplateStoreWriterUnitTests`. The line coverage is 98.4 %. `pwsh ./pack.ps1` passes with 36 packages of version 0.1.1. | The run in the container |
| On Windows, the build of the solution with `--no-incremental` has no errors and no warnings. The 37 unit test projects pass 5,805 tests and skip 1, the path limit test of the named pipe signal. This is 4 more than after phase 42: the new test for 4 stores. The line coverage is 98.4 %. `pwsh ./pack.ps1` passes with 36 packages of version 0.1.1. | `dotnet build`, `pwsh ./coverage.ps1`, `pwsh ./pack.ps1` |

Checks that the phase did not do:
- A run on macOS.
- The error path of `link()` other than `EEXIST`, because no test has a file system with no hard links.
- The first run of the workflow on `ubuntu-latest`, which the push of the phase starts.

Commits:
1. "Plan phase 43: atomic state rows of the file store, and the build on Linux"
2. "Create the state rows of the file store with a hard link on Unix"
3. "Skip the file lock tests of the template store unless Windows"
4. "Run the build job on Linux"
5. "Describe the atomic state rows and the Linux build in the design"
6. "Record the commits and the checked facts of phase 43 in the plan"
7. "Record phase 43 as done in README and AGENTS.md"

Commit 1 holds the plan of this phase and the decisions D312 and D313.

The plan of this phase is in commit 1 (`6e8395b`). Commit 2 holds item 2 (`0aaac67`), commit 3 item 3 (`224c911`), commit 4 item 4 (`2e6eb21`), and commit 5 item 5 (`15fc945`). Item 1 is a check, and its results are in the checked facts.

The implementation changed the plan in these points:
- The new test starts the two calls of each round at a `Barrier`, because with no barrier the old move failed in only 2 of 3 runs. It reads the current state with `GetDownChannelsAsync`, so a round does not read the whole history.
- `TryCreate` deletes the temporary file in a `finally` block. So the move on Windows also deletes it when it fails with an error other than an existing file.
- The README of `MsgHatch.Data.FileSystem` and section 6 of the design also name the hard links as a need of the file stores on Linux and macOS.

Done when: the build has no errors and no warnings, all unit tests pass on Windows and on Linux, `pwsh ./coverage.ps1` passes on both, `pwsh ./pack.ps1` passes with 36 packages of 0.1.1, and the integration tests of the EF Core and MongoDB outbox stores pass.

### Phase 44: One bind of the core options in AddMsgHatch

Goal: a value from code of `AddMsgHatch` wins over the configuration in each order of the calls (D314).

Starting point: the commits of phase 43. Phase 42 found that a second `services.AddMsgHatch()` after `AddMsgHatch(o => o.DefaultLanguage = "hu")` gives the configuration value. The owner asked for the cause:
- Each call of `AddMsgHatch` runs `BindConfiguration("MsgHatch")` again, and the options run their configure steps in the order of registration. So the bind of the second call overwrites the value from code of the first call.
- The binder sets only the keys that the configuration has, so the effect needs the same key in the configuration.
- A probe gave `de` for the configuration value `de`, `hu` from code, and then a call with no action. A call with no action and then `hu` from code gave `hu`.

1. `AddMsgHatch` registers the core options, their binds, and their validators only on its first call, when it creates the setup, as `AddChannelOptions` does for a channel (D314). Each call applies its action from code.
2. `VersionPrefix` becomes `0.1.2`, and the README of `MsgHatch.Domain` gets the entry 0.1.2.
3. Describe the second call in the design: the row of `AddMsgHatch` in section 4.7.

Tests:
- `AddMsgHatch_PlainCallAfterACallWithACodeValue_KeepsTheCodeValue`: the configuration has `MsgHatch:DefaultLanguage` `de`, the first call sets `hu` in code, and a second call has no action. The options have `hu`. The test fails first.
- The unit tests stay green, `pwsh ./coverage.ps1` passes, and `pwsh ./pack.ps1` passes with 36 packages of 0.1.2.

Checked facts (2026-11-23), on Windows 11 build 26300 with the .NET SDK 10.0.404:

| Fact | Source |
|---|---|
| Before the fix, a second call with no action gave `de`, the configuration value, after `hu` from code. The options had 2 change token sources of `MsgHatchOptions`, one for each bind. A call with no action and then `hu` from code gave `hu`. With no `DefaultLanguage` in the configuration, both orders gave `hu`. | A probe in a scratch folder that references `MsgHatch.Domain` |
| The new test failed before the fix with Expected `"hu"` and Actual `"de"`, and passes after it. | The runs of the test |
| `MsgHatch.Domain.Tests.Unit` passes 1,705 tests, and its 14 README tests pass. | `dotnet test` |
| The build of the solution with `--no-incremental` has no errors and no warnings. The 37 unit test projects pass 5,806 tests and skip 1, the path limit test of the named pipe signal: 1 more than after phase 43. The line coverage is 98.4 %. `pwsh ./pack.ps1` passes with 36 packages of version 0.1.2. | `dotnet build`, `pwsh ./coverage.ps1`, `pwsh ./pack.ps1` |

Checks that the phase did not do:
- A run on Linux before the push. The workflow run of the push builds and tests on Linux.

Commits:
1. "Plan phase 44: one bind of the core options in AddMsgHatch"
2. "Bind the core options one time in AddMsgHatch"
3. "Describe the second call of AddMsgHatch in the design"
4. "Record the commits and the checked facts of phase 44 in the plan"
5. "Record phase 44 as done in README and AGENTS.md"

Commit 1 holds the plan of this phase and the decision D314.

The plan of this phase is in commit 1 (`d453470`). Commit 2 holds items 1 and 2 (`94ec5f9`), and commit 3 item 3 (`8ca57e9`).

The implementation changed the plan in these points:
- The README of `MsgHatch.Domain` also says in its setup text that a value from code wins when the host calls `AddMsgHatch` more than one time.
- The domain README test keeps its extra `AddMsgHatch().UseStoredRegistry()` before the snippet, which phase 42 added because of the order effect. The owner did not ask to remove it.

Done when: the build has no errors and no warnings, all unit tests pass, `pwsh ./coverage.ps1` passes, and `pwsh ./pack.ps1` passes with 36 packages of 0.1.2.

### Phase 45: Tenant-wide SignalR messages

Goal: a message can go through SignalR to all connected users of one tenant (D315).

Starting point: the commits of phase 44. The owner asked whether the SignalR package supports messages for a tenant, a company, and a user in a multi-tenant UI. It supports a user (`Clients.User("{TenantId}:{UserId}")`) and a company (the group `msghatch:{TenantId}:company:{CompanyId}`). `DeliveryTargetKind` has only `User` and `Company`, and `SignalRTransport.ClientsOf` gives `Errors.ValidationFailed` for each other kind. Only `SignalRTransport`, `DeterministicId`, and the subscription router use the kinds. The owner wants no tenant level in the subscriptions.

1. Add `DeliveryTargetKind.Tenant` to `MsgHatch.Domain`. Its target id is empty, and the tenant comes from the message. `DeterministicId` includes the new kind in its input, so a tenant target gets its own id.
2. SignalR (D315):
   - `SignalRGroups.ForTenant(TenantId?)` gives `msghatch:{TenantId}:tenant`, or `msghatch:all` with no tenant. The tenant id gets the same escaping as in `ForCompany`.
   - `SignalRTransport` sends a tenant target to that group.
   - The README of `MsgHatch.SignalR` says that the hub adds each connection to the group of its tenant, and shows `SignalRGroups.ForTenant`.
3. The subscription router does not change. A router of the host can return a tenant target.
4. The contracts: the delivery query filter and the routing verification response already carry `DeliveryTargetKind`. Their mapping tests get a row with `Tenant`.
5. The example app: its hub adds each connection to the group of its tenant.
6. `VersionPrefix` becomes `0.2.0`, because a new feature before 1.0.0 raises the minor. The READMEs of `MsgHatch.Domain` and `MsgHatch.SignalR` get the entry 0.2.0.
7. Describe the phase in the design: the SignalR section, and the table of `DeliveryTargetKind`.

Tests:
- `SignalRGroups.ForTenant` with a tenant and with no tenant, and with `%` and `:` in the tenant id.
- The transport sends a tenant target to the tenant group, and a user in the group of another tenant gets nothing.
- `DeterministicId` gives different ids for a tenant target and a user target with the same message.
- The contract mappings accept `Tenant`.
- Each new test fails first.

Checked facts (2026-11-23), on Windows 11 build 26300 with the .NET SDK 10.0.404:

| Fact | Source |
|---|---|
| Before the change, `DeterministicId` wrote the byte `c` for each kind other than `User`, so a tenant target got the id of a company target with the same id. The new test failed with this, and passes with the byte `a`. The 2 golden tests keep their fixed values. | The runs of `DeterministicIdUnitTests` |
| Before the change, the HTTP endpoints and the outbox page refused a target with an empty id. The HTTP client sends an empty `targetId` as `targetId=`. The new tests of the client, the endpoints, and the page failed before the change, and pass after it. | The runs of the tests, `HttpApiSource.AppendQuery` |
| The planned group `msghatch:{TenantId}:tenant` of the tenant `company` is `msghatch:company:tenant`. That is also the group of the company `tenant` with no tenant. The new test of this conflict fails with the planned name, and passes with `msghatch:tenant:{TenantId}`. | A break run of `SignalRGroupsUnitTests` |
| A break of each new rule makes its test fail: a null id of a tenant target in the endpoint parser, the id check of a tenant target in the transport, and the tenant line of the hub snippet of the README. | Break runs |
| The stores need no change. SQLite, EF Core, MongoDB, and the files keep the target kind as text with no check of the values. Only EF Core limits it, to 32 characters. | `SqliteSchema`, `StoreModel.IsEnum`, `MongoDbOutboxStore`, `MsgHatchJson` |
| SignalR creates a hub in a DI scope, and the directory of the Wolverine example is a scoped service. So the hub test gives the hub the services of a scope. | The first run of `NotificationHubUnitTests` |
| The build of the solution has no errors and no warnings. The 37 unit test projects pass 5,825 tests and skip 1, the path limit test of the named pipe signal: 19 more than after phase 44. The line coverage is 98.3 %. `pwsh ./pack.ps1` passes with 36 packages of version 0.2.0. | `dotnet build`, `pwsh ./coverage.ps1`, `pwsh ./pack.ps1` |

Checks that the phase did not do:
- A run on Linux before the push. The workflow run of the push builds and tests on Linux.
- A run of the Wolverine example with a SignalR client. The hub test uses substitutes of the hub context and of the groups.

Commits:
1. "Plan phase 45: tenant-wide SignalR messages"
2. "Add the tenant delivery target"
3. "Accept a tenant target in the outbox filters"
4. "Send a tenant target to the tenant group of SignalR"
5. "Join the tenant group in the hub of the Wolverine example"
6. "Describe the tenant target in the design"
7. "Record the commits and the checked facts of phase 45 in the plan"
8. "Record phase 45 as done in README and AGENTS.md"

Commit 1 holds the plan of this phase and the decision D315.

The plan of this phase is in commit 1 (`7c34534`). Commit 2 holds item 1 and the version of item 6 (`2adc5af`), commit 3 item 4 (`3062a55`), commit 4 item 2 (`eab6b7a`), commit 5 item 5 (`50565f5`), and commit 6 item 7 (`50c2452`).

The implementation changed the plan in these points:
- The tenant group is `msghatch:tenant:{TenantId}`, not `msghatch:{TenantId}:tenant`, because the planned name of the tenant `company` is the group of the company `tenant` with no tenant. D315 names the planned group as a rejected alternative.
- The plan said that the contracts already carry a tenant target. The bodies do, but the HTTP endpoints and the outbox page refused a target with an empty id. Commit 3 makes them accept a tenant target with no id or an empty one, and refuse a tenant target with an id. So the READMEs of `MsgHatch.AspNetCore` and `MsgHatch.AdminUI` also get the entry 0.2.0.
- A test of `DeliveryQueriesTestsBase` sends a tenant target through the HTTP client, the endpoint, and the mapping of the response. The routing verification response does not change, because the subscription router never gives a tenant target.
- The SignalR transport gives `Errors.ValidationFailed` for a tenant target with an id.
- The hub of `ExampleApp` has no tenant and joins no group. The hub of `WolverineExample.Consumer` knows the tenant of each connection, so it joins the tenant group, and a new test checks its groups.
- The test of `DeterministicId` compares a tenant target with a user target and with a company target. A comparison with only a user target passes also before the change.
- The README of `MsgHatch.SignalR` shows a hub that joins the groups of its tenant and of its company, as a snippet that a test runs.

Done when: the build has no errors and no warnings, all unit tests pass, `pwsh ./coverage.ps1` passes, and `pwsh ./pack.ps1` passes with 36 packages of 0.2.0.

### Phase 46: SignalR demo dashboard for tenants, companies, and users

Goal: an example app that shows on one screen how a web page finds its tenant, its company, and its user, which SignalR groups it joins at each step, and which open session gets each published notification (D316). The phase changes no library project, so the version stays 0.2.0.

Starting point: the commits of phase 45. The SignalR transport sends to a user, to the group of a company, and to the group of a tenant. The hub of `WolverineExample.Consumer` and its `TenantUserIdProvider` show a hub that joins the groups of its tenant and its company. The owner described the flow of a page:
1. The page maps its domain to a tenant, to a company, or to both. A tenant can be a hosting region or a logical split, for example `testing`. When neither is clear, the page asks the user to choose a tenant.
2. With a tenant, the page subscribes to the notifications of the tenant. A tenant that maps one-to-one to a company also gives the company, so the page also subscribes to the notifications of the company.
3. The login identifies the company when the tenant did not. A user with accounts in more than one company of the tenant chooses one account.
4. After the login, the page subscribes to the notifications of the company and of the user.

In production, the same flow supports a subdomain for each company, a subdomain for each tenant, or a landing page with drop-downs before the login page.

1. Add the project `examples/SignalRDemo`: an ASP.NET Core app with Razor pages and plain JavaScript, with the `@microsoft/signalr` client in `wwwroot/lib`, no CDN, and no build step for the front end. It uses the in-memory stores of MsgHatch and the SignalR channel, and needs no database, no broker, and no network. It listens only on `http://localhost:5091`. It is not packable.
2. The demo data in `demo-data.json`, read through a typed options class:
   - The tenants `eu`, `us`, and `testing`.
   - Companies: at least one that maps one-to-one to its tenant, and at least two that share a tenant.
   - Users: at least one user with accounts in two companies of one tenant, and one user in another tenant.
   - The domain map: `{company}.localhost` gives a company and its tenant, `{tenant}.localhost` gives only a tenant, and `localhost` gives nothing.
3. The resolution and the hub:
   - `DomainResolver` maps a host to a tenant, a company, both, or nothing.
   - The anonymous connection of a page with a tenant joins the group of the tenant, and also the group of the company of a one-to-one tenant. The hub method `JoinTenant(tenant)` checks the tenant against the demo data.
   - The demo login with no password: `POST /demo/login` takes a user and an account, and gives a token of the app with the tenant, the company, and the user. A user with more than one account in the tenant must choose one account.
   - With a token, the connection joins the groups of its tenant and its company, and `IUserIdProvider` gives `{TenantId}:{UserId}`.
4. The dashboard page `/`:
   - A grid of session panes. "Add session" adds a pane. Each pane has a host field, for example `acme.localhost`, and runs the flow of the page: domain, tenant choice, login, and account choice. Each pane has its own `HubConnection`, and keeps its token in memory, so two panes can be two users of one tenant or of two tenants.
   - Each pane shows its tenant, company, user, connection state, its groups, and its log of received notifications, with the time, the target level, and the payload. A pane flashes when it gets a notification.
   - The publish panel: the target level (tenant, company, or user), the tenant, the company, or the user, the payload text, a Send button, and an automatic producer that sends a random notification at an interval. A notification goes through a router of the host and the real outbox, not directly through the hub.
   - The sent log shows each notification with its expected panes and the panes that got it. The dashboard computes the expected panes from the state of each pane, and marks a miss or an extra hit in red.
   - The page `/session?host=...` shows one pane alone, for a demo in real browser tabs.
5. The code panel: it shows the JavaScript of each step (connect, join a tenant, log in, and connect again with the token) and the C# of the hub, the `IUserIdProvider`, and the router. Each block comes from a region marker in a source file, so it equals the code that runs.
6. `examples/SignalRDemo/README.md`: the start command, the flow, the meaning of each pane, and the production layouts: a subdomain for each company, a subdomain for each tenant, or a landing page with drop-downs before the login page.
7. Describe the phase in the design: D316, the project in sections 4.2 and 5.1, and the root `README.md` with a link to the demo.

Tests (`tests/SignalRDemo.Tests.Unit`):
- `DomainResolver`: a company host, a tenant host, `localhost`, and an unknown host.
- The demo login: a user with one account in the tenant gets a token, a user with two accounts and no choice gets a validation error, and an account of another tenant is refused.
- The hub on a test server with SignalR clients of two tenants: a tenant notification reaches only the clients of its tenant, a company notification only its company, and a user notification only its user, also when the same user id is in two tenants.
- The computation of the expected panes for a tenant, a company, and a user target.
- Each code block of the code panel equals its source region.
- Each new test fails first.

Check for the owner: open the dashboard, add four panes (`localhost`, `eu.localhost`, a host of a one-to-one company, and a host of a shared tenant), log in two users of different companies and one user with two accounts, and send one notification at each level. Each sent notification shows no miss and no extra hit.

Checked facts (2026-11-23), on Windows 11 build 26300 with the .NET SDK 10.0.404:

| Fact | Source |
|---|---|
| The last version of `@microsoft/signalr` for ASP.NET Core 10 is 10.0.11. The latest version is 11.0.0. The npm metadata of 10.0.11 gives the license MIT and the repository `dotnet/aspnetcore`, and the package has no license file. | The npm registry |
| The file `dist/browser/signalr.min.js` of 10.0.11 has 47,668 bytes. Its SHA-256 is `l+m5fmQqcuWkcJFxR6K/efhsrYKaXGeGrbEGFNJIu5U=`, the same as the hash of jsDelivr, and the committed file has the same hash. The file has no license header. | The jsDelivr API, `openssl dgst` of the download and of `git show` |
| A break of each rule makes its new test fail: the four hosts of the resolver, the three login rules, the user name of `DemoUserIdProvider`, the company of a tenant with one company, the tenant group, the query token, the two rules of the demo data, the four rules of the expected panes, the indent of a code block, and the embedded file of a code block. The demo data test fails with no user id in two tenants. | Break runs |
| The source-generated options validator calls `IValidatableObject.Validate`, so the two rules of the demo data need no other validator. | The break runs of `DemoDataOptionsUnitTests` |
| The hub test finds an extra hit at its assert, not by a timeout: with an anonymous session of `eu` in the group of `acme`, the row of `acme` failed after 1.4 s. A miss fails after the timeout of 10 s. | Break runs of `DemoHubUnitTests` |
| The test server accepts the WebSocket of a SignalR client with the token in the query `access_token`. With no read of the query, the sessions with a token got no `Joined`, and the test failed. | A break run of `DemoTokens` |
| A run in the browser pane of the desktop app: four panes (`localhost` with the choice `eu`, `eu.localhost`, `globex.localhost`, and `acme.localhost`), the logins of `bela`, of `anna` with the account `initech` after `ValidationFailed`, of `dora`, and of `anna` with the account of `acme.localhost`, 8 notifications at the three levels, and 10 notifications of the automatic producer. Each row of the sent log showed OK. `http://acme.localhost:5091/session` passed the host filter, and the login of `cecil` there gave `Rejected`. The browser console showed no script error, only the 400 and the 403 of the two refused logins. | The browser pane, `dotnet run --project examples/SignalRDemo` |
| The owner tested the dashboard before the push of the phase. | The owner |
| The build of the solution with `--artifacts-path` in a scratch folder has no errors and no warnings. The 38 unit test projects pass 5,849 tests and skip 1, the path limit test of the named pipe signal: 24 more than after phase 45. The line coverage is 98.4 %. The coverage counts only the assemblies `MsgHatch.*`, so the demo does not change it. `pwsh ./pack.ps1` passes with 36 packages of version 0.2.0. | `dotnet build`, `pwsh ./coverage.ps1`, `pwsh ./pack.ps1`, `coverage.runsettings` |

Checks that the phase did not do:
- A run on Linux before the push. The workflow run of the push builds and tests on Linux.
- A test of the publish endpoint and of the JavaScript. The run in the browser pane covers them.

Commits:
1. "Plan phase 46: SignalR demo dashboard"
2. "Add the SignalR demo project with its demo data"
3. "Resolve the tenant, the company, and the user in the demo hub"
4. "Add the dashboard with the session panes and the publish panel"
5. "Add the code panel of the SignalR demo"
6. "Describe the SignalR demo in its README and in the design"
7. "Record the commits and the checked facts of phase 46 in the plan"
8. "Record phase 46 as done in README and AGENTS.md"

Commit 1 holds the plan of this phase and the decision D316.

The plan of this phase is in commit 1 (`6489056`). Commit 2 holds item 2 and the project of item 1 (`ac2d52e`), commit 3 item 3 (`b71f6f6`), commit 4 item 4 with the pages and the SignalR client of item 1 (`844d7fd`), commit 5 item 5 (`966a2c4`), and commit 6 items 6 and 7 (`a88e0ed`).

The implementation changed the plan in these points:
- The user name of a connection comes from `SignalRUsers.ForUser`, and the groups from `SignalRGroups.ForTenant` and `SignalRGroups.ForCompany`. So the names get the escape of `%` and `:` of D114, and the demo uses the names that the transport sends to.
- A company id is unique only inside its tenant (D114), but the host `{company}.localhost` names a company with no tenant. So the validation of the demo data refuses a host label that names more than one company, or a tenant and a company.
- The test of the expected panes needs the computation in C#. So the dashboard sends the state of each pane with the publish request, and the server gives the expected panes (`ExpectedPanes`). The automatic producer runs in the page, because only the page knows the state of the panes.
- The panes use the hosts `*.localhost`, and a real tab of `/session` comes from such a host. So `AllowedHosts` is `localhost;*.localhost`, and the app listens only on `http://localhost:5091`.
- A browser sends no header with a WebSocket. So the JWT bearer handler reads the token of the hub from the query `access_token`, as the JavaScript client sends it. The hub test sends the token in the same way.
- The app signs its tokens with a random key that it makes at each start, so it needs no setup and keeps no secret in a file. A token does not survive a restart.
- The login request also carries the tenant of the page. An account is the company of the account. A page with a company from its domain sends that company as the account.
- The tests of a user target need one user id in two tenants, so the demo data has the user `anna` in `eu` and in `us`.
- The hub tells each connection its tenant, company, user, and groups with the client method `Joined`, after the join of a token and after `JoinTenant`. The pane shows them, and the dashboard sends them as the state of the pane.
- The JavaScript client is `signalr.min.js` of `@microsoft/signalr` 10.0.11, the last version for ASP.NET Core 10, under the MIT license. The package has no license file and the file has no license header, so the README of the demo names the package, the version, the source, and the license. The repository gets no source map.
- The code panel reads its blocks from the source files that the app embeds as resources, between `#region` and `#endregion` in C# and `//#region` and `//#endregion` in JavaScript. The test compares each block with the region of the file on the disk.
- Commit 3 holds the message type with its router and payload loader, because the hub test sends each notification through the router and the outbox. Commit 4 holds the publish endpoint.
- The tests also check the demo data of the plan, the two rules of the demo data, and the message `Joined` of each kind of session, because the expected panes come from `Joined`. The login and the resolver tests call their HTTP endpoints.
- A pane can get a notification before the answer of the publish endpoint arrives. So the dashboard keeps such a hit, and adds it to the row of the notification.

Done when: the build has no errors and no warnings, all unit tests pass, `pwsh ./coverage.ps1` passes, `pwsh ./pack.ps1` passes with 36 packages of 0.2.0, and the owner check of the dashboard shows no miss and no extra hit.

### Phase 47: Package versions that float within their major

Goal: each package of `Directory.Packages.props` takes the latest stable version of its major at each restore, so the repository gets each new minor and patch with no commit (D317).

Starting point: the commits of phase 46. Each package has an exact version. For example, EF Core is 10.0.14, but 10.0.15 exists, and `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.4 needs EF Core 10.0.15. NuGet refuses a floating version in central package management (NU1011), unless `CentralPackageFloatingVersionsEnabled` is true. With the floating version `10.*`, a restore takes 10.0.15, and `dotnet pack` writes `10.0.15` as the lowest version of the dependency, not `10.0.0`.

1. `Directory.Packages.props` sets `CentralPackageFloatingVersionsEnabled` to true, and gives each package the version `{major}.*`, for example `10.*`, `4.*`, or `6.*`. A package with the major 0 floats within its minor, for example `0.15.*`, because a new minor of a version 0 can break.
2. These packages keep an exact version, with the reason in their label:
   - `Microsoft.CodeAnalysis.CSharp` and `Microsoft.CodeAnalysis.Analyzers` of the source generator, because a generator loads only on a compiler that is not older than the Roslyn that it references.
   - Each package whose label gives a reason for its version. The phase checks each reason: `RabbitMQ.Client` for WolverineFx.RabbitMQ, and `Microsoft.Data.SqlClient` for the advisory, where `6.*` gives 6.1.8 or later.
3. The build and the tests run with the new versions. A new warning or a failed test gets a fix in this phase, or its package keeps an exact version with the reason in its label.
4. `VersionPrefix` becomes `0.2.1`, because the packages get newer dependencies and no new feature. Each README of a package with a changed dependency gets the entry 0.2.1.
5. Describe the phase: D317, section 5.2 of the design, and a rule for package versions in `AGENTS.md`.

Checks before the code:
- The Roslyn version of the compiler of the SDK of `global.json`, and the version that the generator references.
- Each analyzer package, for example `Microsoft.CodeAnalysis.BannedApiAnalyzers`, loads on that compiler at the latest version of its major.

Tests:
- No new test. All unit tests pass with the new versions, and `pwsh ./coverage.ps1` and `pwsh ./pack.ps1` pass.

Check for the owner: run the integration tests of the stores with the new versions.

Checked facts (2026-11-24), on Windows 11 build 26300 with the .NET SDK 10.0.404:

| Fact | Source |
|---|---|
| `global.json` asks for SDK 10.0.100 with `latestFeature`, so this machine builds with 10.0.404 and Roslyn 5.9.0. The SDK 10.0.204 has Roslyn 5.3.0. The generator references `Microsoft.CodeAnalysis.CSharp` 5.0.0, the Roslyn of SDK 10.0.100, so each SDK of the major 10 loads it. | The file versions of `Microsoft.CodeAnalysis.CSharp.dll` of each installed SDK |
| With no `CentralPackageFloatingVersionsEnabled`, the restore fails with `NU1011` for each floating `PackageVersion`. | `dotnet restore -p:CentralPackageFloatingVersionsEnabled=false` |
| The restore gives these new versions: the `Microsoft.Extensions`, ASP.NET Core, and EF Core packages 10.0.15 (from 10.0.14), `Microsoft.Extensions.Caching.Hybrid`, `.TimeProvider.Testing`, and `.Diagnostics.Testing` 10.11.1 (from 10.11.0), `AWSSDK.SimpleEmailV2` 4.1.1, `AWSSDK.SimpleNotificationService` and `AWSSDK.SQS` 4.1.0.2, `Fluid.Core` 2.41.2, the `WolverineFx` packages 6.65.1, `MongoDB.Driver` 3.14.1, `ZiggyCreatures.FusionCache` 2.10.0, the `OpenTelemetry` packages 1.21.0, `Microsoft.IdentityModel.JsonWebTokens` 8.23.0, the `Testcontainers` packages 4.17.0, `xunit.v3` 4.0.3, `Microsoft.NET.Test.Sdk` 18.12.0, and `coverlet.collector` 10.1.1. The restore gives no warning. | `project.assets.json` of each project |
| `WolverineFx.RabbitMQ` 6.65.1, the latest of the major 6, still needs `RabbitMQ.Client` 7.2.2 for `net10.0` and `net9.0`, and `7.*` gives 7.3.0. So the reason holds, and `RabbitMQ.Client` keeps 7.2.2. | The nuspec of `WolverineFx.RabbitMQ` 6.65.1 in the NuGet cache |
| `Microsoft.EntityFrameworkCore.SqlServer` 10.0.15 still depends on `Microsoft.Data.SqlClient` 6.1.7, and `6.*` gives 6.1.8. So the test projects keep their reference of `Microsoft.Data.SqlClient`, with `6.*`. | The nuspec of `Microsoft.EntityFrameworkCore.SqlServer` 10.0.15 |
| `Microsoft.CodeAnalysis.BannedApiAnalyzers` `3.*` gives 3.3.4, the version of D32. | `project.assets.json` |
| No analyzer DLL of a resolved package references a Roslyn newer than 5.0.0, so each one loads on the compiler of SDK 10.0.100. The newest reference is 5.0.0, of `Microsoft.EntityFrameworkCore.Analyzers` 10.0.15. `xunit.analyzers` 2.3.0 references 4.11.0, and `Microsoft.CodeAnalysis.BannedApiAnalyzers` 3.3.4 references 2.9.0. | The assembly references of each analyzer DLL of the 282 resolved packages |
| Each package version that the phase changes has a license of the rule of section 5.5. The 7 packages with a license file or a license URL keep the versions of phase 46: the two packages of D179, `NSubstitute.Analyzers.CSharp` 1.0.17, `NETStandard.Library` 2.0.3, `Microsoft.NETCore.Platforms` 1.1.0, and `CommandLineParser` 2.9.1 and `Microsoft.DotNet.PlatformAbstractions` 3.1.6 of `BenchmarkDotNet` 0.15.8. | The nuspec of each of the 282 resolved packages |
| `dotnet pack` writes the resolved version as the lowest version of a dependency, for example `Microsoft.Extensions.Options` 10.0.15 for `10.*`. Each MsgHatch dependency is 0.2.1. | The nuspec of each 0.2.1 package |
| Only 15 packages get a newer dependency of their own, the 15 packages with the entry 0.2.1: `MsgHatch.Domain`, `MsgHatch.AdminUI`, the three client packages, `MsgHatch.Broker.AmazonSns`, `MsgHatch.Broker.Wolverine`, `MsgHatch.Data.EFCore`, `MsgHatch.Data.MongoDb`, `MsgHatch.Data.Sqlite`, `MsgHatch.Email.AmazonSES`, `MsgHatch.Email.Resend`, the two cache packages, and `MsgHatch.Templates.Liquid`. The other 21 packages depend only on MsgHatch packages, or keep their dependency, as `MsgHatch.Dispatch.RabbitMq` does. | A comparison of the dependencies of each nuspec of 0.2.0 and of 0.2.1 |
| The build of the solution with `--artifacts-path` in a scratch folder has no errors and no warnings. The 38 unit test projects pass 5,849 tests and skip 1, the path limit test of the named pipe signal, the same as after phase 46. The line coverage is 98.4 %. `pwsh ./pack.ps1` passes with 36 packages of version 0.2.1. | `dotnet build`, `pwsh ./coverage.ps1`, `pwsh ./pack.ps1` |
| The pack of the solution gives the warning "This project cannot be packaged because packaging has been disabled" for the 4 web examples `AdminUiExample`, `ExampleApp`, `SignalRDemo`, and `WolverineExample.Consumer`. The phase changes no project file, so the warning is not from this phase. | The log of `pwsh ./pack.ps1` |

Checks that the phase did not do:
- The integration tests of the stores and of the brokers with the new versions. They are the check for the owner.
- A run on Linux before the push. The workflow run of the push builds and tests on Linux.

Commits:
1. "Plan phases 47 to 49: floating versions and the provider packages of the EF Core stores"
2. "Float each package version within its major"
3. "Describe the floating versions in the design and AGENTS.md"
4. "Record the commits and the checked facts of phase 47 in the plan"
5. "Record phase 47 as done in README and AGENTS.md"

Commit 1 holds the plans of phases 47, 48, and 49, and the decisions D317 to D319. A second plan commit adds the schema migrations to phases 48 and 49, and the decision D320. Commit 2 holds items 1 to 4. Commit 5 also sets the range of the decisions in `AGENTS.md`, which says D315 since phase 46.

The plan of this phase is in commit 1 (`8f03d01`) and in the second plan commit (`721f09b`). Commit 2 holds items 1 to 4 (`1cfa252`), and commit 3 item 5 (`2fa29e8`). Item 3 needed no fix, because the build gave no new warning and no test failed.

The implementation changed the plan in these points:
- `RabbitMQ.Client` keeps 7.2.2 after the check of item 2, and its label names `WolverineFx.RabbitMQ` 6.65.1. `Microsoft.Data.SqlClient` gets `6.*`.
- `Microsoft.CodeAnalysis.BannedApiAnalyzers` gets `3.*` and no exact version, because the major 3 gives 3.3.4, so D32 holds.
- Item 4 gives the entry 0.2.1 only to the packages whose own dependencies change. A package with only MsgHatch dependencies gets no entry, as in phase 45.
- The entry 0.2.1 names no resolved version, because the pack of a release can resolve a newer version than the pack of this phase.
- Section 5.2 of the design said that a package version changes only in its own commit. Now a new major or a change of an exact version is a commit of its own.

Done when: the build has no errors and no warnings, all unit tests pass, `pwsh ./coverage.ps1` passes, and `pwsh ./pack.ps1` passes with 36 packages of 0.2.1.


### Phase 48: Provider-agnostic EF Core stores, with SQL Server packages

Goal: `MsgHatch.Data.EFCore` has no SQL text and no provider name, and runs on each relational EF Core provider with no change. The new packages `MsgHatch.Data.EFCore.SqlServer` and `MsgHatch.Subscriptions.Data.EFCore.SqlServer` hold the parts for SQL Server: the claim with `READPAST`, the default collation, the schema migrations, and a setup with one call (D318 to D320).

Starting point: the commits of phase 47. The stores select their SQL by the provider of the context (D120), and only SQL Server has SQL. A context of another provider fails the startup validation (D175). The host makes the schema with its own migrations. These parts are for SQL Server only:
- `SqlServerStatements`: names in brackets, an insert if absent with `WITH (UPDLOCK, HOLDLOCK)`, the claim with `TOP`, `WITH (ROWLOCK, READPAST, UPDLOCK)`, and `OUTPUT deleted.NextTryAtUtc`, and the functions `JSON_QUERY`, `JSON_VALUE`, `JSON_MODIFY`, and `OPENJSON`.
- `SqlArguments`: a time is `DbType.DateTime2`.
- `StoreModel`: the provider name, and the binary collation `Latin1_General_100_BIN2`.
- `MsgHatchDbContext`: the filter `Status = 'Pending'` of the due indexes.

The subscription store, the LINQ of the registry store, the error mapping, the execution strategy, and the schema option have no part for SQL Server only.

1. The claim of the base package with no SQL text:
   - A LINQ query reads the due candidates of a claim in the order of `NextTryAtUtc` and `Sequence` (D144).
   - One `ExecuteUpdate` claims the candidates that are still pending and due. It sets a new claim token, the old due time, the due time, and the lease end. The old due time comes first, because some databases, for example MySQL, apply the assignments from left to right.
   - The store reads the rows of its token. When another dispatcher took a candidate first, the store reads the next candidates, until it has the maximum count or no due message is left. So two parallel claims of 5 get 10 different messages, as the contract test requires.
2. The writes of the base package with no SQL text:
   - An add is an insert on the unique key. When the key exists, the store reads the stored row and treats the add as done. When an outbox batch meets a stored id, the store adds the other messages in a new transaction, because some databases, for example PostgreSQL, end a transaction after an error.
   - A channel state row is an insert on the unique key of the channel and the version. A stored version gives false, so `ChannelState.ChangeAsync` reads the latest row again.
   - The store splits a message into its columns in C#, and builds the document in C# (D176).
   - A try record and a cancel write the new text of the tries with an `ExecuteUpdate` on the condition of the `TryCount` that the store read. When another writer changed the row first, the store reads it again.
3. The model of the base package with no provider name:
   - The due indexes have no filter. `Status` is a column of each due index.
   - The new option `TextCollation` sets the collation of each id and name. With no value, the collation comes from the provider package, or else from the database.
   - Each time is a `DateTime` in the time type of the provider (D319). The value converter also sets the kind UTC on a write, because a provider can refuse a time with no kind.
   - The startup check of D175 accepts each relational provider, and refuses a provider that is not relational. It also fails when a registered provider package does not match the provider of the context.
4. The public contract of a provider package in the base package, for example `IEntityFrameworkStoreDialect`:
   - The name of the EF Core provider that it serves.
   - The default collation of the ids and the names.
   - An optional claim statement. The base package gives it the names of the tables and the columns with the quotes of the provider, from the EF Core model and `ISqlGenerationHelper`, and runs it with `FromSqlRaw`. With no claim statement, the base package uses the claim of item 1.
   - The contract does not expose the row types of the stores.
5. The package `MsgHatch.Data.EFCore.SqlServer`:
   - It references `MsgHatch.Data.EFCore` and `Microsoft.EntityFrameworkCore.SqlServer`.
   - Its dialect has the claim of D120 with `READPAST` and `OUTPUT`, and the default collation `Latin1_General_100_BIN2`.
   - It holds the migrations of `MsgHatchDbContext` for SQL Server, which the EF Core tools generate (D320).
   - `UseEntityFrameworkSqlServerStores` registers the stores with `UseSqlServer`, the dialect, and the migrations in one call.
   - It has its `README.md`, its `Description`, its `PackageTags`, and its README tests.
6. The package `MsgHatch.Subscriptions.Data.EFCore.SqlServer`:
   - It references `MsgHatch.Subscriptions.Data.EFCore` and `MsgHatch.Data.EFCore.SqlServer`, so the store packages never reference `MsgHatch.Subscriptions`.
   - It holds the migrations of `MsgHatchSubscriptionDbContext` for SQL Server, and uses the dialect of `MsgHatch.Data.EFCore.SqlServer`.
   - `UseEntityFrameworkSqlServerSubscriptionStore` registers the subscription store with `UseSqlServer`, the dialect, and the migrations in one call.
   - It has its `README.md`, its `Description`, its `PackageTags`, and its README tests.
7. The migration step of the base package (D320):
   - `MigrateMsgHatchAsync` applies the migrations of each registered MsgHatch context. No other code of MsgHatch changes the schema.
   - An optional `Action<DbContextOptionsBuilder>` gives the migration its own provider setup, for example a connection string with the rights to change the schema. With no action, the migration uses the setup of the stores. MsgHatch never logs a connection string.
   - A host can call the method at its start. In a setup where only the pipeline has the rights to change the schema, a separate console app calls the same method with the connection string of the pipeline, and the service gets only the rights to read and write rows.
   - `ScriptMsgHatchMigrationsAsync` gives an idempotent SQL script of the MsgHatch migrations, for a pipeline or a database administrator that runs only SQL.
   - The option `RequireCurrentSchema` makes the start of a host fail when a MsgHatch migration is not applied, with an error that names the context and the migration. The check reads only the history tables, so it needs no right to change the schema.
   - Each context has a history table of its own in the schema of the option, for example `__MsgHatchMigrations` and `__MsgHatchSubscriptionMigrations`, so the history of the host does not change.
   - Each migration takes the schema of the option at run time.
   - For a provider with no MsgHatch package, the host generates the migrations of the MsgHatch contexts in its own assembly with `MigrationsAssembly`, and the same method applies them.
   - The new example `examples/SchemaMigrator` is a console app for a pipeline. It reads the connection string from an environment variable, applies the MsgHatch migrations, or writes the script with an argument. Its README shows a step of a pipeline, and the setup of the service with `RequireCurrentSchema`.
8. `SqlServerStatements`, `SqlArguments`, and the provider name of `StoreModel` go away from the base package.
9. Tests:
   - The new unit test projects `tests/MsgHatch.Data.EFCore.Tests.Unit` and `tests/MsgHatch.Subscriptions.Data.EFCore.Tests.Unit` run the contract bases on the EF Core SQLite provider, with a file in a temporary folder (D71). So the coverage includes the base packages.
   - `tests/MsgHatch.Data.EFCore.Tests.Integration` and `tests/MsgHatch.Subscriptions.Data.EFCore.Tests.Integration` run the contract bases on SQL Server with no dialect, so the claim of item 1 meets the concurrency of a real server.
   - The new projects `tests/MsgHatch.Data.EFCore.SqlServer.Tests.Integration` and `tests/MsgHatch.Subscriptions.Data.EFCore.SqlServer.Tests.Integration` run the contract bases on SQL Server with the dialect, on a database that the migrations of their package created, and the README tests of their package. They use the fixture and the test data of `MsgHatch.Data.EFCore.Tests.Integration`.
10. `VersionPrefix` becomes `0.3.0`. The schema of the EF Core stores changes: a claim token, the old due time, the due indexes with no filter, and the collation of the option. Before 1.0.0, a breaking change raises the minor. The READMEs of `MsgHatch.Data.EFCore` and `MsgHatch.Subscriptions.Data.EFCore` get the entry 0.3.0, which starts with "Breaking:", and show the setup with the SQL Server packages, with `UseSqlite`, and with the migration step.
11. Describe the phase:
    - In the design: D318 to D320, the changes of D120, D175, and D178, and sections 4.2, 4.5, 4.7, 5.1, 5.6, and 7.
    - In `docs/stores.md`: the section of the EF Core stores, with the two claims, the writes, the types, the dialect contract, the migrations, and the limits.
    - The count of 38 packages in `AGENTS.md`, `README.md`, and the design.

Checks before the code:
- An `ExecuteUpdate` that sets a column from the old value of another column works on SQLite and SQL Server.
- Parallel claims on one SQLite file get no `SQLITE_BUSY` error within the timeout of the connection.
- `ISqlGenerationHelper` gives the quoted names of the schema, the tables, and the columns for the claim statement of a dialect.
- How a migration takes the schema of the option at run time, for example through a custom `IMigrationsAssembly` that gives the schema to each migration.
- Whether `MigrateAsync` of EF Core 10 locks the database, so two hosts that start at the same time do not apply one migration two times.
- A console app with `MsgHatchRoles.None` and no message type passes the startup validation of MsgHatch.

Tests:
- The contract bases of the inbox, the outbox, the registry settings, and the subscription store, with the scenario of Acme and Globex, pass on SQLite, on SQL Server with no dialect, and on SQL Server with the dialect. They include two parallel claims, 160 parallel adds, parallel cancels, parallel try records, and parallel channel states.
- A claim of the base package whose first candidates another dispatcher took claims the next due messages.
- Two tenants that differ only in case stay apart on SQL Server with the collation of the dialect, and with the collation of the option.
- The migrations create the schema of each package on an empty SQL Server database, in the schema of the option, with two history tables. A second call of `MigrateMsgHatchAsync` changes nothing.
- The model of each context has no pending change against the migrations of its package.
- A migration with its own provider setup uses its own connection string, and the stores keep theirs.
- The script of `ScriptMsgHatchMigrationsAsync` creates the same schema on an empty database, and a second run of it changes nothing.
- With `RequireCurrentSchema`, a start with a pending migration fails with the names of the context and the migration, and a start with each migration applied passes.
- `examples/SchemaMigrator` applies the migrations on an empty SQL Server database with the connection string of its environment variable, and writes the script with its argument.
- The setup: SQLite, SQL Server with no dialect, and SQL Server with the dialect pass the startup validation. A provider that is not relational fails, and a dialect for another provider fails.
- The README snippets.
- Each new test fails first. The SQLite tests fail before the change, because SQLite has no SQL statements.

Check for the owner: run the EF Core integration projects on SQL Server.

Commits:
1. "Run the EF Core stores on each relational provider"
2. "Add the dialect contract and the collation option of the EF Core stores"
3. "Add the SQL Server package of the EF Core stores with its migrations"
4. "Add the SQL Server package of the EF Core subscription store"
5. "Add the schema migrator example"
6. "Describe the provider-agnostic EF Core stores in the design and the store guide"
7. "Record the commits and the checked facts of phase 48 in the plan"
8. "Record phase 48 as done in README and AGENTS.md"

The plan of this phase is in commit 1 of phase 47. Commit 1 holds items 1, 2, and 8, the claim of D120 in place until commit 3, the SQLite tests of item 9, and the version of item 10. Commit 2 holds items 3 and 4. Commit 3 holds items 5 and 7 with their tests, except the example of item 7. Commit 4 holds item 6 with its tests. Commit 5 holds the example of item 7 with its test. Each commit of a package holds its README of item 10.

Checked facts (2026-11-24), on Windows 11 build 26300 with the .NET SDK 10.0.404, EF Core 10.0.15, and SQL Server 2025 of the local container dev-mssql:

| Fact | Source |
|---|---|
| An `ExecuteUpdate` that sets `ClaimedDueAtUtc` from the old `NextTryAtUtc` works on SQLite and on SQL Server. | The claim tests of the contract bases, and `ClaimDueAsync_DueMessage_WritesTheClaimTokenAndTheDueTimeBeforeTheClaim` |
| Parallel claims and 160 parallel adds on one SQLite file get no `SQLITE_BUSY` error with the default busy timeout of `Microsoft.Data.Sqlite`, 30 seconds. With `Default Timeout=5`, one full run of the unit project hit the timeout in the 160 parallel adds with the claim statement of a dialect, so the tests use the default. | The SQLite unit tests, two full runs |
| `ISqlGenerationHelper.DelimitIdentifier` gives `[msghatch].[MsgHatchOutbox]` and `[NextTryAtUtc]` on SQL Server, and `"MsgHatchOutbox"` with no schema on SQLite. `RelationalTypeMapping.CreateParameter` gives each parameter the type of its column, with the value converter. | The log of the executed claim statement of the SQL Server dialect, and the contract tests with the SQLite test dialect |
| SQL Server cannot use the filtered due index `WHERE Status = 'Pending'` for the parameter `@pending`. The subquery then read each due row, and the test of two parallel claims failed in some full runs. The due index with `Status` as a column and no filter passed three full runs. | The integration tests of commit 1 |
| `IMigrationsAssembly` of EF Core 10 has 5 public members: `Migrations`, `ModelSnapshot`, `Assembly`, `FindMigrationId`, and `CreateMigration`. The default implementation is in a namespace `Internal`, so the base package implements the public interface. | The XML documentation of `Microsoft.EntityFrameworkCore.Relational` 10.0.15 |
| `MigrationsAssembly(Assembly)` keeps the assembly in `RelationalOptionsExtension.MigrationsAssemblyObject`, and `MigrationsAssembly` stays null. The first version of the migrations assembly read only the name, found no migration, and created no table. | The first run of the SQL Server package tests |
| A model with the schema `other` and the collation `Latin1_General_100_CS_AS` has no pending model change against the migrations of each SQL Server package, because the snapshot takes the schema and the collation of the options. | `HasPendingModelChanges_SchemaAndCollationOfTheOptions_HasNoChangeAgainstTheMigrationsOfThePackage` |
| EF Core 10 has `HistoryRepository.AcquireDatabaseLock` and `IMigrationsDatabaseLock`: `MigrateAsync` takes an exclusive lock on the database, so two hosts that start at the same time do not apply one migration two times. | The XML documentation of `Microsoft.EntityFrameworkCore.Relational` 10.0.15 |
| `GetPendingMigrations` reads only the history table, and gives each migration when the database or the history table is missing. So `RequireCurrentSchema` needs no right to change the schema. | The tests of `RequireCurrentSchema` on SQLite and on SQL Server |
| A console app with `MsgHatchRoles.None` and no message type passes the startup validation of MsgHatch. | `Build_HostWithNoRoleAndNoMessageType_PassesTheStartupValidationOfMsgHatch` |
| The global tool `dotnet-ef` 10.0.2 was installed already, so the phase downloaded no tool. With the runtime 10.0.15, it writes the warning "older than that of the runtime" and generates the migrations. It uses the `IDesignTimeDbContextFactory` of the test project, does not start the entry point of xUnit, and finds the generator through `DesignTimeServicesReference` of the startup assembly. | The verbose output of `dotnet ef migrations add` |
| The analyzers check a generated migration with no header `// <auto-generated />`, for example IDE0161 and CA1861. A file `.editorconfig` with `generated_code = true` in the folder `Migrations` stops them. | The first build of the SQLite migrations of the host |
| EF Core writes no idempotent migration script for SQLite, so the tests of `ScriptMsgHatchMigrationsAsync` run on SQL Server. The script of SQL Server has the batch separator `GO`. | The EF Core documentation of the migration scripts, and the script tests |
| `Microsoft.EntityFrameworkCore.SqlServer` 10.0.15 brings `Microsoft.Data.SqlClient` 6.1.7 with `System.Security.Cryptography.Pkcs` 10.0.13, and the restore of the SQL Server package gives no audit warning. So the package has no direct reference of `Microsoft.Data.SqlClient`. | `project.assets.json`, `dotnet restore --force` |
| `RegularExpressionAttribute` accepts an empty text, so `TextCollation` also has `MinLength(1)`. | `Validate_TextCollation_FailsWhenItHasACharacterOutsideTheNameOfACollation` |
| `GetCollation` of a property needs the design-time model of `IDesignTimeModel`. The read-optimized model of `DbContext.Model` throws. | The first run of `Model_TextCollation_IsTheCollationOfEachTextColumn` |
| EF Core writes a time on SQLite as the text `yyyy-MM-dd HH:mm:ss.FFFFFFF`. | The raw reads of the SQLite unit tests |
| The build of the solution with `--artifacts-path` in a scratch folder has no errors and no warnings. The 40 unit test projects pass 6,500 tests and skip 1, the path limit test of the named pipe signal. The line coverage is 98.3 %, and 98 % for `MsgHatch.Data.EFCore`. Its open lines are the script, which needs SQL Server, and the unreadable history of `RequireCurrentSchema`. | `dotnet build`, `pwsh ./coverage.ps1` |
| `pwsh ./pack.ps1` passes with 38 packages of version 0.3.0. The two new packages depend on their MsgHatch packages of 0.3.0, and `MsgHatch.Data.EFCore.SqlServer` on `Microsoft.EntityFrameworkCore.SqlServer` 10.0.15. The pack warning "packaging has been disabled" names only the 4 web examples, as in phase 47. | `pwsh ./pack.ps1` |
| The EF Core integration projects pass on dev-mssql: `MsgHatch.Data.EFCore.Tests.Integration` 332 tests with no dialect and the binary collation of the option, `MsgHatch.Subscriptions.Data.EFCore.Tests.Integration` 52, `MsgHatch.Data.EFCore.SqlServer.Tests.Integration` 308 with the dialect on a database of the migrations, `MsgHatch.Subscriptions.Data.EFCore.SqlServer.Tests.Integration` 43, and `SchemaMigrator.Tests.Integration` 6. No run started a container: `docker events` show no create of a container from 10:00 to 11:50 UTC, and the three new projects read the settings of `MsgHatch.Data.EFCore.Tests.Integration`, which reach their output. | `dotnet test` of each project, `docker events`, `docker ps` |

Checks that the phase did not do:
- The owner check: the EF Core integration projects on SQL Server, on the machine of the owner. The phase ran them on dev-mssql.
- A run on Linux before the push. The workflow run of the push builds and tests on Linux, with no integration project.

Commits:
1. "Run the EF Core stores on each relational provider"
2. "Add the dialect contract and the collation option of the EF Core stores"
3. "Add the SQL Server package of the EF Core stores with its migrations"
4. "Add the SQL Server package of the EF Core subscription store"
5. "Add the schema migrator example"
6. "Describe the provider-agnostic EF Core stores in the design and the store guide"
7. "Record the commits and the checked facts of phase 48 in the plan"
8. "Record phase 48 as done in README and AGENTS.md"

Commit 1 is `dda16a0`, commit 2 `922f1ee`, commit 3 `f09a7b7`, commit 4 `0c0942a`, commit 5 `20683d6`, and commit 6 `c80ec8b`.

The implementation changed the plan in these points:
- The due indexes with no filter of item 3 are in commit 1, because the claim of D120, which stays until commit 3, has the status as a parameter. `Sequence` is the last column of each due index, so the index gives the claim order on each provider.
- Commit 1 also accepts each relational provider in the startup check, so the SQLite tests run. The provider name of `StoreModel` stays for the collation until commit 2. The claim of D120 stays as the internal `SqlServerClaim`, chosen by the provider name, until commit 3 moves it into `SqlServerStoreDialect`.
- `TextCollation` sets the collation of each text column, not only of the ids and the names, as the binary collation of D178 did.
- A provider package gives its dialect to the options of each context with `UseEntityFrameworkStoreDialect`, not through DI. So the stores and the subscription store of one host can use two providers. `SqlServerStoreDialect` is public with `Instance`.
- `ClaimStatementNames` is a public record with the quoted names of the table and the columns, and the placeholders of the parameters. The base package gives each parameter the type of its column.
- The migrations derive from the public base classes `EntityFrameworkStoreMigration` and `EntityFrameworkStoreModelSnapshot`. The internal `StoreMigrationsAssembly` replaces `IMigrationsAssembly`, and gives them the schema and the collation of the options. The generator `StoreMigrationsCodeGenerator` of `tests/MsgHatch.Data.EFCore.Tests.Integration` writes `Schema` and `TextCollation` into the generated code, and makes the classes internal.
- `MigrateMsgHatchAsync` and `ScriptMsgHatchMigrationsAsync` are extension methods of `IServiceProvider` in `MsgHatch.Data.EFCore.DependencyInjection`. A failed migration throws the exception of EF Core, because a migration step must stop the pipeline. `RequireCurrentSchema` is in the shared `EntityFrameworkStoreOptions`.
- `UseEntityFrameworkSqlServerStores` has the parameters `connectionString`, `configureOptions`, and `configureSqlServer`, and `UseEntityFrameworkSqlServerSubscriptionStore` has `connectionString` and `configureSqlServer`.
- An outbox batch drops a second message with the same id before the insert. The old `INSERT ... WHERE NOT EXISTS` stored the first one, and the contract test requires it.
- The README tests of the two base packages stay in their integration projects, which reference the SQL Server packages for the snippets of their setup. The snippet of the migration step runs on SQL Server, and the async snippets take a cancel token for the rule xUnit1051.
- `tests/MsgHatch.Data.EFCore.Tests.Unit` keeps the migrations of a host on SQLite in the folder `SqliteMigrations`. They test the migration step in the unit tests, and the path of a provider with no MsgHatch package.
- The three new integration projects have no appsettings files: the files of `MsgHatch.Data.EFCore.Tests.Integration` reach their output. `TestDatabase` gets a create step and `CreateEmptyAsync`, and `SqlServerSchema` reads the schema and runs a script.
- The test project of the example is `tests/SchemaMigrator.Tests.Integration`, because the example needs SQL Server. The example reads its connection string through `SchemaMigratorOptions` with a source-generated validator.
- The options tests of `EntityFrameworkStoreOptions` move to `MsgHatch.Data.EFCore.Tests.Unit`. The tests use the new package versions `Microsoft.EntityFrameworkCore.InMemory` and `Microsoft.EntityFrameworkCore.Design` `10.*`.
- The test "two tenants that differ only in case stay apart" runs with the collation of the dialect in the contract tests of the SQL Server package, with the binary collation of the option in the base integration project, and with the collation `Latin1_General_100_CS_AS` of the option on a migrated database.

Done when: the build has no errors and no warnings, all unit tests pass, `pwsh ./coverage.ps1` passes, `pwsh ./pack.ps1` passes with 38 packages of 0.3.0, and the EF Core integration projects pass on SQL Server.

### Phase 49: PostgreSQL packages of the EF Core stores

Goal: the new packages `MsgHatch.Data.EFCore.PostgreSql` and `MsgHatch.Subscriptions.Data.EFCore.PostgreSql` run the EF Core stores on PostgreSQL with a claim with `SKIP LOCKED`, the default collation `C`, the schema migrations, and a setup with one call. The base packages do not change (D318 to D320).

Starting point: the commits of phase 48. The base packages run on each relational provider, and the SQL Server packages show a dialect and a migration set.

1. The package `MsgHatch.Data.EFCore.PostgreSql`:
   - It references `MsgHatch.Data.EFCore` and `Npgsql.EntityFrameworkCore.PostgreSQL`.
   - Its dialect has the claim `UPDATE ... FROM` a subquery with `ORDER BY` of the due time and `"Sequence"`, `LIMIT`, and `FOR UPDATE SKIP LOCKED`. `RETURNING` gives the old due time from the subquery (D144).
   - The default collation is `C`, so an id or a name is case-sensitive and sorts in ordinal order.
   - It holds the migrations of `MsgHatchDbContext` for PostgreSQL (D320).
   - `UseEntityFrameworkPostgreSqlStores` registers the stores with `UseNpgsql`, the dialect, and the migrations in one call.
   - It has its `README.md`, its `Description`, its `PackageTags`, and its README tests.
2. The package `MsgHatch.Subscriptions.Data.EFCore.PostgreSql`:
   - It references `MsgHatch.Subscriptions.Data.EFCore` and `MsgHatch.Data.EFCore.PostgreSql`.
   - It holds the migrations of `MsgHatchSubscriptionDbContext` for PostgreSQL, and uses the dialect of `MsgHatch.Data.EFCore.PostgreSql`.
   - `UseEntityFrameworkPostgreSqlSubscriptionStore` registers the subscription store with `UseNpgsql`, the dialect, and the migrations in one call.
   - It has its `README.md`, its `Description`, its `PackageTags`, and its README tests.
3. A time is `timestamptz`, which keeps microseconds (D319).
4. Tests:
   - The new projects `tests/MsgHatch.Data.EFCore.PostgreSql.Tests.Integration` and `tests/MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Integration` run the contract bases on PostgreSQL with the dialect, on a database that the migrations of their package created, and their README tests. The fixture `PostgreSqlFixture` uses `PostgreSqlIntegration:ConnectionString` of `appsettings.Local.json` (D180). With no connection string, Testcontainers starts the image `postgres:18.7`.
   - `tests/MsgHatch.Data.EFCore.Tests.Integration` and the subscription integration project also run the contract bases on PostgreSQL with no dialect, in a folder `PostgreSql`, so the base packages show a third provider with no change.
   - A database name of PostgreSQL has at most 63 bytes, and PostgreSQL cuts a longer name with no error. The prefix of the subscription project has 53 characters, so its names `subscriptions`, `setup_alone`, and `setup_schema` get names of at most 10 characters on PostgreSQL.
5. Packages: `Npgsql.EntityFrameworkCore.PostgreSQL` `10.*`, and `Testcontainers.PostgreSql` `4.*` for the tests.
6. `VersionPrefix` becomes `0.4.0`, because the new packages are a new feature. The READMEs of the two base packages show the setup with the PostgreSQL packages, and get the entry 0.4.0.
7. `examples/SchemaMigrator` also applies the PostgreSQL migrations, and also writes their script.
8. Describe the phase: the design (sections 4.2, 4.5, 5.1, and 5.6), the EF Core section of `docs/stores.md`, `AGENTS.md` with the commands and the settings of the PostgreSQL tests, and the count of 40 packages.

Checks before the code:
- Npgsql cuts a time down to the microsecond, and does not round it up. A stored due time that is later than the given time delays its claim.
- `FromSqlRaw` of an `UPDATE ... RETURNING` gives the rows with no composition.
- The image `postgres:19.0` also exists. The phase checks whether Npgsql 10 supports PostgreSQL 19, and uses 18.7 when it does not.

Tests:
- The contract bases pass on PostgreSQL with the dialect and with no dialect.
- Two tenants that differ only in case stay apart with the collation `C`.
- A time with a part of a microsecond comes back cut down to the microsecond (D319).
- The migrations create the schema of each package on an empty PostgreSQL database, in the schema of the option, and a second call changes nothing. The model of each context has no pending change against its migrations.
- `examples/SchemaMigrator` applies the PostgreSQL migrations on an empty database, and writes their script.
- The setup with the two PostgreSQL packages passes the startup validation.
- The README snippets.
- Each new test fails first.

Check for the owner: run the EF Core integration projects on PostgreSQL, with a local PostgreSQL in `appsettings.Local.json` or with Docker.

Commits:
1. "Add the PostgreSQL package of the EF Core stores with its migrations"
2. "Add the PostgreSQL package of the EF Core subscription store"
3. "Test the base EF Core stores on PostgreSQL"
4. "Describe the PostgreSQL packages in the design and the store guide"
5. "Record the commits and the checked facts of phase 49 in the plan"
6. "Record phase 49 as done in README and AGENTS.md"

The plan of this phase is in commit 1 of phase 47. Commit 1 also holds item 7 with its test.

Checked facts (2026-11-24), on Windows 11 build 26300 with the .NET SDK 10.0.404, EF Core 10.0.15, Npgsql 10.0.4, SQL Server 2025 of the local container dev-mssql, and PostgreSQL 18.7 of the local container dev-postgres:

| Fact | Source |
|---|---|
| The images `postgres:18.7` and `postgres:19.0` exist, and `postgres:19.1` does not. | `docker manifest inspect`, with no pull |
| No page of Npgsql 10 names PostgreSQL 19. The compatibility page names only the policy of the supported PostgreSQL versions, and the release notes of the EF Core provider 10 name PostgreSQL 18 only. So the tests use `postgres:18.7`. | npgsql.org: the compatibility page, and the release notes of Npgsql 10.0 and of the EF Core provider 10.0 |
| Npgsql writes a `DateTime` to `timestamptz` as `(value.Ticks - PostgresTimestampOffsetTicks) / 10`, so it cuts a time down to the microsecond and never rounds it up. The claim parameter `nowUtc` is cut in the same way, so a due time with a part of a microsecond does not delay its claim. A time of `2030-01-01T00:00:00.0000019Z` comes back as `2030-01-01T00:00:00.0000010Z`. | `PgTimestamp.Encode` in the source of Npgsql, and `QueryAsync_DueTimeWithAPartOfAMicrosecond_GivesTheDueTimeCutDownToTheMicrosecond` |
| `FromSqlRaw` of the `UPDATE ... RETURNING` of the dialect runs with `AsNoTracking().ToListAsync()` and no composition, and gives the claimed rows. | The contract tests of the PostgreSQL package, and the log of the statement in `ClaimDueAsync_DueMessage_ClaimsItWithTheSkipLockedStatementOfTheDialect` |
| The restore takes `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.4 with `Npgsql` 10.0.4, and `Testcontainers.PostgreSql` 4.17.0. | `project.assets.json` |
| The EF Core tools write the PostgreSQL types `character varying(256)`, `character(36)`, `text`, `timestamp with time zone`, `boolean`, and `bigint` with the identity strategy `IdentityByDefaultColumn`. After the generator, the migrations have no literal `"C"` and no literal `"msghatch"`. | The generated migrations of both PostgreSQL packages |
| PostgreSQL allows 100 connections by default, and Npgsql keeps a pool of up to 100 connections for each connection string. The first run of the PostgreSQL package tests failed 6 of 309 tests with the error 53300, "too many clients already", in the tests of 160 parallel adds, parallel try records, parallel cancels, and the migration of the README. With a pool of at most 10 connections for each test database, each run passed. | `show max_connections` on dev-postgres, the first two runs of `MsgHatch.Data.EFCore.PostgreSql.Tests.Integration` |
| After about 8 calls of `docker manifest inspect`, Docker Hub answered "toomanyrequests" for the unauthenticated pulls of this machine. The owner then started the local container dev-postgres from `postgres:18.7`, so the phase pulled no image. | The output of `docker manifest inspect`, `docker ps` |
| The build of the solution with `--artifacts-path` in a scratch folder has no errors and no warnings. The 40 unit test projects pass 6,500 tests and skip 1, the path limit test of the named pipe signal. The line coverage is 98.3 %. | `dotnet build`, `pwsh ./coverage.ps1` |
| `pwsh ./pack.ps1` passes with 40 packages of version 0.4.0. `MsgHatch.Data.EFCore.PostgreSql` depends on `MsgHatch.Data.EFCore` 0.4.0 and on `Npgsql.EntityFrameworkCore.PostgreSQL` 10.0.4. The pack warning "packaging has been disabled" names only the 4 web examples, as in phase 48. | `pwsh ./pack.ps1`, the nuspec of the package |
| The EF Core integration projects pass on dev-mssql and on dev-postgres: `MsgHatch.Data.EFCore.Tests.Integration` 624 tests, 333 on SQL Server and 291 on PostgreSQL with no dialect, `MsgHatch.Subscriptions.Data.EFCore.Tests.Integration` 87, 53 and 34, `MsgHatch.Data.EFCore.PostgreSql.Tests.Integration` 309, `MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Integration` 43, `MsgHatch.Data.EFCore.SqlServer.Tests.Integration` 308, `MsgHatch.Subscriptions.Data.EFCore.SqlServer.Tests.Integration` 43, and `SchemaMigrator.Tests.Integration` 10. No run started a container, and after the runs dev-postgres has only the databases `postgres`, `template0`, and `template1`. | `dotnet test` of each project, `docker ps` before and after each run, `pg_database` |
| Each new test failed first. A dialect with no claim statement, no collation, and another provider name failed the 2 tests of the `SKIP LOCKED` statement, the collation test, and the startup validation. The PostgreSQL packages with no migrations failed 301 of 309 and 37 of 43 tests. The base claim with no read of the next candidates failed the two parallel claim tests on PostgreSQL with no dialect. A subscription update with no revision condition failed the 2 revision tests on PostgreSQL. A migrator that ignores the provider failed the 2 PostgreSQL runs, the row of the unknown provider, and the startup test of the PostgreSQL host. | The runs of the broken code, which the phase then restored |

Checks that the phase did not do:
- The owner check: the EF Core integration projects on PostgreSQL on the machine of the owner. The phase ran them on dev-postgres.
- A run on PostgreSQL 19, and a run of the PostgreSQL tests through Testcontainers.
- A run on Linux before the push. The workflow run of the push builds and tests on Linux, with no integration project.

Commits:
1. "Add the PostgreSQL package of the EF Core stores with its migrations"
2. "Add the PostgreSQL package of the EF Core subscription store"
3. "Test the base EF Core stores on PostgreSQL"
4. "Describe the PostgreSQL packages in the design and the store guide"
5. "Record the commits and the checked facts of phase 49 in the plan"
6. "Record phase 49 as done in README and AGENTS.md"

Commit 1 is `d20ebb6`, commit 2 `bd8d21c`, commit 3 `cbaf97e`, and commit 4 `74b41e6`.

The implementation changed the plan in these points:
- The change of `examples/SchemaMigrator` (item 7) is in commit 2, not in commit 1, because the example applies the migrations of both PostgreSQL packages. The new option `Provider` of `SchemaMigratorOptions`, `SqlServer` or `PostgreSql`, comes from the environment variable `SchemaMigrator__Provider`. An unknown provider gives the usage error.
- A database name of a PostgreSQL test starts with the name of the test project in lower case, with no `.Tests.Integration`, each `.` as `_`, and `__`, not with the prefix of D180. The name of `MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Integration` alone has 63 characters, so the prefix of D180 leaves no byte for the name of a database. The `__` keeps `msghatch_data_efcore__` from being the start of `msghatch_data_efcore_postgresql__`, and `PostgreSqlFixture` refuses a name of more than 63 bytes. So the subscription project keeps its names `subscriptions`, `setup_alone`, and `setup_schema`.
- `PostgreSqlFixture` gives each test database a pool of at most 10 connections (checked fact).
- The tests run on the local container dev-postgres of the owner, from `PostgreSqlIntegration:ConnectionString` of `appsettings.Local.json`, not on a container of Testcontainers.
- `tests/MsgHatch.Data.EFCore.Tests.Integration` has the PostgreSQL types of the tests in its folder `TestTypes`: `PostgreSqlIntegration`, `PostgreSqlFixture`, `PostgreSqlTestDatabase`, `PostgreSqlSchema`, and the test data of the inbox, the outbox, and the registry settings, which read the documents with `json_build_object`. `TestDatabase` stays the type of SQL Server. The `appsettings.json` files of both base projects describe the key `PostgreSqlIntegration:ConnectionString`.
- The contracts on PostgreSQL with no dialect use the collation of the database, with no option `TextCollation`. A deterministic collation of PostgreSQL is case-sensitive.
- The test of the time cut down to the microsecond reads the due time with a query. The claim needs no test of its own, because Npgsql cuts the parameter of the claim in the same way.
- The test "no pending model change" uses the collation `POSIX` of PostgreSQL in the row with the schema `other`.
- The README tests of the setup with the PostgreSQL packages in the two base integration projects use a constant connection string, because they open no connection.
- The design also changes D10, D180, section 4.7, and section 5.5.

Done when: the build has no errors and no warnings, all unit tests pass, `pwsh ./coverage.ps1` passes, `pwsh ./pack.ps1` passes with 40 packages of 0.4.0, and the EF Core integration projects pass on SQL Server and on PostgreSQL.

### Phase 50: EF Core tables in their schema with no prefix

Goal: the EF Core stores keep their tables in the schema of `EntityFrameworkStoreOptions.Schema` with no prefix `MsgHatch`, because the schema already keeps them apart from the tables of the host. SQLite and MongoDB keep the prefix, because their databases have no schemas (D321). The base package `MsgHatch.Data.EFCore` also gets feature folders (D50), and the EF Core stores classify exceptions in the same order as the transports (D141, D322). The JSON columns get the JSON type of the dialect or of an option (D323).

Starting point: the commits of phase 49. `MsgHatch.Data.EFCore` has 27 files at its root and 7 files in `DependencyInjection`, because the package grew in phase 48 with no new folders. The private static method `ErrorOf` of `StoreContexts<TContext>` gives `Errors.Unavailable` for each `DbException`, so a missing permission or a missing table is transient, and neither a provider package nor a host can change the mapping. Each JSON column is `nvarchar(max)` on SQL Server and `text` on PostgreSQL. `MsgHatchDbContext` maps `MsgHatchInbox`, `MsgHatchOutbox`, `MsgHatchChannelState`, `MsgHatchChannelEntry`, and `MsgHatchMessageTypeEntry` in the schema of the option, default `msghatch`, so a name such as `msghatch.MsgHatchOutbox` repeats the schema. `MsgHatchSubscriptionDbContext` maps `MsgHatchSubscription`. The indexes have the names of the model, for example `IX_MsgHatchInbox_Due`. The history tables are `__MsgHatchMigrations` and `__MsgHatchSubscriptionMigrations`. The 4 provider packages of phases 48 and 49 hold the `Initial` migrations with these names. No package is on nuget.org.

1. `MsgHatchDbContext` maps `Inbox`, `Outbox`, `ChannelState`, `ChannelEntry`, and `MessageTypeEntry`. `MsgHatchSubscriptionDbContext` maps `Subscription`. The names of the indexes drop the prefix, for example `IX_Inbox_Due` and `IX_Outbox_Due`.
2. The history tables are `__StoreMigrations` and `__SubscriptionMigrations`. A history table that the host set in the provider setup stays.
3. The EF Core tools make the `Initial` migrations of `MsgHatch.Data.EFCore.SqlServer`, `MsgHatch.Subscriptions.Data.EFCore.SqlServer`, `MsgHatch.Data.EFCore.PostgreSql`, and `MsgHatch.Subscriptions.Data.EFCore.PostgreSql` again, with the same steps as phases 48 and 49. The phase adds no rename migration, because no database of a consumer has the old names.
4. The SQL text and the test data of the EF Core tests, and the tests of `examples/SchemaMigrator`, use the new names.
5. The SQLite and MongoDB stores do not change.
6. `MsgHatch.Data.EFCore` gets feature folders (D50). The path of each folder is its namespace, and no folder has the name of a type:
   - The root keeps the types that a host uses: the three stores, `EntityFrameworkStoreOptions` with its validator, and `MsgHatchDbContext`. So a host still needs only the namespace `MsgHatch.Data.EFCore`.
   - `DependencyInjection` does not change.
   - `Messages`: `MessageRow`, `InboxRow`, `OutboxRow`, `MessageRows`, `MessageDocuments`, `MessageQueries`, `MessageClaims`, `PublishTries`, `RowWrites`, and `ChannelStateRow`, which only the outbox uses.
   - `Registry`: `ChannelEntryRow` and `MessageTypeEntryRow`.
   - `Model`: `StoreModel`, `StoreContexts`, and `StoreOptionsExtension`.
   - `Providers`, the API of a provider package: `IEntityFrameworkStoreDialect`, `EntityFrameworkStoreDialectExtensions`, and `ClaimStatementNames`.
   - `Migrations`: `EntityFrameworkStoreMigration`, `EntityFrameworkStoreModelSnapshot`, and `StoreMigrationsAssembly`.
   - The 5 public types of `Providers` and `Migrations` get new namespaces, which is a breaking change. The provider packages, the subscription package, the tests, and the README code blocks change their `using` directives. The other store packages have 4 to 12 files, so they stay flat.
7. Exception classifiers of the EF Core stores (D322):
   - `StoreContexts<TContext>` takes the registered `IExceptionClassifier<TContext>` services and reads the dialect of the context. `ErrorOf` becomes an instance method, but not a virtual one. The order: a cancelled token, the classifiers of the host, the dialect, and then the built-in mapping.
   - The 4 EF Core stores get the optional parameter `IEnumerable<IExceptionClassifier<TContext>>? classifiers = null`, as the transports have, and their setup methods give them the registered classifiers. A host adds one with `AddExceptionClassifier<MsgHatchDbContext, TClassifier>()`.
   - `IEntityFrameworkStoreDialect` gets the default method `ErrorType? ErrorTypeOf(DbException exception)`, which gives null.
   - The SQL Server dialect maps `SqlException.Number`: 229, 230, 262, and 18456 to `Errors.Rejected`, and 207, 208, 2628, and 8152 to `Errors.ValidationFailed`.
   - The PostgreSQL dialect maps `PostgresException.SqlState`: `42501`, `28000`, and `28P01` to `Errors.Rejected`, and `42P01`, `42703`, and `22001` to `Errors.ValidationFailed`.
   - The README of each changed package shows a classifier of the host.
8. The JSON column type (D323):
   - The new option `EntityFrameworkStoreOptions.JsonColumnType`, with a validation attribute that accepts a type name such as `json`, `jsonb`, or `nvarchar(max)`. With no value, the type comes from the dialect, or else it is the text type of the provider.
   - `IEntityFrameworkStoreDialect` gets the default property `string? JsonColumnType`, which gives null. The PostgreSQL dialect gives `json`. The SQL Server dialect gives null, so its columns stay `nvarchar(max)`.
   - `StoreModel` gives the type to each JSON column of both contexts, and the migrations take it at run time, as they take the schema and the collation.
   - The READMEs show the option in the options table, with the reason for `json` and not `jsonb` on PostgreSQL.
9. `VersionPrefix` becomes `0.5.0`, because the schema of the EF Core stores, the namespaces of 5 public types, and the dialect contract change, and before 1.0.0 a breaking change raises the minor. The READMEs of `MsgHatch.Data.EFCore`, `MsgHatch.Subscriptions.Data.EFCore`, and the 4 provider packages get the entry 0.5.0, which starts with "Breaking:".
10. Describe the phase: the EF Core text of section 4.5 of the design, the EF Core section of `docs/stores.md`, and `AGENTS.md`.

Checks before the code:
- Each error code of item 7 in the documentation of SQL Server and PostgreSQL, and the exception that EF Core gives for it, for example the provider exception inside a `DbUpdateException`.
- Npgsql writes a `string` into a `json` and a `jsonb` column, also in an `ExecuteUpdate` and in the claim statement. EF Core and `Microsoft.Data.SqlClient` read and write a `string` in a `json` column on SQL Server 2025, the version of dev-mssql.
- The migrations that the tools make again have no literal schema and no literal collation, as in phases 48 and 49.
- The generated migrations of a provider package, in for example the namespace `MsgHatch.Data.EFCore.SqlServer.Migrations`, compile with the base classes in `MsgHatch.Data.EFCore.Migrations`.
- Each place that names an old table, index, or history table, in `src`, `tests`, `examples`, and `docs`.

Tests:
- Each public type of `MsgHatch.Data.EFCore` is in the namespace `MsgHatch.Data.EFCore`, `MsgHatch.Data.EFCore.DependencyInjection`, `MsgHatch.Data.EFCore.Providers`, or `MsgHatch.Data.EFCore.Migrations`.
- On SQLite: a classifier of the host wins over the dialect and the built-in mapping, a classifier that gives null passes the exception to the next step, and a cancelled token gives `Errors.OperationCancelled` before each classifier.
- A store on a database with no migrations gives `Errors.ValidationFailed` on SQL Server and on PostgreSQL.
- The PostgreSQL dialect gives the error type of each code of item 7, from a `PostgresException` that the test builds.
- On PostgreSQL, each JSON column has the type `json`, and an insert of invalid JSON into `Content` fails. The contract bases also pass with the option `jsonb` on PostgreSQL and with the option `json` on SQL Server.
- With no dialect, on SQLite, each JSON column keeps the text type. The migrations with the option create the type of the option, with no pending model change.
- Each table of both contexts is in the schema of the option, and no name of a table, an index, or a history table starts with `MsgHatch`.
- A host with the schema `other` gets `other.Outbox` and `other.__StoreMigrations`.
- The model of each context has no pending change against the migrations of each provider package.
- The EF Core integration projects pass on dev-mssql and on dev-postgres.
- The tests of the SQLite and MongoDB stores pass with no change.
- Each new test fails first.

Check for the owner: run the EF Core integration projects on SQL Server and on PostgreSQL.

Commits:
1. "Move the types of the base EF Core package into feature folders"
2. "Keep the EF Core tables in their schema with no prefix"
3. "Add the exception classifiers of the EF Core stores"
4. "Give the JSON columns of the EF Core stores the JSON type of the dialect"
5. "Describe the changes of the EF Core stores in the design and the store guide"
6. "Record the commits and the checked facts of phase 50 in the plan"
7. "Record phase 50 as done in README and AGENTS.md"

Commit 1 only moves types, so commit 2 makes the migrations with the base classes in their new namespace.

The plan of this phase and D321 to D323 are in their own commit before commit 1.

Checked facts (2026-11-24), on Windows 11 build 26300 with the .NET SDK 10.0.404, EF Core 10.0.15, `Microsoft.Data.SqlClient` 6.1.8, Npgsql 10.0.4, SQL Server 2025 (17.0.4065.4) of the local container dev-mssql, and PostgreSQL 18.7 of the local container dev-postgres:

| Fact | Source |
|---|---|
| Before the code, 563 places in 98 files named an old table, index, or history table. The phase changes the places of the EF Core stores, their tests, `examples/SchemaMigrator`, and the documents. The SQLite and MongoDB stores, their tests, and `tests/ExampleApp.Tests.Unit`, whose example app uses the SQLite stores, keep the prefix. | `git grep` of the old names in `src`, `tests`, `examples`, and `docs` |
| The error numbers of the SQL Server dialect are the messages 207 "Invalid column name", 208 "Invalid object name", 229 and 230 "permission was denied on the object" and "on the column", 262 "permission denied in database", 2628 and 8152 "String or binary data would be truncated", and 18456 "Login failed for user". | `sys.messages` of dev-mssql, read by a temporary test that the phase did not commit |
| The states of the PostgreSQL dialect are the conditions `insufficient_privilege` 42501, `invalid_authorization_specification` 28000, `invalid_password` 28P01, `undefined_table` 42P01, `undefined_column` 42703, and `string_data_right_truncation` 22001. The constants of `PostgresErrorCodes` have these values, because the dialect test with the literal states passes. | Appendix A of the PostgreSQL documentation, and `PostgreSqlStoreDialectIntegTests` |
| A query of a missing table throws the provider exception, 208 or 42P01, with no wrap. `SaveChanges` wraps the provider exception of an insert in `DbUpdateException`: a target id of 300 characters gives 2628 or 8152 on SQL Server and 22001 on PostgreSQL inside it. Npgsql does not cut the value to the size of the column. With the dialect, each of these gives `Errors.ValidationFailed`. | The EF Core documentation of `SaveChanges`, and the integration tests of the database with no migrations and of the long target id |
| PostgreSQL refuses a collation on a `json` column: the first migration with the JSON type failed with "42804: collations are not supported by type json". So a JSON column gets no collation. | The migration test of the PostgreSQL package |
| Npgsql writes a `string` into a `json` and a `jsonb` column with an insert, an `ExecuteUpdate`, and the claim statement, and reads it back. A write of invalid JSON into a `json` column fails with 22P02. | The contract bases of the PostgreSQL packages with the dialect and with the option `jsonb`, and the test of invalid JSON |
| EF Core 10 and `Microsoft.Data.SqlClient` 6.1.8 write and read a `string` in a `json` column on SQL Server 2025, and the claim with `OUTPUT inserted.*` reads it. SQL Server keeps the order of the keys, so `$type` stays the first key. | The contract bases of the SQL Server packages with the option `json` |
| The EF Core tools 10.0.2 made the `Initial` migrations of the 4 provider packages again. With the generator, they have no literal schema, no literal collation, and no literal `"json"`, and they compile with the base classes in `MsgHatch.Data.EFCore.Migrations`. A JSON column has `type: JsonColumnType` and no collation. With no JSON type in the options or the dialect, EF Core gives the column the type of a `string` with no length of the provider. | The generated migrations, and the tests "no pending model change" with the schema `other`, a collation, and the options `json` and `jsonb` |
| The build of the solution with `--artifacts-path` in a scratch folder has no errors and no warnings. The 40 unit test projects pass 6,527 tests and skip 1, the path limit test of the named pipe signal. The line coverage is 98.3 %. | `dotnet build`, `pwsh ./coverage.ps1` |
| `pwsh ./pack.ps1` passes with 40 packages of version 0.5.0. The pack warning "packaging has been disabled" names only the 4 web examples, as in phase 49. | `pwsh ./pack.ps1` |
| The EF Core integration projects pass on dev-mssql and on dev-postgres: `MsgHatch.Data.EFCore.Tests.Integration` 625 tests, `MsgHatch.Subscriptions.Data.EFCore.Tests.Integration` 88, `MsgHatch.Data.EFCore.SqlServer.Tests.Integration` 604, `MsgHatch.Subscriptions.Data.EFCore.SqlServer.Tests.Integration` 75, `MsgHatch.Data.EFCore.PostgreSql.Tests.Integration` 613, `MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Integration` 76, and `SchemaMigrator.Tests.Integration` 10. No container started, and no image was pulled. | `dotnet test` of each project, `docker ps` before and after each run |
| Each new test failed first. The namespace test failed with the two namespaces of the root. The tests of the names failed with the prefix. The classifier tests gave 503 in place of 409 and 403, and the cancel test failed when the classifiers came before the cancel. The dialect tests gave null and 503. The tests of the JSON type failed with no type in the options of a context, and the rows of the text type failed with `json` for each JSON column. The collation tests failed with the collation on the JSON columns. | The test runs before the code, and the temporary changes of the code |

Checks that the phase did not do:
- The owner check: the EF Core integration projects on SQL Server and on PostgreSQL on the machine of the owner. The phase ran them on dev-mssql and dev-postgres.
- A test of a missing permission, of a failed login, and of a missing column against a server. The dialect test of PostgreSQL builds each state, and `sys.messages` gives the SQL Server numbers.
- A run of the EF Core tests through Testcontainers, and a run on Linux before the push. The workflow run of the push builds and tests on Linux, with no integration project.

Commits:
1. "Move the types of the base EF Core package into feature folders"
2. "Keep the EF Core tables in their schema with no prefix"
3. "Add the exception classifiers of the EF Core stores"
4. "Give the JSON columns of the EF Core stores the JSON type of the dialect"
5. "Describe the changes of the EF Core stores in the design and the store guide"
6. "Record the commits and the checked facts of phase 50 in the plan"
7. "Record phase 50 as done in README and AGENTS.md"

Commit 1 is `c86cebb`, commit 2 `7972cf8`, commit 3 `2128a52`, commit 4 `e0be691`, and commit 5 `dc31331`. The plan of this phase is in `35286e0` and `942a30c`.

The implementation changed the plan in these points:
- A JSON column has no collation, because PostgreSQL refuses a collation on `json` (checked fact). The collation tests of both contexts check that the JSON columns have none. A host with migrations of its own makes them again.
- The design-time contexts of the EF Core tools set the option `JsonColumnType` to `json`, and the generator writes `JsonColumnType` in place of the literal `"json"`, because the type `nvarchar(max)` of the design time is also the type of `LastError` and `TraceParent`. At run time, the migration gets the type of the options or of the dialect, or null, and with null EF Core gives the column the text type of the provider. So the migrations need no type mapping source.
- Commit 1 also changes the `using` directives of the migrations of phase 49 and the base namespace of the generator, so commit 1 builds. Commit 2 makes the `Initial` migrations again, and commit 4 makes them a third time for the JSON type. Commit 2 also makes the SQLite migrations of the host in `tests/MsgHatch.Data.EFCore.Tests.Unit` again.
- The test of invalid JSON is an `UPDATE` of a stored row, not an insert, because an insert needs each column of the row.
- The phase adds two integration tests on each server that the plan does not list: a database with no migrations gives `Errors.ValidationFailed` for the inbox, the outbox, and the registry settings, and a target id longer than its column gives `Errors.ValidationFailed`. The second test checks the wrap in `DbUpdateException`.
- The contract classes with the JSON option use databases of their own, and the subscription classes also a scenario fixture of their own, because two scenario fixtures on one database fail. A PostgreSQL test database of the subscription project has the name `subs_jsonb`, because `subscriptions_jsonb` gives more than 63 bytes. `MigratedStores` and `MigratedSubscriptionStores` of the 4 provider projects take the options of a database.
- The READMEs of `MsgHatch.Data.EFCore`, `MsgHatch.Subscriptions.Data.EFCore`, and the 2 store provider packages show a classifier of the host. The subscription provider packages change no code in commit 3, so their READMEs have no classifier.
- The README entries of 0.5.0 start with "Breaking:" for the names, the namespaces, the error types of the dialects, the dialect contract, and the JSON type. The option and the classifiers of the subscription store are entries with no "Breaking:".

Done when: the build has no errors and no warnings, all unit tests pass, `pwsh ./coverage.ps1` passes, `pwsh ./pack.ps1` passes with 40 packages of 0.5.0, and the EF Core integration projects pass on SQL Server and on PostgreSQL.

### Phase 51: Channel packages under MsgHatch.Channels

Goal: the 7 channel packages get the role segment `Channels` in their names, as the other package families have a role segment, and `AmazonSES` becomes `AmazonSes` (D324). Section 5.6 of the design gets the step that sets the baseline of the package validation after the first publish.

Starting point: the commits of phase 50. The channel packages are `MsgHatch.Email`, `MsgHatch.Email.AmazonSES`, `MsgHatch.Email.Resend`, `MsgHatch.SignalR`, `MsgHatch.Broker`, `MsgHatch.Broker.Wolverine`, and `MsgHatch.Broker.AmazonSns`, with 12 test projects. On 2026-11-24, 274 files of the repository name one of them: 72 in `src`, 169 in `tests`, 26 in `examples`, and 7 others. `pack.ps1`, `coverage.ps1`, and the workflow name no channel project. The solution folders `/src/Channels/` and `/tests/Channels/` already hold the channel projects. `Directory.Build.props` sets `EnablePackageValidation` with no baseline. No package is on nuget.org.

1. `git mv` the 7 project folders in `src` and the 12 test project folders in `tests` to the new names, with their project files. Each test project takes the name of its production project and its level, for example `tests/MsgHatch.Channels.Email.AmazonSes.Tests.Integration`.
2. Change each namespace and each `using` directive in `src`, `tests`, and `examples`. The class names do not change.
3. `MsgHatch.slnx`, each `ProjectReference`, and the labels of the item groups in `Directory.Packages.props` use the new names.
4. The READMEs:
   - The README of each renamed package, with its README tests.
   - The READMEs of `MsgHatch.Domain`, `MsgHatch.Templates`, and `MsgHatch.Templates.Liquid` name a channel package. They get the new names with no version entry, because their code does not change.
   - The root `README.md`, `docs/signalr-client.md`, and `examples/WolverineExample.Consumer/README.md`.
5. Git ignores the `appsettings.Local.json` files of the SES, Resend, Wolverine, and SNS integration projects, so `git mv` leaves them in the old folders. The owner moves them. The phase never reads or changes them, and it does not delete the old folders.
6. Section 5.6 of the design gets a step after the first publish: set `PackageValidationBaselineVersion` in `Directory.Build.props` to the published version, because `EnablePackageValidation` with no baseline checks no breaking change against an earlier version.
7. `VersionPrefix` becomes `0.6.0`, because the package ids and the namespaces change, and before 1.0.0 a breaking change raises the minor. The README of each renamed package gets the entry 0.6.0, which starts with "Breaking:" and names the old package id.
8. Describe the phase: section 4.2 and each other mention of an old name in the design, and `AGENTS.md` with the project table and the test commands.

Checks before the code:
- Each file that names an old package, project, or namespace, in `src`, `tests`, `examples`, `docs`, and the root folder.
- `examples/ExampleApp` and `examples/WolverineExample.Consumer` have a folder `Channels` with the namespaces `ExampleApp.Channels` and `WolverineExample.Consumer.Channels`. Inside these namespaces, a name that starts with `Channels.` resolves to the namespace of the example first. Each file of these folders builds with the new `using` directives.

Tests:
- The build has no errors and no warnings.
- All unit tests and the README tests pass.
- `pwsh ./pack.ps1` passes with 40 packages of 0.6.0 with the new ids.
- No file names an old package id, project, or namespace, except the "Breaking:" entries of the version history and the done phases of this roadmap, which record the history.

Check for the owner: move the 4 `appsettings.Local.json` files to the new folders, then run the SES, Resend, Wolverine, and SNS integration projects.

Commits:
1. "Rename the channel packages to MsgHatch.Channels"
2. "Describe the channel package names in the design and AGENTS.md"
3. "Record the commits and the checked facts of phase 51 in the plan"
4. "Record phase 51 as done in README and AGENTS.md"

The plan of this phase and D324 are in their own commit before commit 1.

Checked facts (2026-11-25), on Windows 11 build 26300 with the .NET SDK 10.0.404 and Git 2.56.0:

| Fact | Source |
|---|---|
| Before the code, 274 files named an old package, project, or namespace: 72 in `src`, 169 in `tests`, 26 in `examples`, 3 in `docs`, and `AGENTS.md`, `Directory.Packages.props`, `MsgHatch.slnx`, and `README.md`. | `git grep` of the old names |
| A `git mv` of a folder also moves the files of the folder that git ignores. So item 5 of the plan is wrong: a folder move of an integration project moves its `appsettings.Local.json` too. | A test with a tracked file and an ignored file in a scratch repository |
| Only 3 `appsettings.Local.json` files exist, of the SES, Resend, and Wolverine integration projects. The SNS integration project has none. | The untracked files of the old folders, listed by name only |
| One test used the partly qualified name `SignalR.SignalRChannel`, which resolved to the old namespace `MsgHatch.SignalR` from inside the namespace `MsgHatch.Subscriptions.Tests.Unit.Resolution`. The build failed with CS0246 and CS0103 until the name became `Channels.SignalR.SignalRChannel`. The files of the folders `Channels` of `examples/ExampleApp` and `examples/WolverineExample.Consumer` build with the new `using` directives. | `dotnet build` of the solution |
| A text replacement of the old names also changes the names of earlier packages that never get the new names: `MsgHatch.Email.Razor`, `MsgHatch.Email.Liquid`, and the namespace `MsgHatch.Email.Templates` in `README.md` and in D196, D204, D205, D215, and D216 of the design. The phase keeps these names. | `git grep` of each distinct new name |
| A folder that moved as a whole keeps its old `bin` and `obj`. The build of the new project does not remove the old assemblies, for example `MsgHatch.Email.dll`, and coverlet gives each of them a line coverage of 0 %. The first coverage run gave 93.8 %. With no `bin` and `obj` in the 15 folders that moved as a whole, it gives 98.3 %. 5 other integration and performance projects also had old assemblies in `bin`. | The assemblies of `artifacts/coverage/Summary.json`, and the second run of `pwsh ./coverage.ps1` |
| The build of the solution with `--artifacts-path` in a scratch folder has no errors and no warnings. The 40 unit test projects pass 6,527 tests and skip 1, the path limit test of the named pipe signal. The line coverage is 98.3 %. | `dotnet build`, `pwsh ./coverage.ps1` |
| `pwsh ./pack.ps1` passes with 40 packages of version 0.6.0, with the 7 new channel ids. The pack warning "packaging has been disabled" names only the 4 web examples, as in phase 50. | `pwsh ./pack.ps1` |
| The SES integration project passes 4 tests, and the Wolverine integration project passes 3 tests on the local container dev-rabbit. The Resend integration project fails 4 of 6 tests with `Resend: HTTP 401 validation_error`, as in phase 6, with the same `appsettings.Local.json` of 2026-11-02 23:44. So Resend refuses the API key of that file. | `dotnet test` of each project, on the word of the owner |
| No file names an old package id, project, or namespace, except the "Breaking:" entries of the 7 READMEs and the done phases of this roadmap. | `git grep` of the old names |

The phase adds no test, because it changes no behavior.

Checks that the phase did not do:
- The SNS integration project, which needs the AWS resources of the owner.
- A pass of the Resend integration project, which needs a new API key.
- A run on Linux before the push. The workflow run of the push builds and tests on Linux, with no integration project.

Commit 1 is `a4161f8`, and commit 2 `bc8730f`. The plan of this phase is in `5aa5df8`.

The implementation changed the plan in these points:
- The 4 integration projects moved file by file, because a folder move also moves the ignored files (checked fact). The other 15 folders moved as a whole.
- The owner: "yes, move the appsettings.Local.json files and cleanup orphaned project folders". So the phase moved the 3 files with no read, and removed the 4 old integration folders, which then held only build output.
- The owner: "run the SES, Resend and Wolverine integration tests and cleanup stale bin and obj folders". So the phase ran these 3 projects, and removed `bin` and `obj` of the 15 folders that moved as a whole and of the 5 projects with old assemblies.
- Section 5.3 of the design gets the naming rule of a channel package, because its example of the project names was `MsgHatch.Email.AmazonSES`.
- The section "Later phases" names the planned MassTransit client `MsgHatch.Channels.Broker.MassTransit`.

Done when: the build has no errors and no warnings, all unit tests pass, `pwsh ./coverage.ps1` passes, and `pwsh ./pack.ps1` passes with 40 packages of 0.6.0.

### Phase 52: MySQL packages of the EF Core stores

Goal: the new packages `MsgHatch.Data.EFCore.MySql` and `MsgHatch.Subscriptions.Data.EFCore.MySql` run the EF Core stores on MySQL 8.4 with the binary collation, the schema migrations, and a setup with one call (D325). On MySQL, the tables keep the prefix `MsgHatch` (D326). On each provider, the subscription key of the EF Core store moves into a hash column (D327).

Starting point: the commits of phase 51. The base packages run on each relational provider, and the SQL Server and PostgreSQL packages show a dialect and a migration set. MySQL has no `UPDATE ... RETURNING`, so the base claim runs. The base claim already sets the old due time first, because MySQL applies the assignments of an `UPDATE` from left to right (`docs/stores.md` section 4.4). The unique index `UX_Subscription_Owner` has 7 columns with 1,344 characters, and no MySQL index key can hold them (D327).

1. The base packages (D326): `IEntityFrameworkStoreDialect` gets the default property `bool HasSchemas => true`. With false, `StoreModel` puts the prefix `MsgHatch` on each table name and each history table name of both contexts, and uses no schema. With such a dialect, the startup validation fails when `Schema` is not the default `msghatch`, because the option has no effect. The SQL Server and PostgreSQL packages do not change.
2. The subscription key (D327):
   - `MsgHatchSubscriptionDbContext` gets the column `OwnerKey`, a binary column of 32 bytes. The unique index `UX_Subscription_Owner` has only this column.
   - The store calculates `OwnerKey` in C# before each insert and each update: the SHA-256 of `TenantId`, `MessageType`, `Level`, `CompanyId`, `UserId`, `Channel`, and `Recipients`. Each value goes into the hash as its byte count and then its UTF-8 bytes.
   - The new index `IX_Subscription_Type` on `TenantId` and `MessageType` is not unique, and serves `FindForDeliveryAsync`. Its key has at most 2,048 bytes on MySQL and 768 bytes on SQL Server, so it needs no column prefixes.
   - An add of a second subscription with the same key still gives the error of the unique key.
   - The SQL Server and PostgreSQL subscription packages make their `Initial` migrations again, as in phase 50. No package is on nuget.org.
3. The package `MsgHatch.Data.EFCore.MySql`:
   - It references `MsgHatch.Data.EFCore` and `Microting.EntityFrameworkCore.MySql` (D325). Its public API has no type of the provider, for example no `ServerVersion`.
   - Its dialect gives no claim statement, so the base claim runs: a LINQ read of the due candidates and one `ExecuteUpdate` with a claim token.
   - The default collation is `utf8mb4_bin`, so an id or a name is case-sensitive and sorts in binary order. MariaDB also has this collation.
   - The JSON type of the dialect is null, so each JSON column is `longtext`. The MySQL type `json` sorts the keys and drops duplicate keys, as `jsonb` does. So a host selects it only with the option `JsonColumnType` (D323).
   - The dialect maps the error numbers 1044, 1045, 1142, and 1143 to `Errors.Rejected`, and 1054, 1146, and 1406 to `Errors.ValidationFailed` (D322).
   - Its dialect gives `HasSchemas` false. So the tables are `MsgHatchInbox`, `MsgHatchOutbox`, `MsgHatchChannelState`, `MsgHatchChannelEntry`, and `MsgHatchMessageTypeEntry`, and the history table is `__MsgHatchStoreMigrations`, in the database of the host (D326).
   - It holds the migrations of `MsgHatchDbContext` for MySQL (D320).
   - `UseEntityFrameworkMySqlStores` registers the stores with the provider, the dialect, and the migrations in one call.
   - It has its `README.md`, its `Description`, its `PackageTags`, and its README tests. The README says why the package uses Microting (D325): the MIT license, EF Core 10, and MariaDB. The Oracle provider has the GPL-2.0 with the Universal FOSS exception, which limits a closed-source host that ships its binaries. Pomelo has no EF Core 10 build.
4. The package `MsgHatch.Subscriptions.Data.EFCore.MySql`:
   - It references `MsgHatch.Subscriptions.Data.EFCore` and `MsgHatch.Data.EFCore.MySql`, and uses the dialect of `MsgHatch.Data.EFCore.MySql`.
   - It holds the migrations of `MsgHatchSubscriptionDbContext` for MySQL, with the column `OwnerKey` of item 2.
   - The subscription table is `MsgHatchSubscription`, and its history table is `__MsgHatchSubscriptionMigrations` (D326).
   - `UseEntityFrameworkMySqlSubscriptionStore` registers the subscription store in one call.
   - It has its `README.md`, its `Description`, its `PackageTags`, and its README tests. The README gives the reason for the provider, as the README of item 3 does.
5. A time is `datetime(6)`, which keeps microseconds, as on PostgreSQL (D319).
6. `docker/mysql.compose.yml`: the container dev-mysql from `mysql:8.4` on 127.0.0.1:3306, with the password from `MYSQL_ROOT_PASSWORD` in `docker/.env`. The owner adds the password to `docker/.env`. The owner pulls the image.
7. Tests:
   - The new projects `tests/MsgHatch.Data.EFCore.MySql.Tests.Integration` and `tests/MsgHatch.Subscriptions.Data.EFCore.MySql.Tests.Integration` run the contract bases on MySQL with the dialect, on a database that the migrations of their package created. They also run their README tests.
   - The fixture `MySqlFixture` uses `MySqlIntegration:ConnectionString` of `appsettings.Local.json` (D180). With no connection string, Testcontainers starts the image `mysql:8.4`.
   - The two base integration projects also run the contract bases on MySQL with no dialect, in a folder `MySql`.
   - MySQL allows at most 64 characters in a database name, and the name of the subscription project has 58. So the name of a test database follows the PostgreSQL rule of D180: `msghatch_subscriptions_data_efcore_mysql__` has 42 characters.
   - Each test database has a pool of at most 10 connections, because MySQL allows 151 connections by default.
8. Packages: `Microting.EntityFrameworkCore.MySql` `10.*`, because its 11.x line needs .NET 11, and `Testcontainers.MySql` `4.*` for the tests.
9. `VersionPrefix` becomes `0.7.0`, because the new packages are a new feature. Version entries:
   - The README of `MsgHatch.Data.EFCore` gets the entry 0.7.0 for `HasSchemas`, and shows the setup with the MySQL package.
   - The READMEs of `MsgHatch.Subscriptions.Data.EFCore`, `MsgHatch.Subscriptions.Data.EFCore.SqlServer`, and `MsgHatch.Subscriptions.Data.EFCore.PostgreSql` get the entry 0.7.0. It starts with "Breaking:", and names the column `OwnerKey` and the new `Initial` migrations.
10. `examples/SchemaMigrator` also applies the MySQL migrations, and writes their script (`SchemaMigrator__Provider` `MySql`).
11. Describe the phase: the design (sections 4.2, 4.5, 5.1, and 5.6, and D178), the EF Core section of `docs/stores.md`, `AGENTS.md` with the commands and the settings of the MySQL tests, and the count of 42 packages.

Checks before the code:
- The exception type of the Microting provider (`MySqlConnector.MySqlException`), and how the provider treats a schema.
- How the setup gets the server version with no provider type in the API of MsgHatch.
- How the provider maps a text column with `IsUnicode(false)`, and whether the collation `utf8mb4_bin` fits its character set.
- The restore takes a 10.x version of `Microting.EntityFrameworkCore.MySql` with EF Core 10.
- MySQL 8.4 has no `UPDATE ... RETURNING`, so a dialect claim statement cannot return the claimed rows.
- The provider cuts a time down to the microsecond, and does not round it up. The MySQL server rounds a time with more fractional digits than the column, unless the SQL mode has `TIME_TRUNCATE_FRACTIONAL`. A rounded-up due time delays its claim.
- The MySQL type `json` sorts the keys of an object (the reason for the JSON type in item 3).
- The key length of each index of both contexts in `utf8mb4` against 3,072 bytes. On 2026-11-24, only `UX_Subscription_Owner` is over the limit.
- The image tag `mysql:8.4` exists, with `docker manifest inspect` and no pull.

Tests:
- The contract bases pass on MySQL with the dialect and with no dialect.
- On MySQL, each table name and each history table name of both contexts starts with `MsgHatch`, and no table is in another database. On SQLite, a test dialect with `HasSchemas` false gives the same names, and a `Schema` that is not the default fails the startup validation (D326).
- A subscription with each key column at its maximum length is stored on SQL Server, PostgreSQL, and MySQL, and a second add with the same key fails (D327). Before the hash column, the insert fails on SQL Server, because the key has 2,112 bytes and SQL Server allows 1,700 (D178).
- Two keys that differ only at the border of two values give two subscriptions, for example `UserId` `ab` with `Channel` `c`, and `UserId` `a` with `Channel` `bc` (D327).
- Two tenants that differ only in case stay apart with `utf8mb4_bin`.
- A time with a part of a microsecond comes back cut down to the microsecond (D319).
- The migrations create the schema of each package on an empty MySQL database, and a second call changes nothing. The model of each context has no pending change against its migrations, on each provider.
- The dialect gives the error type of each number of item 3.
- `examples/SchemaMigrator` applies the MySQL migrations on an empty database, and writes their script.
- The setup with the two MySQL packages passes the startup validation.
- The README snippets.
- Each new test fails first.

Check for the owner: start dev-mysql, and put `MySqlIntegration:ConnectionString` into the `appsettings.Local.json` files. Then run the EF Core integration projects on SQL Server, PostgreSQL, and MySQL.

Commits:
1. "Add the MySQL compose file for the local tests"
2. "Keep the EF Core tables of a database with no schemas under the prefix MsgHatch"
3. "Keep the subscription key of the EF Core store in a hash column"
4. "Add the MySQL package of the EF Core stores with its migrations"
5. "Add the MySQL package of the EF Core subscription store"
6. "Test the base EF Core stores on MySQL"
7. "Describe the MySQL packages in the design and the store guide"
8. "Record the commits and the checked facts of phase 52 in the plan"
9. "Record phase 52 as done in README and AGENTS.md"

The plan of this phase and D325 to D327 are in their own commit before commit 1.

Checks that the phase does not do: a run on MariaDB, and a run of the MySQL tests through Testcontainers.

Done when: the build has no errors and no warnings, all unit tests pass, `pwsh ./coverage.ps1` passes, `pwsh ./pack.ps1` passes with 42 packages of 0.7.0, and the EF Core integration projects pass on SQL Server, PostgreSQL, and MySQL.

### Later phases

These items are on the roadmap with no detailed plan yet. Each one becomes its own phase with the same rules:
- A template store in a database, for example SQLite, EF Core, or MongoDB, with `ITemplateWriter` (D218), so that a written template survives a deployment that replaces the template folder.
- Partials of the Liquid engine (D208): `render` and `include` read a partial from the template store, with the lookups of the tenant, the company, and the language, through the asynchronous `ITemplateFileProvider` of the first stable Fluid 3.
- A general block builder for HTML email templates, as an editor package that knows no engine (D240), for example `MsgHatch.Templates.Builder.AdminUI`:
  - Layout blocks (section, columns, text, button, image, and spacer) and data blocks (variable, repeat, and condition). The builder writes email HTML with tables and inline styles.
  - Each engine package gives a syntax adapter that writes and reads its data blocks: Liquid with `{{ Name }}`, `{% for %}`, and `{% if %}`, and the placeholder engine with `{{Name}}` only. The placeholder engine has no repeat and no condition, so the builder hides those blocks for it.
  - The builder stores no block model. It reads the model again from the template text: each block has a marker, for example a `data-msghatch-block` attribute, and the adapter reads the data blocks. A region with no valid marker becomes a raw HTML block, so a hand edit of the text does not break the other blocks.
  - The variable names come from the server, for example from the sample payload of D254, with no reflection that breaks AOT (D237).
  - The edit page offers the builder and the raw editor for one part, so the extension point of D240 changes from one editor for each engine to a choice of editors.
  - The markers are needed only in the text of the template store. An email client can remove them from a sent email, because the builder never reads a sent email.
  - The phase checks the license of each JavaScript library that it uses.
- A host-context mode of the EF Core stores, next to the current mode with a context of their own. The host maps the MsgHatch tables in its own `DbContext` with a model builder extension, for example `modelBuilder.MapMsgHatch()`, and registers the stores with `UseEntityFrameworkStores<AppDbContext>()`. This is composition, as in the EF Core outbox of MassTransit, not a subclass as in `IdentityDbContext`, because a class has only one base class:
  - Gain: an add to the inbox can join the transaction of the host, which is the transactional outbox pattern.
  - Cost: the host owns the migrations, so the migrations of the provider packages (D320) do not apply in this mode.
  - Cost: the stores are singletons, so they need `IDbContextFactory<AppDbContext>`.
  - Cost: the phase changes D11, which needs no shared transaction.
- A dispatch signal with PostgreSQL `LISTEN` and `NOTIFY`, after the PostgreSQL package of phase 49 (D102, D188). Phases 39 and 40 add the signals of MongoDB and RabbitMQ.
- SMS and push channels.
- Batched try records: one store call records the tries of several sent messages, for more throughput on one channel.
- More direct broker clients, one for each SDK, with the same pattern as SNS: `Azure.Messaging.ServiceBus`, `Confluent.Kafka`, and `RabbitMQ.Client`.
- More framework clients: Rebus, SlimMessageBus, and `MsgHatch.Channels.Broker.MassTransit` on MassTransit 8 (section 6). SlimMessageBus is a light publish abstraction with transport plugins. Its phase first checks that the client uses no outbox of SlimMessageBus.
- AOT for the other packages (D237): each package outside the API boundary sets `IsAotCompatible` when its dependencies allow it, or the design records why not.
