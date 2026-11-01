# MsgHatch roadmap

The roadmap holds the order of the phases of MsgHatch, and each phase with its items, tests, and commits. The design `docs/design.md` holds the requirements, terms, decisions, architecture, and conventions. In this document, a section number refers to a section of the design, for example "section 4.5", unless the text names another document. A decision number refers to a decision of the design, for example D122.

## Order

Each phase builds on the phases of its row. Phases with no dependency between them can run in any order, but the numbers give the default order:

| Phase | Needs |
|---|---|
| 1 Core to the outbox | 0 |
| 2 Dispatch | 1 |
| 3 Email composition | 1 |
| 4 Email delivery with Amazon SES | 2, 3 |
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

1. Add `Delivery/ITransport.cs` and `TransportLoggingWrapper` (D129). Add `PublishOutcome.cs` with no `Cancelled`, `PublishTry.cs`, and `PublishReceipt.cs` (section 4.4), and the internal `RetrySchedule` with the next try time of section 4.14.
2. Add `Dispatch/ClaimResult.cs`, `ChannelDownReason.cs`, and `ChannelDown.cs`. Add the members `ClaimDueAsync`, `RecordTryAsync`, and the members of the down state of a channel (D106) to `IOutboxDispatchStore`. Add them to `InMemoryOutboxStore`, and their use cases to `OutboxStoreTestsBase` (section 5.5).
3. Add `Inbox/IInboxStore.cs` with `AddAsync`, and `Inbox/IInboxDispatchStore.cs` with `ClaimDueAsync`, `CompleteAsync`, `FailAsync`, and `DeleteTestDataAsync`. Add `InboxStoreLoggingWrapper`, `InboxDispatchStoreLoggingWrapper`, `UseInboxStore`, `UseInboxDispatchStore`, and `InMemoryInboxStore` with its two parts, which `UseInMemoryStores` now also registers. Add `INotificationService.AcceptAsync` (section 4.8, step 1). Add the contract base class `InboxStoreTestsBase<TStore>` with `IInboxStoreTestData`, and its subclass `InMemoryInboxStoreUnitTests`.
4. Add `Dispatch/DispatchOptions.cs` with `DispatchOptionsValidator`, `DispatchSummary.cs`, `DispatchTopic.cs`, `IDispatchSignal.cs`, `InProcessDispatchSignal`, `UseDispatchSignal`, the internal dispatch loop with its work queue (section 4.9), and the notify of step 6 of section 4.8. Add the contract base class `DispatchSignalTestsBase<TSignal>`, and its subclass `InProcessDispatchSignalUnitTests`. A later signal between processes gets the same tests with one subclass (D102).
5. Add `IInboxProcessor`, `IOutboxDispatcher`, the internal `InboxProcessor` and `OutboxDispatcher` with one dispatch loop for each published channel (D101), their logging wrappers, `ConfigureInboxDispatch`, and `ConfigureOutboxDispatch`.
6. Add `Dispatch/ChannelDownOptions.cs` with `ChannelDownOptionsValidator`, `ConfigureChannelDown`, and the down state of the channels in the outbox loops (section 4.9, D86, D106).
7. Add `TestDeliveryOptions` with its validator, `ConfigureTestDelivery`, and the test delivery of section 4.8. Add `MsgHatchOptions.Roles` with `MsgHatchRoles`, and the startup validation of the roles (D105).
8. Add the `send` span, the `process` span of the inbox processor with its link, the `TraceParent` of `AcceptAsync`, and the instruments `msghatch.publish.tries`, `msghatch.messages.failed`, `msghatch.send.duration`, `msghatch.delivery.delay`, `msghatch.queue.length`, and `msghatch.channel.down` (section 4.21).

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
2. "Add claims, publish tries, and channel down states to the outbox dispatch store"
3. "Add the inbox stores, the in-memory inbox, and AcceptAsync"
4. "Add the dispatch signal and the dispatch loops"
5. "Add the inbox processor and the outbox dispatcher"
6. "Add the shared down state of a channel"
7. "Add test delivery and host roles"
8. "Add dispatch telemetry"

Done when: the build has no errors and no warnings, all tests pass, and the test with two DI containers sends an event through the test transport.

### Phase 3: email composition

Goal: flow steps 3 to 5 for email. The rendered emails are in the in-memory outbox. No email is sent yet.

