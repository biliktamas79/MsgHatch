# AGENTS.md

This file provides guidance to coding agents when they work with code in this repository.

## State of the repository

MsgHatch is a .NET 10 library for message routing, message templates, and delivery through an inbox and an outbox. The optional package `MsgHatch.Subscriptions` adds managed subscriptions. The code arrives in phases, as `docs/roadmap.md` describes. Each phase ends in a tested and working state. Phases 0 to 51 are done: the core, the channels, the file and SQLite stores, the subscription router with its stores, cache, and HTTP endpoints, the example app, the benchmarks, the routing verification, the delivery management, the registry administration, a store for each tenant, the EF Core stores on SQL Server, the MongoDB stores, the Amazon SES client against a real account, the dispatch signal between the processes of one machine, the company folders of the template stores, the Liquid email templates, the template library that knows no channel, the HTTP API of the templates, the query and command interfaces of the management APIs with their contracts packages, the HTTP clients of the management APIs with their source generator, the admin UIs of the subscriptions and the templates with the Liquid editor, the template preview with sample payloads, one layout of the admin pages, the registry queries with their HTTP client and the read-only pages of the channels and the message types, the state history of the channels with the new order of the admin pages, the channel check, the removal of the open allocations of the hot path, the registry commands with the edit pages of the channels and the message types, the query and command interfaces of the delivery with their HTTP client, the delivery pages with the pause and the resume of a channel, the Amazon SNS broker client, the MongoDB change stream signal, the RabbitMQ dispatch signal, the versions and the NuGet packages, the package READMEs, and the atomic state rows of the file store with the build on Linux, one bind of the core options in `AddMsgHatch`, the tenant-wide SignalR messages, the SignalR demo dashboard, the package versions that float within their major, the EF Core stores on each relational provider with the SQL Server packages and the schema migrations, and the PostgreSQL packages of the EF Core stores, the EF Core tables in their schema with no prefix, with the exception classifiers and the JSON type of the EF Core stores, and the channel packages under `MsgHatch.Channels`.

The plan has two documents:

- `docs/design.md` is the single source for the requirements, terms, decisions (D1 to D320), architecture, and conventions.
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
| Channels | `MsgHatch.Channels.Email`, `MsgHatch.Channels.Email.AmazonSes`, `MsgHatch.Channels.Email.Resend`, `MsgHatch.Channels.SignalR`, `MsgHatch.Channels.Broker`, `MsgHatch.Channels.Broker.Wolverine`, `MsgHatch.Channels.Broker.AmazonSns` |
| Stores | `MsgHatch.Data.FileSystem`, `MsgHatch.Data.Sqlite`, `MsgHatch.Data.EFCore`, `MsgHatch.Data.EFCore.SqlServer`, `MsgHatch.Data.EFCore.PostgreSql`, `MsgHatch.Data.EFCore.MySql`, `MsgHatch.Data.MongoDb` |
| Subscriptions | `MsgHatch.Subscriptions`, `MsgHatch.Subscriptions.Data.FileSystem`, `MsgHatch.Subscriptions.Data.Sqlite`, `MsgHatch.Subscriptions.Data.EFCore`, `MsgHatch.Subscriptions.Data.EFCore.SqlServer`, `MsgHatch.Subscriptions.Data.EFCore.PostgreSql`, `MsgHatch.Subscriptions.Data.EFCore.MySql`, `MsgHatch.Subscriptions.Data.MongoDb`, `MsgHatch.Subscriptions.Caching.Hybrid`, `MsgHatch.Subscriptions.Caching.FusionCache`, `MsgHatch.Subscriptions.AspNetCore` |
| Dispatch signal | `MsgHatch.Dispatch.NamedPipes`, `MsgHatch.Dispatch.RabbitMq` |
| API contracts | `MsgHatch.Api.Contracts`, `MsgHatch.Subscriptions.Api.Contracts`, `MsgHatch.Templates.Api.Contracts` |
| API clients | `MsgHatch.Api.Client`, `MsgHatch.Subscriptions.Api.Client`, `MsgHatch.Templates.Api.Client` |
| Admin UIs | `MsgHatch.AdminUI`, `MsgHatch.Subscriptions.AdminUI`, `MsgHatch.Templates.AdminUI`, `MsgHatch.Templates.Liquid.AdminUI` |

