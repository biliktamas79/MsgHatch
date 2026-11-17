# AGENTS.md

This file provides guidance to coding agents when they work with code in this repository.

## State of the repository

MsgHatch is a .NET 10 library for message routing, message templates, and delivery through an inbox and an outbox. The optional package `MsgHatch.Subscriptions` adds managed subscriptions. The code arrives in phases, as `docs/roadmap.md` describes. Each phase ends in a tested and working state. Phases 0 to 25 are done: the core, the channels, the file and SQLite stores, the subscription router with its stores, cache, and HTTP endpoints, the example app, the benchmarks, the routing verification, the delivery management, the registry administration, a store for each tenant, the EF Core stores on SQL Server, the MongoDB stores, the Amazon SES client against a real account, the dispatch signal between the processes of one machine, the company folders of the template stores, the Liquid email templates, the template library that knows no channel, and the HTTP API of the templates.

The plan has two documents:

- `docs/design.md` is the single source for the requirements, terms, decisions (D1 to D225), architecture, and conventions.
- `docs/roadmap.md` is the single source for the order of the phases, and for the items, tests, and commits of each phase.

Read the current phase in the roadmap, and the design sections that it refers to, before you write code.

`docs/subscription-domain.md` describes the subscription domain of `MsgHatch.Subscriptions`: the three owner levels, the recipients, the merge rules, the resolution trace, the domain actions, and the numbered examples (for example `R7`, `M15`, or `T3`). A test of the subscription domain takes its rows from these examples, and names the example in each row.

`docs/stores.md` explains the formats, the publish tries, the claims, the tenant field, and the versioning of the file, SQLite, EF Core, and MongoDB stores, a storage for each tenant, and what a store for another storage must do. Read it before you change a store or add one.

`examples/ExampleApp` is the subscription example of section 1.1 of the design as a web app, with its use cases in the folders `Notices`, `PasswordResets`, `Recipients`, `Channels`, and `Startup`. `tests/ExampleApp.Tests.Unit` tests it on SQLite files.

## Projects

| Area | Projects in `src/` |
|---|---|
| Core | `MsgHatch.Domain`, `MsgHatch.SourceGeneration`, `MsgHatch.AspNetCore` |
| Templates | `MsgHatch.Templates`, `MsgHatch.Templates.Liquid`, `MsgHatch.Templates.AspNetCore` |
| Channels | `MsgHatch.Email`, `MsgHatch.Email.AmazonSES`, `MsgHatch.Email.Resend`, `MsgHatch.SignalR`, `MsgHatch.Broker`, `MsgHatch.Broker.Wolverine` |
| Stores | `MsgHatch.Data.FileSystem`, `MsgHatch.Data.Sqlite`, `MsgHatch.Data.EFCore`, `MsgHatch.Data.MongoDb` |
| Subscriptions | `MsgHatch.Subscriptions`, `MsgHatch.Subscriptions.Data.FileSystem`, `MsgHatch.Subscriptions.Data.Sqlite`, `MsgHatch.Subscriptions.Data.EFCore`, `MsgHatch.Subscriptions.Data.MongoDb`, `MsgHatch.Subscriptions.Caching.Hybrid`, `MsgHatch.Subscriptions.Caching.FusionCache`, `MsgHatch.Subscriptions.AspNetCore` |
| Dispatch signal | `MsgHatch.Dispatch.NamedPipes` |

No phase after phase 25 has a detailed plan yet. The section "Later phases" of the roadmap lists the next items.

## Commands

From phase 1 on:

```bash
dotnet build MsgHatch.slnx
dotnet test MsgHatch.slnx
dotnet test tests/MsgHatch.Domain.Tests.Unit --filter "FullyQualifiedName~ErrorOrUnitTests"
```

The coverage report of the unit tests (PowerShell 7). It writes `artifacts/coverage/index.html`, and fails when the line coverage of the production code is less than 90 %:

```bash
pwsh ./coverage.ps1
```

From phase 4 on, the SES integration tests need an Amazon SES account. Put its settings into `tests/MsgHatch.Email.AmazonSES.Tests.Integration/appsettings.Local.json`, which git ignores. `appsettings.json` next to it describes each key. With no account, each test is skipped:

```bash
dotnet test tests/MsgHatch.Email.AmazonSES.Tests.Integration
```

The Resend integration tests need a Resend API key. Put it into `tests/MsgHatch.Email.Resend.Tests.Integration/appsettings.Local.json`, which git ignores. Each run sends 3 emails to the Resend test address `delivered@resend.dev`. With no key, each test is skipped:

```bash
dotnet test tests/MsgHatch.Email.Resend.Tests.Integration
```