1. Add `src/MsgHatch.SourceGeneration` with the generator of section 4.10, and `tests/MsgHatch.SourceGeneration.Tests.Unit`.
2. Add `src/MsgHatch.Email` with `EmailChannel`, `EmailChannelSettings`, `EmailMessage`, and `RenderedEmail` (section 4.4), and `tests/MsgHatch.Email.Tests.Unit`.
3. Add `Engines/EmailTemplateRequest.cs`, `IEmailTemplateEngine.cs`, `IEmailTemplate.cs`, and `EmailTemplateEngineLoggingWrapper<TPayload>`.
4. Add the email composer `EmailComposer<TEvent, TPayload> : IEnvelopeComposer<TEvent, EmailMessage>` (section 4.8, step 4). It calls the payload loader of its registration and the template engine.
5. Add `Templates/TemplateKey.cs`, `TemplateLookup.cs`, `TemplatePartKind.cs`, `TemplatePart.cs`, `TemplateSource.cs`, `ITemplateStore.cs`, the internal `TemplateSelector` with the fallback of D123, and `TemplateStoreLoggingWrapper`.
6. Add the internal `TemplateParser`, `ParsedTemplate`, and `ParsedTemplateCache<TPayload>`.
7. Add `Placeholders/PlaceholderPayloadAttribute.cs`, `IPlaceholderPayload.cs`, and `IPlaceholderWriter.cs`.
8. Add the internal `PooledCharBufferWriter`, the internal `PlaceholderRenderer`, and `PlaceholderEmailEngine<TPayload>`. Add the contract base class `EmailTemplateEngineTestsBase<TEngine>` with `IEmailTemplateTestData` in `tests/MsgHatch.Email.Tests.Unit/Contracts/`, and its subclass `PlaceholderEmailEngineUnitTests`.
9. Add `FileTemplateOptions` with its validator, `FileSystemTemplateStore`, and `EmbeddedResourceTemplateStore`, each with the cache of its sources (section 4.10). Add the contract base class `TemplateStoreTestsBase<TStore>` with `ITemplateStoreTestData`, and its subclasses `FileSystemTemplateStoreUnitTests` and `EmbeddedResourceTemplateStoreUnitTests`.
10. Add `UseTemplateStore`, `UseTenantTemplateStore`, `UseEmbeddedTemplates`, `UseFileTemplates`, and the two `AsEmail` overloads (section 4.7). Add the startup validation rule for the template store. Add `EmailChannelSettingsValidator`, which the first `AsEmail` registers with `AddChannelType`.
11. Add the `MsgHatch.Email` package content: the generator assembly under `analyzers/dotnet/cs`.

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
- The standard logging wrapper tests for the engine and template store wrappers. A log record never contains an email address or a subject.
- End to end, with `UseInMemoryStores()`, embedded templates, and a test router: `ProcessAsync` stores a rendered email in the in-memory outbox.

Commits:
1. "Add the placeholder payload source generator"
2. "Add the email channel types"
3. "Add the email template engine contract and the email composer"
4. "Add template stores, selection, and parsing"
5. "Add the placeholder engine"
6. "Add email registration"

Done when: the build has no errors and no warnings, all tests pass, and the end-to-end test finds a rendered email in the in-memory outbox.

### Phase 4: email delivery with Amazon SES

Goal: the email publisher sends the emails of the outbox through `IEmailClient`, with Amazon SES as the first client.

1. Add `IEmailClient` (section 4.6), `EmailClientLoggingWrapper`, `EmailTransport : ITransport`, and `UseEmailClient<TClient>` with its factory overload (D129). With a `redirectTo` value, the email transport replaces `To` with it, and removes `Cc` and `Bcc`. Add the contract base class `TransportTestsBase<TTransport>` with `ITransportTestDriver` in `tests/MsgHatch.Domain.Tests.Unit/Contracts/`, and its first subclass `EmailTransportUnitTests`.
2. Add `src/MsgHatch.Email.AmazonSES` with `AmazonSesOptions` and its validator (section 4.4), `AmazonSimpleEmailServiceClient : IEmailClient`, and `UseAmazonSes` (section 4.7). Add `tests/MsgHatch.Email.AmazonSES.Tests.Unit`, the contract base class `EmailClientTestsBase<TClient>` with `IEmailClientTestDriver` in `tests/MsgHatch.Email.Tests.Unit/Contracts/`, and its subclass `AmazonSimpleEmailServiceClientUnitTests`:
   - `UseAmazonSes` registers `IAmazonSimpleEmailServiceV2` with `TryAddSingleton`, as an `AmazonSimpleEmailServiceV2Client` for `AmazonSesOptions.Region`, or for the region of the AWS SDK configuration when `Region` is null.
   - A send calls `SendEmailAsync` one time, and returns the `MessageId` of the response in the receipt. The client writes the outgoing message id into the email tag `msghatch-id`.
   - The client converts each exception into an `Error`, and does not throw. `TooManyRequestsException` and `LimitExceededException` give `Errors.RateLimited`. `SendingPausedException`, `AccountSuspendedException`, `HttpRequestException`, and an HTTP 5xx response give `Errors.Unavailable`. The account-level errors log at the Critical level, because an administrator must act. `MessageRejectedException`, `BadRequestException`, `MailFromDomainNotVerifiedException`, and `NotFoundException` give `Errors.Rejected`.
   - `CheckHealthAsync` calls `GetAccountAsync`. It gives `Errors.Unavailable` when the response has `SendingEnabled` false, because SES then refuses each send, and the channel must stay down (D106). First check the property name in the package version.
3. Add the startup validation rules of these packages.

Tests:
- The use cases of `TransportTestsBase`, run by `EmailTransportUnitTests` with an `IEmailClient` substitute as the driver: a success gives the receipt of the provider, a transient error and a permanent error come back as values, and a cancelled token gives `Errors.OperationCancelled`.
- `EmailTransport` gives the `EmailMessage` of `OutgoingMessage.Content` and the outgoing message id to `IEmailClient`. With a `redirectTo` value, it replaces `To` and removes `Cc` and `Bcc`.
- The use cases of `EmailClientTestsBase`, run by `AmazonSimpleEmailServiceClientUnitTests` with an `IAmazonSimpleEmailServiceV2` substitute as the driver.
- Tests unique to SES: one data-driven row for each SES exception class checks the `ErrorType` code. `CheckHealthAsync` gives null for `SendingEnabled` true, and `Errors.Unavailable` for `SendingEnabled` false and for an `HttpRequestException`. The sender address rule, with and without a sender name, and with an address that already contains `<` and `>`.
- The options tests of `AmazonSesOptions`, with the rows of section 5.5. `SenderEmail` gets null, empty, only whitespace, a value with no `@`, and a value with an accented character. `SenderName` gets the lengths 99, 100, and 101. `Region` gets null, `"eu-west-1"`, and `"EU-WEST-1"`.
- `UseAmazonSes` alone registers all parts: `IEmailClient` resolves as its logging wrapper around `AmazonSimpleEmailServiceClient`, and `IAmazonSimpleEmailServiceV2` resolves. An `IAmazonSimpleEmailServiceV2` that the test registered first stays.
- The standard logging wrapper tests for the email client wrapper. A log record never contains an email address or a subject.
- A publisher host with only `UseAmazonSes` sends only the email channel. The other messages of the same outbox stay pending (D101).