Phase 52 has a detailed plan in the roadmap. No phase after phase 52 has a detailed plan yet. The section "Later phases" of the roadmap lists the next items.

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

From phase 41 on, the pack check (PowerShell 7). It packs the 42 packages in Release into `artifacts/packages`, and fails when a package has a wrong version, wrong metadata, or missing content (section 5.6 of the design). The GitHub workflow `.github/workflows/build.yml` runs `coverage.ps1` and `pack.ps1 -VersionSuffix ci.<run number>` on `ubuntu-latest` on each push of a branch and each pull request, and publishes to nuget.org only on a tag `v<version>`. Never push a tag `v*` unless the owner asks:

```bash
pwsh ./pack.ps1
```

From phase 4 on, the SES integration tests need an Amazon SES account. Put its settings into `tests/MsgHatch.Channels.Email.AmazonSes.Tests.Integration/appsettings.Local.json`, which git ignores. `appsettings.json` next to it describes each key. With no account, each test is skipped:

```bash
dotnet test tests/MsgHatch.Channels.Email.AmazonSes.Tests.Integration
```

The Resend integration tests need a Resend API key. Put it into `tests/MsgHatch.Channels.Email.Resend.Tests.Integration/appsettings.Local.json`, which git ignores. Each run sends 3 emails to the Resend test address `delivered@resend.dev`. With no key, each test is skipped:

```bash
dotnet test tests/MsgHatch.Channels.Email.Resend.Tests.Integration
```

From phase 6 on, the Wolverine integration tests need a RabbitMQ broker, for example a local container. Put its URI, for example `amqp://localhost:5672`, into `tests/MsgHatch.Channels.Broker.Wolverine.Tests.Integration/appsettings.Local.json`, which git ignores. The tests declare only exclusive queues. With no URI, each test is skipped:

```bash
dotnet test tests/MsgHatch.Channels.Broker.Wolverine.Tests.Integration
```

From phase 13 on, the benchmarks run in the Release configuration. Run one project at a time, because each run builds the project again. Section 4.11 of the design records the results:

```bash
dotnet run -c Release --project tests/MsgHatch.Domain.Tests.Performance
dotnet run -c Release --project tests/MsgHatch.Channels.Email.Tests.Performance
dotnet run -c Release --project tests/MsgHatch.Subscriptions.Tests.Performance
```

The example app needs no server, no cloud account, and no network. Its SQLite file `msghatch-example.db` is in the working folder of the app, and git ignores it. Its email templates are in the folder `Templates` of its build output (D221), so a write of the template endpoints never changes the files of the repository. A token for the user `"1"` comes from `dotnet user-jwts` (D158). The delivery endpoints need the roles `DeliveryAdmin` and `DeliveryContent` (D165), the registry endpoints need the role `RegistryAdmin` (D170), and the template endpoints need the role `TemplateAdmin` (D225).

The admin UI example `examples/AdminUiExample` calls the subscription, template, registry, and delivery APIs of `ExampleApp` through the HTTP clients, so `ExampleApp` runs first (section 4.24). It reads its token from the option `AdminUiExample:ApiToken` in its user secrets, which git ignores. The token needs the roles `TemplateAdmin`, `RegistryAdmin`, `DeliveryAdmin`, and `DeliveryContent`, and also passes the policy of the subscription routes. A token with no role `RegistryAdmin` gives 403 on the pages Channels and Message types. A token with no role `DeliveryAdmin` gives 403 on the pages Traffic, Outbox, and Inbox, and on the pending counts of the channel pages. A token with no role `DeliveryContent` gives 403 only on the content of a message page. Never write a token into a file of the repository. The host listens only on `http://localhost:5090`.