From phase 6 on, the Wolverine integration tests need a RabbitMQ broker, for example a local container. Put its URI, for example `amqp://localhost:5672`, into `tests/MsgHatch.Broker.Wolverine.Tests.Integration/appsettings.Local.json`, which git ignores. The tests declare only exclusive queues. With no URI, each test is skipped:

```bash
dotnet test tests/MsgHatch.Broker.Wolverine.Tests.Integration
```

From phase 13 on, the benchmarks run in the Release configuration. Run one project at a time, because each run builds the project again. Section 4.11 of the design records the results:

```bash
dotnet run -c Release --project tests/MsgHatch.Domain.Tests.Performance
dotnet run -c Release --project tests/MsgHatch.Email.Tests.Performance
dotnet run -c Release --project tests/MsgHatch.Subscriptions.Tests.Performance
```

The example app needs no server, no cloud account, and no network. Its SQLite file `msghatch-example.db` is in the working folder of the app, and git ignores it. Its email templates are in the folder `Templates` of its build output (D221), so a write of the template endpoints never changes the files of the repository. A token for the user `"1"` comes from `dotnet user-jwts` (D158). The delivery endpoints need the roles `DeliveryAdmin` and `DeliveryContent` (D165), the registry endpoints need the role `RegistryAdmin` (D170), and the template endpoints need the role `TemplateAdmin` (D225):

```bash
dotnet user-jwts create --project examples/ExampleApp --name 1
dotnet user-jwts create --project examples/ExampleApp --name 1 --role DeliveryAdmin --role DeliveryContent
dotnet user-jwts create --project examples/ExampleApp --name 1 --role RegistryAdmin
dotnet user-jwts create --project examples/ExampleApp --name 1 --role TemplateAdmin
dotnet run --project examples/ExampleApp
dotnet test tests/ExampleApp.Tests.Unit
```

From phase 18 on, the EF Core integration tests use the SQL Server of `SqlServerIntegration:ConnectionString` in `appsettings.Local.json` of each EF Core integration project, which git ignores, for example the local container dev-mssql. `appsettings.json` next to it describes the key. The tests drop and create only the databases whose names start with the name of their project, at the start and at the end of a run (D180). With no connection string, they need a container runtime, for example Docker: Testcontainers starts SQL Server from the image `mcr.microsoft.com/mssql/server:2025-CU7-ubuntu-24.04`, and pulls the image only when it is missing. A machine with no image of the Ryuk container of Testcontainers sets `TESTCONTAINERS_RYUK_DISABLED=true` for its run (D177):

```bash
dotnet test tests/MsgHatch.Data.EFCore.Tests.Integration
dotnet test tests/MsgHatch.Subscriptions.Data.EFCore.Tests.Integration
```

From phase 19 on, the MongoDB integration tests use the MongoDB of `MongoDbIntegration:ConnectionString` in `appsettings.Local.json` of each MongoDB integration project, which git ignores, for example the local container dev-mongo with `mongodb://localhost:27017/?replicaSet=rs0`. `appsettings.json` next to it describes the key. The tests drop only the databases whose names start with the name of their project, at the start and at the end of a run (D183). MongoDB allows at most 63 characters in a database name, so the name of a test database adds at most 9 characters to the 54 of the subscription project. With no connection string, Testcontainers starts MongoDB from the image `mongo:8.3`:

```bash
dotnet test tests/MsgHatch.Data.MongoDb.Tests.Integration
dotnet test tests/MsgHatch.Subscriptions.Data.MongoDb.Tests.Integration
```

## Rules for each phase

- Build only what the current phase lists. Do not add code that a later phase removes or renames.
- Make one commit for each concern, with the code and its tests. Commit and push only when the owner asks. At the end of a phase, push `main` and add the annotated tag `phase-N`.
- Expected failures are `ErrorOr<T>` or `Error?` values. Do not add custom exception types.
- Write each log method as a source-generated `[LoggerMessage]` extension method of `ILogger`, and call it as `logger.EventName(...)`.
- Turn a sequence into an array or a list with a collection expression, for example `[.. items]`, not with a LINQ `ToArray()` or `ToList()`. `.editorconfig` sets IDE0305 to error.
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
- Coverage: `pwsh ./coverage.ps1` keeps the unit test line coverage of the production code at 90 % or more. Read an uncovered line as a behavior with no test, not as a number to raise.
- The use-case tests of each domain interface are in an abstract base class `{Interface}TestsBase<T>`. An implementation gets them with one subclass that only builds it.
- Tests use only the public API and the DI setup methods. Do not add `InternalsVisibleTo`.
- Each new test fails first.