Commits:
1. "Add the email transport and the email client contract"
2. "Add the Amazon SES email client"

Done when: the build has no errors and no warnings, and all tests pass.

### Phase 5: SignalR channel

Goal: real-time UI updates, composed by the event consumer and sent by the SignalR publisher.

1. Add `src/MsgHatch.SignalR` with `SignalRChannel`, `SignalRChannelSettings`, `SignalRChannelSettingsValidator`, the SignalR composer, `SignalRTransport<THub> : ITransport where THub : Hub`, `UseSignalR<THub>`, and `MessageBuilder<TEvent>.AsSignalR<TPayload, TLoader>`. The first `AsSignalR` registers the channel with `AddChannelType`. The settings validator accepts each settings value, because the subscription manager checks the language (section 4.17). Add `tests/MsgHatch.SignalR.Tests.Unit` with `SignalRTransportUnitTests`, a subclass of `TransportTestsBase`.
2. The composer calls the payload loader of its registration, and gives each payload as the envelope (D17).
3. The transport sends `OutgoingMessage.Content` with the message type name as the method name:
   - For a user target, to `Clients.User(SignalRUsers.ForUser(tenant, user))`, with the tenant of the outgoing message. The text is the user id with no tenant, and `{TenantId}:{UserId}` with a tenant. The host's `IUserIdProvider` must give the same text for each connection (D114).
   - For a company target, to `Clients.Group(SignalRGroups.ForCompany(tenant, company))`. The group name is `msghatch:company:{CompanyId}` with no tenant, and `msghatch:{TenantId}:company:{CompanyId}` with a tenant. The host's hub adds each connection to the group of the company of its user, for example in `OnConnectedAsync`.
   - The transport returns a receipt with no provider id. SignalR has no delivery guarantee, so "sent" means that SignalR accepted the message.
4. With a `redirectTo` value, the transport sends to the SignalR group `redirectTo` in place of the target.

Tests:
- The use cases of `TransportTestsBase`, run by `SignalRTransportUnitTests`.
- The transport calls `Clients.User` and `Clients.Group` with the names of item 3, with and without a tenant, each with the method name, on an `IHubContext<THub>` substitute.
- `AsSignalR` adds the content type `SignalR:{name}` to the registry. With no retry configuration, a SignalR message gets an `ExpiresAtUtc` 5 minutes after the fake time (D94).
- With a `redirectTo` value, the transport sends to that group.
- `UseSignalR<THub>` alone registers one `ITransport` for the SignalR channel, for each payload type, and `IHubContext<THub>`.

Commits:
1. "Add the SignalR channel"

Done when: the build has no errors and no warnings, and all tests pass.

### Phase 6: broker channel with Wolverine

Goal: broker messages, composed by the event consumer and sent by the broker publisher through Wolverine (D134).

1. Add `src/MsgHatch.Broker` with `BrokerChannel`, `BrokerChannelSettings`, `BrokerChannelSettingsValidator`, `IBrokerEnvelope`, `BrokerEnvelope<TPayload>`, the broker composer, `BrokerPublish`, `IBrokerClient`, `BrokerClientLoggingWrapper`, `BrokerTransport : ITransport`, `UseBrokerClient<TClient>` with its factory overload, and `MessageBuilder<TEvent>.AsBroker<TPayload, TLoader>`. The first `AsBroker` registers the channel with `AddChannelType`. The settings validator rejects a header with an empty name, and a header name that starts with `msghatch-`, because the library uses these names. Add `tests/MsgHatch.Broker.Tests.Unit` with `BrokerTransportUnitTests`, a subclass of `TransportTestsBase`, and the contract base class `BrokerClientTestsBase<TClient>` with `IBrokerClientTestDriver` in its `Contracts/` folder.
2. The composer calls the payload loader of its registration. The broker transport builds the headers: the headers of the channel settings, `msghatch-id` with the outgoing message id, `msghatch-tenant` for a message of a tenant, `msghatch-test: true` for a test message, and `msghatch-redirect` with the `redirectTo` value. Then it calls `IBrokerClient.PublishAsync` (section 4.6). So each client only maps these values to its library, and a receiver can find duplicates by `msghatch-id`.
3. Add `src/MsgHatch.Broker.Wolverine` with `WolverineBrokerClient : IBrokerClient` and `UseWolverineBroker` (section 4.7). Add `tests/MsgHatch.Broker.Wolverine.Tests.Unit` with `WolverineBrokerClientUnitTests`, a subclass of `BrokerClientTestsBase`:
   - The client calls `IMessageBus.PublishAsync` with the payload and a `DeliveryOptions` with the headers and the tenant id. It returns a receipt with no provider id.
   - First check that `PublishAsync` with a payload of the static type `object` routes by the run-time type of the payload, in the pinned version. If it does not, the client calls the generic method through one cached delegate for each payload type.
   - First check which headers Wolverine uses for the trace context, and add the result to the checked facts of section 4.21.
   - The client converts each exception into an `Error`, and does not throw. `CheckHealthAsync` returns null, because Wolverine has no general health check of a transport.