`examples/ExampleApp/README.md` has the steps that make the tokens and start both apps. `pwsh ./start-admin-ui.ps1` starts both apps in one terminal, and checks the roles of the token. The tests of both apps need no token and no setup:

```bash
dotnet test tests/ExampleApp.Tests.Unit
dotnet test tests/AdminUiExample.Tests.Unit
```

From phase 46 on, the SignalR demo `examples/SignalRDemo` needs no setup, no token, and no network (D316). It listens only on `http://localhost:5091`. Its tests run the hub on a test server with SignalR clients:

```bash
dotnet run --project examples/SignalRDemo
dotnet test tests/SignalRDemo.Tests.Unit
```

From phase 18 on, the EF Core integration tests use the SQL Server of `SqlServerIntegration:ConnectionString` in `appsettings.Local.json` of each EF Core integration project, which git ignores, for example the local container dev-mssql. `appsettings.json` next to it describes the key. The tests drop and create only the databases whose names start with the name of their project, at the start and at the end of a run (D180). With no connection string, they need a container runtime, for example Docker: Testcontainers starts SQL Server from the image `mcr.microsoft.com/mssql/server:2025-CU7-ubuntu-24.04`, and pulls the image only when it is missing. A machine with no image of the Ryuk container of Testcontainers sets `TESTCONTAINERS_RYUK_DISABLED=true` for its run (D177):

```bash
dotnet test tests/MsgHatch.Data.EFCore.Tests.Integration
dotnet test tests/MsgHatch.Subscriptions.Data.EFCore.Tests.Integration
```

From phase 48 on, the base EF Core stores also run on SQLite in the unit test projects, and the SQL Server packages, their migrations, and the example `examples/SchemaMigrator` have integration projects. These three projects have no appsettings files of their own: the files of `tests/MsgHatch.Data.EFCore.Tests.Integration` reach their output, so they use the same SQL Server. The migrations of a SQL Server package come from the EF Core tools, with the test project of the package as the startup project (`docs/stores.md` section 4.9). The tool `dotnet-ef` is a download, so it needs the owner's yes when it is missing:

```bash
dotnet test tests/MsgHatch.Data.EFCore.Tests.Unit
dotnet test tests/MsgHatch.Subscriptions.Data.EFCore.Tests.Unit
dotnet test tests/MsgHatch.Data.EFCore.SqlServer.Tests.Integration
dotnet test tests/MsgHatch.Subscriptions.Data.EFCore.SqlServer.Tests.Integration
dotnet test tests/SchemaMigrator.Tests.Integration
```

From phase 49 on, the EF Core integration projects also use the PostgreSQL of `PostgreSqlIntegration:ConnectionString` in `appsettings.Local.json` of `tests/MsgHatch.Data.EFCore.Tests.Integration` and of `tests/MsgHatch.Subscriptions.Data.EFCore.Tests.Integration`, for example the local container dev-postgres. The user needs the right to create databases, and the server needs the database `postgres`. PostgreSQL keeps at most 63 bytes of a database name, so the name of a test database starts with the name of the test project in lower case, with no `.Tests.Integration`, each `.` as `_`, and `__` (D180). The tests drop and create only these databases. Each test database has a pool of at most 10 connections, because PostgreSQL allows 100 connections by default. With no connection string, Testcontainers starts PostgreSQL from the image `postgres:18.7`, and pulls the image only when it is missing. The two base projects run the contracts on PostgreSQL with no dialect in their folder `PostgreSql`, and the PostgreSQL packages have their own projects. The migrations of a PostgreSQL package come from the EF Core tools, as the migrations of a SQL Server package do:

```bash
dotnet test tests/MsgHatch.Data.EFCore.PostgreSql.Tests.Integration
dotnet test tests/MsgHatch.Subscriptions.Data.EFCore.PostgreSql.Tests.Integration
```

