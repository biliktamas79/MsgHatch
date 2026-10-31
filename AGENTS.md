# AGENTS.md

This file provides guidance to coding agents when they work with code in this repository.

## State of the repository

MsgHatch is a .NET 10 library for message routing, message templates, and delivery through an inbox and an outbox. The optional package `MsgHatch.Subscriptions` adds managed subscriptions. The repository contains the plan only. The code arrives in phases, as `docs/roadmap.md` describes. Each phase ends in a tested and working state.

The plan has two documents:

- `docs/design.md` is the single source for the requirements, terms, decisions (D1 to D134), architecture, and conventions.
- `docs/roadmap.md` is the single source for the order of the phases, and for the items, tests, and commits of each phase.

Read the current phase in the roadmap, and the design sections that it refers to, before you write code.

`docs/subscription-domain.md` describes the subscription domain of `MsgHatch.Subscriptions`: the three owner levels, the recipients, the merge rules, the resolution trace, the domain actions, and the numbered examples (for example `R7`, `M15`, or `T3`). A test of the subscription domain takes its rows from these examples, and names the example in each row.

## Commands

From phase 1 on:

```bash
dotnet build MsgHatch.slnx
dotnet test MsgHatch.slnx
dotnet test tests/MsgHatch.Domain.Tests.Unit --filter "FullyQualifiedName~ErrorOrUnitTests"
```

From phase 13 on, the benchmarks run in the Release configuration:

```bash
dotnet run -c Release --project tests/MsgHatch.Domain.Tests.Performance
```

From phase 18 on, the integration tests need a container runtime, for example Docker:

```bash
dotnet test tests/MsgHatch.Data.EntityFrameworkCore.Tests.Integration
```

## Rules for each phase

- Build only what the current phase lists. Do not add code that a later phase removes or renames.
- Make one commit for each concern, with the code and its tests. Commit and push only when the owner asks. At the end of a phase, push `main` and add the annotated tag `phase-N`.
- Expected failures are `ErrorOr<T>` or `Error?` values. Do not add custom exception types.
- Read the time only through `TimeProvider`. The analyzer rule `RS0030` blocks the direct clock APIs.
- Read each setting only through a typed options class with data validation attributes and a source-generated validator. The analyzer rule `RS0030` blocks the `IConfiguration` indexer.
- A logging wrapper never logs a channel address, a payload value, rendered text, or a domain event. A span attribute or a metric tag never holds them either, and the metrics have no tenant tag.
- `UserId`, `CompanyId`, and `TenantId` hold strings. Map a consumer id with `From<T>`, and check a string from outside with `Create`.
- A user id and a company id are unique only inside one tenant. A tenant service is a keyed DI service with a `TenantKey` key, and its methods have no tenant parameter. The data keeps the tenant: the message context, the received message, and each outgoing message. Each cache key and SignalR name carries the tenant.
- A test message carries `MessageFlags.Test` from the inbox to each outgoing message and each log record. Its default test delivery is `Suppress`.
- Each implementation package has one `Use<Technology>` setup method for each layout. One call registers all parts of the package. It calls the generic domain `Use` methods, so that the wrappers and the validation are the same for each package.
- The library stores, transports, template stores, and registries are thread-safe singletons. Only a received message gets a DI scope.
- The core packages never reference `MsgHatch.Subscriptions`. The core knows only the router contract `IMessageRouter<TEvent>` and the composer contract `IEnvelopeComposer<TEvent, TEnvelope>`.

## Testing

Section 5.5 of the design has the full rules. The short form:

- Framework: xUnit v3 (`xunit.v3`). A test project is an executable.
- Assertions: the `Assert` class of xUnit. Assert a stable identifier for a failure, for example an `ErrorType` code, never the text of a message.
- Test doubles: NSubstitute. The clock is `FakeTimeProvider`, which starts at `2030-01-01T00:00:00Z`. The logger is `FakeLogger`.
- Test data: factory methods with valid defaults in each test project. No test data library.
- Projects: `tests/{Project}.Tests.Unit`, `tests/{Project}.Tests.Integration`, or `tests/{Project}.Tests.Performance`, one for each production project and each level. SQLite tests are unit tests, because SQLite runs in process on a file in a temporary folder (D71).
- Classes: `{Sut}UnitTests`, `{Sut}IntegTests`, or `{Sut}PerfTests`. Methods: `Method_Condition_ExpectedResult`.
- Run: `dotnet test MsgHatch.slnx`, or `dotnet test` with one test project. A run that reports 0 tests is a failure.
- The use-case tests of each domain interface are in an abstract base class `{Interface}TestsBase<T>`. An implementation gets them with one subclass that only builds it.
- Tests use only the public API and the DI setup methods. Do not add `InternalsVisibleTo`.
- Each new test fails first.