4. Add the startup validation rule of `UseWolverineBroker` (section 4.7).

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
| CAP is an event bus with its own outbox. Its project has the MIT license. The license page of the core package `DotNetCore.CAP` was not read. | [CAP project](https://awesome.ecosyste.ms/projects/github.com%2Fdotnetcore%2Fcap) |

Tests:
- The use cases of `TransportTestsBase`, run by `BrokerTransportUnitTests` with an `IBrokerClient` substitute as the driver.
- The broker transport gives the headers of item 2 to the client: with and without a tenant, for a test message, and with a `redirectTo` value. Two payload types go through the same transport.
- With an `ActivityListener`, the client calls `PublishAsync` while the `send` span is `Activity.Current` (section 4.21).
- The use cases of `BrokerClientTestsBase`, run by `WolverineBrokerClientUnitTests` with an `IMessageBus` substitute as the driver: the payload with its run-time type, the headers, the tenant id, and an exception that becomes an `Error`.
- `AsBroker` adds the content type `Broker:{name}` to the registry. With no retry configuration, a broker message has no `ExpiresAtUtc` (D94).
- The settings validator rejects an empty header name and the name `msghatch-x`, and accepts `x-source`.
- The standard logging wrapper tests for the broker client wrapper.
- `UseWolverineBroker` alone registers the `IBrokerClient` and one `ITransport` for the broker channel.

Commits:
1. "Add the broker channel and the broker client contract"
2. "Add the Wolverine broker client"

Done when: the build has no errors and no warnings, and all tests pass.

### Phase 7: JSON file stores

Goal: durable inbox and outbox stores with no database (D26).

1. Add `src/MsgHatch.Data.FileSystem` with `FileStoreOptions` and its validator, `FileSystemInboxStore` and `FileSystemOutboxStore` with their two parts (section 4.5), and `UseFileStores`. Add `tests/MsgHatch.Data.FileSystem.Tests.Unit` with the subclasses `FileSystemInboxStoreUnitTests` and `FileSystemOutboxStoreUnitTests`. The two stores share one internal folder helper:
   - A received message is the file `{status}/{nextTryAtUtc:yyyyMMddHHmmssfff}_{id}.json`, written with `MsgHatchJson`. A received message of a tenant has the escaped tenant id after its id in the file name, so two tenants can give the same id. An outgoing message is the file `{channel}/{status}/{nextTryAtUtc:yyyyMMddHHmmssfff}_{id}.json`, so each channel has its own folder (D104). The status folder is `pending`, `claimed`, `done`, or `failed`. The time prefix lets a claim sort the files by name, without a read of each file. The tenant is in the JSON of the message.
   - `AddAsync` first creates the marker file `ids/{tenantFolder}/{id}` with `FileMode.CreateNew`. If the marker exists, the store ignores the message.
   - `ClaimDueAsync` moves each due file from `pending` to `claimed` with `File.Move`, with the lease end as its new time prefix, and writes the lease end into the file as `NextTryAtUtc` and `LeaseUntilUtc` (D103). The first dispatcher that moves a file wins, and the others skip that file. A file in `claimed` whose time prefix is at or before now has an ended lease, so the claim takes it again in the same way. `NextDueAtUtc` is the earliest time prefix in `pending` and in `claimed`, so it includes the lease ends.
   - The down state of a channel is the file `{channel}/down.json`. `SetChannelDownAsync` with the reason `Errors` creates it with `FileMode.CreateNew`, so the first loop wins. The other changes of the down state open the file with `FileShare.None` while they read and write it.
   - `RecordTryAsync` reads the file, and changes nothing when its `TryCount` is not the try number minus 1. Else it adds the try to the `tries` array and writes the new delivery state into a temporary file. Then it replaces the old file with `File.Move`, in the folder of the new status.
   - The tenant store of a key reads the tenant from the JSON of each message, and skips the messages of the other tenants.
   - The stores have the comment `ponytail: rename-based claims suit one machine or a shared folder with atomic rename, use a database store for more throughput`.

Tests:
- The use cases of `InboxStoreTestsBase` and `OutboxStoreTestsBase` (section 5.5) run on the two file stores, each in its own temporary folder.
- The options tests of `FileStoreOptions`. Configuration binding: a value in the section `MsgHatch:FileStores` reaches `FileStoreOptions`, and a value from the `Action<FileStoreOptions>` of the setup method wins over it.
- One call of `UseFileStores` registers `IInboxStore`, `IOutboxStore`, `IInboxDispatchStore`, and `IOutboxDispatchStore`, each resolved as its logging wrapper around the store of the package. The tenant store of a key and the dispatch store use one store instance.
- Tests unique to the file stores: a permanent error moves the file to the `failed` folder of its channel, and a second store instance on the same folder reads the data of the first one.

Commits:
1. "Add JSON file stores for the inbox and the outbox"

Done when: the build has no errors and no warnings, and all tests pass.

### Phase 8: SQLite stores

Goal: relational inbox and outbox stores in process (D55).

1. Add `src/MsgHatch.Data.Sqlite` with `SqliteStoreOptions` and its validator, `SqliteInboxStore` and `SqliteOutboxStore` with their two parts, the internal schema helper that runs one time for each connection string, and `UseSqliteStores` (section 4.5). Add `tests/MsgHatch.Data.Sqlite.Tests.Unit` with the subclasses `SqliteInboxStoreUnitTests` and `SqliteOutboxStoreUnitTests`.
2. First check the busy wait of the pinned `Microsoft.Data.Sqlite` version: a command on a locked database tries again until its command timeout ends (section 4.5).

Tests:
- The use cases of `InboxStoreTestsBase` and `OutboxStoreTestsBase` run on the two SQLite stores, each test on its own database file in its own temporary folder.
- The options tests of `SqliteStoreOptions`. A connection string with `{tenant}` fails, because one database for each tenant has its own setup method (D125).
- One call of `UseSqliteStores` registers `IInboxStore`, `IOutboxStore`, `IInboxDispatchStore`, and `IOutboxDispatchStore`, each resolved as its logging wrapper around the store of the package. The tenant store of a key and the dispatch store use one store instance.
- `SqliteOutboxStoreUnitTests`, unique to SQLite:
  - The stores write times as ISO 8601 text and enum values as text, and an empty text for no tenant. The same message id with two tenants gives two inbox rows.
  - A second store instance on the same database file reads the data of the first one. The connection string comes from the test configuration, bound to `SqliteStoreOptions`.
  - A claim with nothing due takes no write lock: it completes while a second connection holds a write transaction (section 4.5).
  - `RecordTryAsync` with a try number that is already recorded changes no row, and the tries stay one JSON array (D27).
  - A second writer completes after the first writer commits, within the command timeout.

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

Tests:
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
- The use cases of `SubscriptionStoreTestsBase`, run by `InMemorySubscriptionStoreUnitTests`, with the store examples S1 to S12 of `docs/subscription-domain.md`.
- Subscription management tests, with the examples M1 to M22 of `docs/subscription-domain.md`, on the in-memory store and a substitute for `IRecipientDirectory`:
  - `GetChannelsAsync` lists each registered channel with its features.
  - `GetMessageTypesAsync` gives the applied subscriptions with their levels for a user, for a company, and with no filter, and only the message types with a scope function. A user subscription that a fixed subscription hides is not in the result. An opt-out that applies is in the result.
  - Each validation rule of section 4.17 gives `Errors.ValidationFailed`, and the store does not change. A named user of another company is one of these rules, with the company from the directory substitute.
  - Each conflict rule of section 4.17 gives `Errors.Conflict`. A draft that is invalid and also conflicts gives `Errors.ValidationFailed`.
  - A valid `CreateAsync` gives revision 1, a `Guid` version 7 id, and the fake time as `CreatedAtUtc` and `LastModifiedAtUtc`. A valid `UpdateAsync` gives the next revision.
- The standard logging wrapper tests for `SubscriptionStoreLoggingWrapper`, `RecipientDirectoryLoggingWrapper`, and `SubscriptionManagerLoggingWrapper`. No log record contains an email address of a test profile.

Commits:
1. "Add the subscription model and the recipient directory contract"
2. "Add subscription resolution with merge rules"
3. "Add the subscription router and its setup"
4. "Add the in-memory subscription store and its contract test base class"
5. "Add the subscription management API"

Done when: the build has no errors and no warnings, all tests pass, and the examples P1 to P7 run end to end on the in-memory stores.

### Phase 10: subscription stores

Goal: durable subscription stores in files and in SQLite.

1. Add `src/MsgHatch.Subscriptions.Data.FileSystem` with `FileSystemSubscriptionStore` on the `FileStoreOptions` of `MsgHatch.Data.FileSystem` (section 4.5, D59), and `UseFileSubscriptionStore`. Add `tests/MsgHatch.Subscriptions.Data.FileSystem.Tests.Unit` with `FileSystemSubscriptionStoreUnitTests`.
2. Add `src/MsgHatch.Subscriptions.Data.Sqlite` with `SqliteSubscriptionStore` on the `SqliteStoreOptions` of `MsgHatch.Data.Sqlite` (section 4.5, D59), and `UseSqliteSubscriptionStore`. Add `tests/MsgHatch.Subscriptions.Data.Sqlite.Tests.Unit` with `SqliteSubscriptionStoreUnitTests`.

Tests:
- The use cases of `SubscriptionStoreTestsBase` run on the file store and on the SQLite store, with the store examples S1 to S12. The shared test scenario of section 4 of `docs/subscription-domain.md` is seeded one time as a named scenario, for a group of tests that only read.
- `UseSqliteSubscriptionStore()` with no argument uses the `SqliteStoreOptions` that `UseSqliteStores` sets. A connection string in both `UseSqliteStores` and `UseSqliteSubscriptionStore` fails the startup validation, and the message names both methods (D62).
- `FileSystemSubscriptionStoreUnitTests`: a subscription for the user `"auth0|abc"` is stored under an escaped folder name, and is found again.
- `SqliteSubscriptionStoreUnitTests`:
  - A second default subscription with the same message type, channel, and recipient kind gives `Errors.Conflict`, although the default level has no company and no user.
  - The store and `SqliteOutboxStore` share one database file, and each keeps its own schema version.

Commits:
1. "Add the JSON file subscription store"
2. "Add the SQLite subscription store"

Done when: the build has no errors and no warnings, and all tests pass.

### Phase 11: caching

Goal: the subscription cache, with HybridCache and FusionCache as backends (D38, D39).

1. Add `ISubscriptionCache`, `ISubscriptionCacheBackend`, `SubscriptionCacheOptions` with `SubscriptionCacheOptionsValidator`, `SubscriptionStoreCachingWrapper`, and `UseSubscriptionCache<TBackend>` with its factory overload to `MsgHatch.Subscriptions` (section 4.15). `UseSubscriptionCache` registers the caching wrapper and `ISubscriptionCache` as tenant services. Add the contract base class `SubscriptionCacheBackendTestsBase<TBackend>` with `ISubscriptionCacheBackendTestData` in `tests/MsgHatch.Subscriptions.Tests.Unit/Contracts/`.
2. Add `src/MsgHatch.Subscriptions.Caching.Hybrid` with `HybridCacheSubscriptionBackend` and `UseHybridSubscriptionCache` (sections 4.7 and 4.15). First check the names of the `HybridCacheEntryFlags` values in the package version. Add `tests/MsgHatch.Subscriptions.Caching.Hybrid.Tests.Unit` with `HybridCacheSubscriptionBackendUnitTests`.
3. Add `src/MsgHatch.Subscriptions.Caching.FusionCache` with `FusionCacheSubscriptionBackend` and `UseFusionSubscriptionCache`. The backend gets its cache from `IFusionCacheProvider.GetCache("MsgHatch.Subscriptions")`. Add `tests/MsgHatch.Subscriptions.Caching.FusionCache.Tests.Unit` with `FusionCacheSubscriptionBackendUnitTests`.

Tests:
- Caching wrapper tests, with substitutes for `ISubscriptionStore` and `ISubscriptionCacheBackend`:
  - With a miss for all owners, the wrapper calls the inner `FindForDeliveryAsync` one time with all companies and users, and calls `SetAsync` for the defaults, each company, and each user. An owner with no subscription gets an empty list.
  - With a hit for some owners, the wrapper calls the inner store with only the missing companies and users, and returns all subscriptions whole. With a hit for all owners, the inner store receives no call.
  - The wrappers of two keys give different cache keys and tags for the same company id (D114).
  - With the defaults in the cache and a miss for one user, the inner store returns the default subscriptions too. The wrapper ignores these rows, and calls `SetAsync` only for the user.
  - `SetAsync` receives the configured expiration, the tenant tag, and the owner tag. After an `Error` from the inner store, `SetAsync` receives no call.
  - A successful `AddAsync`, `UpdateAsync`, or `DeleteAsync` removes the owner tag of that subscription. A failed one removes nothing.
  - A backend error from `TryGetAsync` counts as a miss. A backend error from `SetAsync` or `RemoveByTagAsync` does not fail the call.
  - `InvalidateAsync` removes the owner tag. `InvalidateAllAsync` removes the tenant tag of its key, and the entries of another tenant stay.
- Wiring: with the cache on, `ISubscriptionStore` resolves to the logging wrapper around the caching wrapper around the implementation.
- The options tests of `SubscriptionCacheOptions`. `KeyPrefix` gets a value with a space and a value with an accented character. Configuration binding: a value in the section `MsgHatch:SubscriptionCache` reaches the options, and a value from the `Action` of the setup method wins over it.
- The use cases of `SubscriptionCacheBackendTestsBase`, run by both backend subclasses, each on a real in-memory cache: a set value with two subscriptions is read back whole, a miss gives null and stores no entry, and a removed tag removes the values of that tag while the values of another tag stay.
- `UseHybridSubscriptionCache` and `UseFusionSubscriptionCache` alone register all parts. `UseFusionSubscriptionCache` does not change the default FusionCache of the host.

Commits:
1. "Add the subscription caching wrapper and the cache backend contract"
2. "Add the HybridCache subscription cache backend"
3. "Add the FusionCache subscription cache backend"

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

Commits:
1. "Add HTTP endpoints for subscription management"

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
   - A log email client that writes each email to the log, a SignalR hub, and the subscription endpoints with `MapMsgHatchSubscriptions`, including the `/me` routes.
   - The broker channel through `UseWolverineBroker`, on a local queue of Wolverine, with a Wolverine handler that writes each broker message to the log. So the example needs no broker server.
   - OpenTelemetry with `AddSource(MsgHatchTelemetry.SourceName)` and `AddMeter(MsgHatchTelemetry.MeterName)`, and the OTLP exporter (D131).
   - `MsgHatchWorker : BackgroundService` starts `RunAsync` for the inbox and the outbox in one process, with the in-process dispatch signal and `EnablePolling` false (D101, D102).
   - At startup, the app writes the users and the companies of the shared test scenario (section 4 of `docs/subscription-domain.md`) into the example directory, and its subscriptions through `ISubscriptionManager`. Then it accepts one event of each message type A to F, and one event of the type C with the scope company Acme.
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

Commits:
1. "Add the example web app"
2. "Add example store tests"
3. "Add performance test projects"
4. "Add the store implementation guide"
5. "Update README and AGENTS.md"

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
1. "Add routing verification types and test events"
2. "Add the routing verifier"
3. "Add the routing verification endpoint"
4. "Add routing verification to the example app"

Done when: the build has no errors and no warnings, and all tests pass.

### Phase 15: delivery management

Goal: the API and the HTTP endpoints for a UI of the traffic, the history, and the repairs (section 4.19).

1. Add the members `Cancelled` at the end of `DeliveryStatus` and `PublishOutcome` (D110). The file stores get the status folder `cancelled`.
2. Add the types of section 4.19 in `Management/`: `MessageCursor`, `MessagePage<T>`, `InboxQuery`, `OutboxQuery`, `ReceivedMessageSummary`, `OutgoingMessageSummary`, `OutgoingMessageHistory`, `OutgoingMessageDetail`, `TrafficQuery`, `MessageCount`, `TrafficReport`, `ChannelStatus`, and `DeliveryActionResult`.
3. Add the management members `QueryAsync`, `GetAsync`, `CountAsync`, `RetryAsync`, and `CancelAsync` to the tenant stores `IInboxStore` and `IOutboxStore`. Add `CountAsync` and `PurgeAsync` to `IOutboxDispatchStore`, and `PurgeAsync` to `IInboxDispatchStore`. Add them to the logging wrappers and to the contract base classes (D108, D128, section 5.5).
4. Add the members to the in-memory, file, and SQLite stores, with the indexes of section 4.5. A query or a count of a file store reads each file of the matching folders, with the comment `ponytail: a query reads each file, use a database store for a large outbox`.
5. Add `DeletedMessages`, `IDeliveryManager`, the internal `DeliveryManager` (section 4.19), and `DeliveryManagerLoggingWrapper`. `AddMsgHatch` registers them as tenant services. From this phase on, the tests delete the test data through `IDeliveryManager.DeleteTestDataAsync`.
6. Add `src/MsgHatch.AspNetCore` with `MapMsgHatchDelivery`, `MapMsgHatchDeliveryContent`, the request and response types, and their `JsonSerializerContext` (section 4.19). Add `tests/MsgHatch.AspNetCore.Tests.Unit`.
7. In the example app, map the two route groups, each with its own authorization policy.

Tests:
- The management use cases of `InboxStoreTestsBase` and `OutboxStoreTestsBase` (section 5.5) run on the in-memory, file, and SQLite stores. They include the tenant rule that the tenant store of one key does not see the messages of another key.
- A `Cancelled` message is never claimed again, and `RecordTryAsync` keeps its status rules.
- `PurgeAsync` of each dispatch store deletes only the finished messages that finished before the given time, of all tenants, at most `maxCount`, and never a pending message (D128).
- Delivery manager tests, with substitutes for the stores and the dispatch signal, and the test channel:
  - `GetChannelsAsync` gives each registered channel with its down state, its pending count, and its oldest due time. It calls `GetDownChannelsAsync` one time and `CountAsync` one time.
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
1. "Add cancelled messages"
2. "Add the delivery management types, store members, and purge"
3. "Add delivery management to the in-memory, file, and SQLite stores"
4. "Add the delivery manager"
5. "Add HTTP endpoints for delivery management"
6. "Add delivery management to the example app"

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
1. "Add the registry writer and the registry settings store contract"
2. "Add the stored registry"
3. "Skip disabled message types and channels in the pipeline"
4. "Add registry settings stores to the in-memory, file, and SQLite packages"
5. "Add HTTP endpoints for registry administration"

Done when: the build has no errors and no warnings, and all tests pass.

### Phase 17: a store for each tenant

Goal: one storage for each tenant, for example one SQLite file for each tenant (D125).

1. Add `ITenantSource` and `TenantSourceLoggingWrapper` (sections 4.6 and 4.15).
2. Add the library dispatch composites `TenantInboxDispatchStore` and `TenantOutboxDispatchStore`, with the next due time of each tenant and channel, and the core method `UseStorageForEachTenant<TTenantSource>` with the internal wrapper of the tenant stores, to `MsgHatch.Domain`. They follow the rules of section 4.5 (D125). Add the subclasses `TenantInboxDispatchStoreUnitTests` and `TenantOutboxDispatchStoreUnitTests` of the store contract base classes, on in-memory stores for two tenants and for `TenantKey.None`.
3. Add `SqliteTenantStoreOptions` with its validator and `UseSqliteTenantStores<TTenantSource>` to `MsgHatch.Data.Sqlite`, and `UseSqliteTenantSubscriptionStore` to `MsgHatch.Subscriptions.Data.Sqlite` (section 4.7).

Tests:
- The use cases of `InboxStoreTestsBase` and `OutboxStoreTestsBase` run on the dispatch composites.
- With an in-memory store for each key through a test function:
  - The tenant store of each key writes only into the store of that key.
  - A claim takes the messages of all stores in turn, and starts after the store that gave the last messages. `NextDueAtUtc` is the earliest value of all stores.
  - After a claim that finds no due message of a tenant, the next claim does not ask that tenant before its next due time. An `AddAsync` through the tenant store of that tenant in the same process clears that time, so the next claim asks it. A message that another store instance adds is found after `MaxPollInterval`.
  - `RecordTryAsync`, `CompleteAsync`, and `FailAsync` go only to the store of the tenant of the message.
  - The down states are only in the store of `TenantKey.None`. With a down state there, a claim returns that state, and claims no message of a tenant store.
  - `CountAsync` sums the counts of all stores. `DeleteTestDataAsync` and `PurgeAsync` call each store, and `PurgeAsync` deletes at most `maxCount` messages in total.
- `UseSqliteTenantSubscriptionStore` gives the subscription store of each key on the file of that key.
- `UseSqliteTenantStores` with the `TenantConnectionString` `Data Source={folder}/msghatch-{tenant}.db` makes one file for each tenant of the tenant source, and the file of `SystemConnectionString` for the messages with no tenant, the down states, and the registry settings. A tenant id with the character `|` gives an escaped file name.
- The options tests of `SqliteTenantStoreOptions`: a `TenantConnectionString` with no `{tenant}` fails, and a `SystemConnectionString` with `{tenant}` fails.
- A host that calls both `UseSqliteStores` and `UseSqliteTenantStores` fails the startup validation, because two methods select the outbox stores (D62).
- In `ExampleApp.Tests.Unit`: two tenants on two SQLite files. An event of each tenant is sent, and each file has only the messages of its tenant.

Commits:
1. "Add the tenant source and the dispatch composites"
2. "Add SQLite stores for each tenant"

Done when: the build has no errors and no warnings, and all tests pass.

### Phase 18: EF Core stores

Goal: the inbox, outbox, registry settings, and subscription stores on EF Core, with SQL Server as the first provider (D120).

1. Add `src/MsgHatch.Data.EntityFrameworkCore` with `EntityFrameworkStoreOptions` and its validator, `MsgHatchDbContext`, `EntityFrameworkInboxStore`, `EntityFrameworkOutboxStore`, `EntityFrameworkRegistrySettingsStore`, and `UseEntityFrameworkStores` (sections 4.5 and 4.7):
   - The model maps the columns of section 4.5, in the schema of `EntityFrameworkStoreOptions.Schema`, with the same indexes. The partial claim index is a filtered index with `WHERE Status = 'Pending'` (D104).
   - Each store is a singleton, and makes one `DbContext` for each operation through the pooled `IDbContextFactory<MsgHatchDbContext>` (D130).
   - The claim is one raw SQL statement through `FromSql`, because LINQ cannot express it. For SQL Server, a CTE selects the first due rows with `TOP (@max)`, `ORDER BY NextTryAtUtc`, and the hints `ROWLOCK, READPAST, UPDLOCK`, and an `UPDATE` of the CTE with an `OUTPUT` clause sets the lease (D120). The store selects the statement by the provider of the context, and a provider with no statement gives `Errors.NotFound` at startup.
   - `RecordTryAsync` is one `UPDATE` that appends the try to the JSON column `Tries` with `JSON_MODIFY`, with `WHERE Id = @id AND TryCount = @tryNumber - 1` (D27).
   - The package creates no schema. The host creates it with its own EF Core migrations, and the tests use `EnsureCreatedAsync`.
2. Add `src/MsgHatch.Subscriptions.Data.EntityFrameworkCore` with `EntityFrameworkSubscriptionStore` and `UseEntityFrameworkSubscriptionStore`.
3. Add `tests/MsgHatch.Data.EntityFrameworkCore.Tests.Integration` and `tests/MsgHatch.Subscriptions.Data.EntityFrameworkCore.Tests.Integration`, with Testcontainers for SQL Server (D124).
4. Add the EF Core sections to `docs/stores.md`.

Tests:
- The use cases of `InboxStoreTestsBase`, `OutboxStoreTestsBase`, `RegistrySettingsStoreTestsBase`, and `SubscriptionStoreTestsBase` run on the EF Core stores against SQL Server in a container. Each test class uses its own database.
- Two parallel claims get different messages, so the claim is atomic between concurrent dispatchers. A claim of one message takes the message with the earliest `NextTryAtUtc`.
- The options tests of `EntityFrameworkStoreOptions`.

Commits:
1. "Add EF Core stores for the inbox, the outbox, and the registry settings"
2. "Add the EF Core subscription store"

Done when: the build has no errors and no warnings, all unit tests pass, and the integration tests pass in the stage after the merge.

### Phase 19: MongoDB stores

Goal: the inbox, outbox, registry settings, and subscription stores on MongoDB (D121).

1. Add `src/MsgHatch.Data.MongoDb` with `MongoDbStoreOptions` and its validator, `MongoDbInboxStore`, `MongoDbOutboxStore`, `MongoDbRegistrySettingsStore`, and `UseMongoDbStores` (sections 4.5 and 4.7):
   - The class maps come from the content type registry, with the discriminators of D30.
   - A claim is a loop of `findOneAndUpdate` calls, one for each message, until `maxCount` messages or no due message (D121). Each call sets `nextTryAtUtc` and `leaseUntilUtc` to the end of the lease (D103).
   - A try is a `$push` in the same update as the delivery state, with the filter `tryCount` equal to the try number minus 1 (D27).
   - The claim index is a partial index on `channel` and `nextTryAtUtc` with the filter `status: "Pending"` (D104). The store creates its indexes one time for each database in the process.
2. Add `src/MsgHatch.Subscriptions.Data.MongoDb` with `MongoDbSubscriptionStore` and `UseMongoDbSubscriptionStore`.
3. Add `tests/MsgHatch.Data.MongoDb.Tests.Integration` and `tests/MsgHatch.Subscriptions.Data.MongoDb.Tests.Integration`, with Testcontainers for MongoDB (D124).
4. Add the MongoDB sections to `docs/stores.md`.

Tests:
- The use cases of `InboxStoreTestsBase`, `OutboxStoreTestsBase`, `RegistrySettingsStoreTestsBase`, and `SubscriptionStoreTestsBase` run on the MongoDB stores against MongoDB in a container. Each test class uses its own database.
- Two parallel claims get different messages.
- The options tests of `MongoDbStoreOptions`.

Commits:
1. "Add MongoDB stores for the inbox, the outbox, and the registry settings"
2. "Add the MongoDB subscription store"

Done when: the build has no errors and no warnings, all unit tests pass, and the integration tests pass in the stage after the merge.

### Later phases

These items are on the roadmap with no detailed plan yet. Each one becomes its own phase with the same rules:
- A dispatch signal between processes, for example with a named pipe or a message broker (D102).
- A PostgreSQL provider for the EF Core stores.
- The Razor template engine `MsgHatch.Email.Razor` (D45).
- SMS and push channels.
- Batched try records: one store call records the tries of several sent messages, for more throughput on one channel.
- `MsgHatch.Broker.AmazonSns`, the first direct broker client (D134):
  - It is an `IBrokerClient` with one SNS `Publish` call for each message. It maps the headers, with the `msghatch-*` headers, to SNS message attributes. SNS fans out each message to the SQS queues of the topic.
  - It uses the AWS SDK for .NET, as the SES client of phase 4 does, so the host keeps one SDK version and one credential setup.
  - SNS sends at most 10 message attributes to an SQS subscription with raw message delivery, and discards a message with more attributes there. So the client gives `Errors.ValidationFailed` for a message with more than 10 headers.
  - The phase checks the SDK call of the `Publish` action, and the size limit of a message with its attributes, in the pinned version.
- More direct broker clients, one for each SDK, with the same pattern as SNS: `Azure.Messaging.ServiceBus`, `Confluent.Kafka`, and `RabbitMQ.Client`.
- More framework clients: Rebus, SlimMessageBus, and `MsgHatch.Broker.MassTransit` on MassTransit 8 (section 6). SlimMessageBus is a light publish abstraction with transport plugins. Its phase first checks that the client uses no outbox of SlimMessageBus.