From phase 50 on, the EF Core tables are in the schema of the options with no prefix `MsgHatch`, for example `msghatch.Outbox` and `msghatch.__StoreMigrations` (D321). The SQLite and MongoDB stores keep the prefix. The provider integration projects also run the contract bases with the option `JsonColumnType`: `jsonb` on PostgreSQL, and `json` on SQL Server, which needs SQL Server 2025 or Azure SQL (D323). Keep the name of a PostgreSQL test database short, for example `subs_jsonb`, because the prefix of the subscription project has 47 bytes. The design-time contexts of the EF Core tools set `JsonColumnType` to `json`, and the generator writes `JsonColumnType` in its place, as it does for the schema and the collation.

From phase 52 on, the EF Core integration projects also use the MySQL of `MySqlIntegration:ConnectionString` in `appsettings.Local.json` of `tests/MsgHatch.Data.EFCore.Tests.Integration` and of `tests/MsgHatch.Subscriptions.Data.EFCore.Tests.Integration`, for example the local container dev-mysql of `docker/mysql.compose.yml` with `Server=127.0.0.1;Port=3306;User ID=root;Password=...`. Use `127.0.0.1`, not `localhost`: the port listens only on IPv4, and Windows needs about 2 seconds to refuse `::1`. The user needs the right to create databases. On MySQL, the EF Core tables keep the prefix `MsgHatch` (D326). The provider locks the migrations with `GET_LOCK('__{database}_EFMigrationsLock')`, and MySQL allows at most 64 characters in a lock name. So the name of a MySQL test database has at most 45 characters: the prefix of the PostgreSQL rule with no `_data_efcore`, for example `msghatch_subscriptions_mysql__`, and then the name. The tests drop and create only these databases. Each test database has a pool of at most 10 connections, because MySQL allows 151 connections by default. A reset empties the tables with `TRUNCATE TABLE`, because two parallel inserts of a key that a `DELETE` removed deadlock in InnoDB. With no connection string, Testcontainers starts MySQL from the image `mysql:8.4`, and pulls the image only when it is missing. The two base projects run the contracts on MySQL with no dialect in their folder `MySql`, and the MySQL packages have their own projects:

```bash
dotnet test tests/MsgHatch.Data.EFCore.MySql.Tests.Integration
dotnet test tests/MsgHatch.Subscriptions.Data.EFCore.MySql.Tests.Integration
```

From phase 19 on, the MongoDB integration tests use the MongoDB of `MongoDbIntegration:ConnectionString` in `appsettings.Local.json` of each MongoDB integration project, which git ignores, for example the local container dev-mongo with `mongodb://localhost:27017/?replicaSet=rs0`. `appsettings.json` next to it describes the key. The tests drop only the databases whose names start with the name of their project, at the start and at the end of a run (D183). MongoDB allows at most 63 characters in a database name, so the name of a test database adds at most 9 characters to the 54 of the subscription project. The server must be a replica set, because the tests of the MongoDB dispatch signal open a change stream. With no connection string, Testcontainers starts MongoDB from the image `mongo:8.3` as a replica set with one member:

```bash
dotnet test tests/MsgHatch.Data.MongoDb.Tests.Integration
dotnet test tests/MsgHatch.Subscriptions.Data.MongoDb.Tests.Integration
```

From phase 38 on, the SNS integration tests need a standard SNS topic and an SQS queue with a raw message delivery subscription to the topic. The owner creates them, because the tests create no AWS resource. Put the access key, the region, the topic ARN, and the queue URL into `tests/MsgHatch.Channels.Broker.AmazonSns.Tests.Integration/appsettings.Local.json`, which git ignores. `appsettings.json` next to it describes each key. The tests delete each message that they receive from the queue. With no settings, each test is skipped:

```bash
dotnet test tests/MsgHatch.Channels.Broker.AmazonSns.Tests.Integration
```

From phase 40 on, the RabbitMQ dispatch signal tests need a RabbitMQ broker, for example a local container. Put its URI, for example `amqp://localhost:5672`, into `RabbitMqIntegration:Uri` of `tests/MsgHatch.Dispatch.RabbitMq.Tests.Integration/appsettings.Local.json`, which git ignores. `appsettings.json` next to it describes the key. Each test uses its own auto-delete exchange, and the signals declare only exclusive queues. The tests of a lost connection need the scheme `amqp`, and are skipped with `amqps`. With no URI, each test is skipped:

```bash
dotnet test tests/MsgHatch.Dispatch.RabbitMq.Tests.Integration
```

## Rules for each phase

- Build only what the current phase lists. Do not add code that a later phase removes or renames.
- Make one commit for each concern, with the code and its tests. Commit and push only when the owner asks. At the end of a phase, push `main` and add the annotated tag `phase-N`.
- From phase 41 on, each library project has the SemVer `VersionPrefix` of `Directory.Build.props`. A phase that changes a library project under `src/` raises it one time:
  - The patch for fixes only.
  - The minor for a new feature.
  - The major for a breaking change of the public API. Before 1.0.0, a breaking change raises the minor.
  - A change of only tests, examples, docs, or READMEs raises nothing.
- From phase 42 on, each library project has its own `README.md`. Keep it in sync with the code in the same commit as the code:
  - Change the setup, the options table, and the examples when the code changes them.
  - Add an entry for the new version to the version history of each package that the phase changes, with its release notes. Start a breaking change with "Breaking:".
  - The README tests fail when a code block or the options table differs from the code.
- A phase that adds a library project adds its `README.md`, its `Description`, its `PackageTags`, and its README tests.
- From phase 47 on, each package of `Directory.Packages.props` has the version `{major}.*`, for example `10.*`, or `0.{minor}.*` for the major 0 (D317). So each restore takes the latest stable version of the major. A package keeps an exact version only with the reason in the label of its item group, for example the Roslyn packages of the source generator.
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
- The packages of the API boundary set `IsAotCompatible` (D237): `MsgHatch.Domain`, `MsgHatch.Subscriptions`, `MsgHatch.Templates`, the contracts packages, the client packages, the two management `.AspNetCore` packages, and the admin UI packages. Each warning of the trim and AOT analyzers is an error. Use no `MakeGenericType`, `Activator.CreateInstance`, assembly scan, or dynamic proxy in them, and give each type parameter that reaches `ActivatorUtilities` the attribute `DynamicallyAccessedMembers(PublicConstructors)`.
- An admin page resolves only the query and command interfaces of `TenantKey.None` (D235, D249). The one exception is `MessageTypeListPage`, which also resolves the registered `IMessageTypeColumn` services, and a column resolves only the query interfaces of `TenantKey.None` (D275). An admin page sets no render mode, and uses the CSS classes of Bootstrap 5 (D239). A list page uses `AdminListPage`, an edit page or a view page uses `AdminEditPage`, a dialog uses `AdminDialog`, and the buttons of a page or a dialog go into `AdminActions`: Cancel or Back on the left, and the main action on the right (D259 to D261, D264). A page uses only the color variables of Bootstrap, so the host selects the dark mode (D266). The setup method of an admin UI package keeps its pages in a trimmed host with `DynamicDependency` (D250). A template editor derives from `TemplateEditorBase`, and a package registers it with `AddTemplateEditor<TEditor>(engine)` (D240).
- The management APIs have a query interface and a command interface each (D231). An implementation runs the stateless rules first, `SubscriptionDraftRules`, `RoutingVerificationRules`, `TemplateLookupRules`, or `DeliveryRules` (D234, D248, D292). The HTTP bodies and their mappings are in the contracts packages, and the route paths and `ErrorProblems` are internal sources in `src/Shared` (D228, D233). A client package declares its routes in an internal interface with `[HttpApi]`, and the generator writes its HTTP calls (D236). Its adapters run the same rules first, so a client gives the error of a rule with no HTTP call.

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
