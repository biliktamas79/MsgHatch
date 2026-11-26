---
name: writing-tests
description: The test rules of MsgHatch - how to choose the level of a test (unit or integration), name and place it, list its cases, write it in Arrange, Act, Assert, build its data with factories, use test doubles, keep it deterministic, share one test set across implementations, write an integration test against one real server, keep the README examples tested, and prove that a test can fail. Use when you add or change a test, reproduce a bug before a fix, choose a test level, move a test between levels, rename covered code, or review a test.
---

# Writing tests in MsgHatch

These are the test rules of this repository. Section 5.5 of `docs/design.md` lists the test projects, and D330 records the rule for the level of a test. Where this guide and a decision of the design differ, the decision wins, and the guide gets a fix.

## Terms

- **Unit under test** - one method, or one decision branch inside one method. The test drives one branch and asserts the direction that the code takes.
- **System under test (SUT)** - the type that holds the unit under test. A test constructs the real SUT and calls it.
- **Collaborator** - a type that the SUT calls. A collaborator has behavior. A record, a value type, or a pure function is not a collaborator.
- **Test double** - a stand-in for a collaborator: an NSubstitute substitute, or a fake such as `FakeTimeProvider` and `FakeLogger`.
- **Service** - one given server whose response and behavior the test depends on: a database server, a broker, a cloud service, or an API. The local file system and local networking are not services.
- **Contract base class** - an abstract class `{Interface}TestsBase<T>` that holds the use cases of one domain interface for every implementation.
- **Scenario** - one named set of test data that a group of tests shares, for example the Acme and Globex subscriptions.

## 1. Choose the level

| Level | What it tests | Services |
|---|---|---|
| Unit | One method or one decision branch. Each collaborator with behavior is a double. | None |
| Integration | The connection and the real communication with one service. | Exactly one |
| Performance | The time or the allocations of one path. | As the benchmark needs, on a dedicated machine |

1. Prefer a unit test. Choose the lowest level that can show the defect.
2. Ask one question: can a basic build agent in the cloud, with no service, run this test in the validation of a pull request? If yes, it is a unit test. If no, it is an integration test.
3. A test is an integration test when its result depends on the response and the behavior of one given service, for example a database server, a broker, a cloud service, or an API.
4. The local file system and local networking stay in unit tests: SQLite on a file in a temporary folder (D71), a folder of the file stores, a named pipe, or a TCP listener in the test process. The test creates its temporary folder, and deletes it at its end.
5. A test that tries to open a connection to a service is an integration test, also when it gives a connection string that is not valid on purpose. A test of a constructor that throws is a unit test when the constructor opens no connection and makes no network call.
6. A test that builds a client of a service is an integration test when the client starts network calls on its own. The MongoDB client starts a server monitor in its constructor, so a test that resolves a MongoDB store is an integration test.
7. A test of an error case, an input validation, or other code that can use a doubled connection is a unit test. So is a test that reads only options, the DI registrations, the startup validation, the EF Core model, or a mapping of an exception that the test creates, because these steps open no connection.
8. An ASP.NET Core `TestServer` in the test process, with each service doubled, is a unit test.
9. Never test a branch of the code at the integration level. A unit test finds the same defect in milliseconds, and its failure names the cause.
10. Never put two levels into one test project.

## 2. Name and place the test

**The project.** One test project for each production project and each level, in `tests/`:

```
tests/{Project}.Tests.Unit
tests/{Project}.Tests.Integration
tests/{Project}.Tests.Performance
```

1. The suffix selects the stage. The validation of a pull request runs `**/*.Tests.Unit/*`, and the stage after the merge runs `**/*.Tests.Integration/*`. A project with another suffix runs in no stage.
2. Add a new test project to `MsgHatch.slnx` in the same commit. A project that is not in the solution does not build, and its tests never run.
3. A unit test project and an integration test project of one package can both exist. Each test goes into the project of its level.

**The class.** The name of the SUT and the level:

```
{Sut}UnitTests    {Sut}IntegTests    {Sut}PerfTests
```

1. Spell the name of the SUT exactly. When an interface has several implementations, name the implementation, for example `MongoDbInboxStoreIntegTests`.
2. A contract base class is `{Interface}TestsBase<T>`, with no level. Its subclass gives the level.
3. Split a large class into partial files with a suffix for each operation, for example `InboxStoreTestsBase.ClaimDueAsync.cs`. The class name stays one name.

**The method.** `Method_Condition_ExpectedResult`, for example `AddMsgHatch_SecondCallWithAnotherRoot_FailsTheStartupValidation`. The name carries the name of the member under test, so a failure leads back to the code.

**A rename.** When a class or a method of the production code gets a new name, the tests follow in the same change:

1. Rename the test class, its file, and its partial files.
2. Rename each test method that carries the old member name.
3. Rename a contract base class and each of its subclasses together.
4. Search the test projects for the old name: a factory, a helper, a data row, or a comment can carry it.
5. Make the rename its own commit, with no change of behavior.

## 3. List the cases before the code

1. Read the code under test in full. List each public behavior, each guard clause, each error result, and each collaborator.
2. List the cases: the normal path, each boundary, each error that the code returns, and each guard.
3. One test asserts one behavior. A workflow of several steps on the same data counts as one behavior (section 4, rule 11).

**A limit costs three cases:** one below it, at it, and one above it. A minimum and a maximum cost six.

| Input | Always test | With a limit |
|---|---|---|
| `string` | `null`, `""`, white space only, a value with an accented letter or a special character | below, at, and above the minimum and the maximum length |
| integer | zero, a positive value, a negative value | below, at, and above each end of the range |
| `TimeSpan`, decimal | zero, a small and a large value, a negative value | below, at, and above each end of the range |
| collection | `null`, empty, one item, several items, a duplicate item | below, at, and above the maximum count |

1. Test white space apart from the empty string. A check that trims and a check that does not differ only there.
2. Test an accented letter where the value reaches a database, a file name, a URL, or a header.
3. Test a collection with several items. A check of the first item only passes with one item.
4. State the result for a duplicate item: rejected, removed, or kept.

**Data-driven tests.**

1. When cases differ only in the input and the expected result, write one `[Theory]` with one row for each case.
2. When a case needs another Arrange or another assertion, write another test. A row that needs an `if` in the body is not a row.
3. Use `[InlineData]` for a few rows. Use a `TheoryData<...>` property with `[MemberData]` for many rows, or for rows that two tests share. Name the property for the behavior, for example `InvalidOptionsRows`.
4. Give each row a name when the values alone do not say the case, for example a first `string row` parameter that the assertion also includes.
5. Write each expected value and each boundary as a literal in the row. Never compute it with the code under test, and never read it from a constant of the production code.

## 4. Write the test in Arrange, Act, Assert

```csharp
[Fact]
public async Task UseResend_ConfigSectionPath_BindsTheOptionsUnderTheRoot()
{
    // Arrange
    var services = TestHost.CreateServices(configuration: new Dictionary<string, string?>
    {
        ["Messaging:Resend:ApiKey"] = "re_root",
        ["MsgHatch:Resend:SenderName"] = "Default",
    });
    services.AddMsgHatch(configSectionPath: "Messaging").UseResend();
    await using var provider = services.Build();

    // Act
    var options = provider.GetRequiredService<IOptions<ResendOptions>>().Value;

    // Assert
    Assert.Equal(("re_root", (string?)null), (options.ApiKey, options.SenderName));
}
```

1. Mark the three parts with the comments `// Arrange`, `// Act`, and `// Assert`. Keep one Act.
2. Assert the result and the decision of the branch: the return value, the error, the stored state, and the calls on the doubled collaborators.
3. In a unit test of one implementation, verify each call that the branch makes on a doubled collaborator, with its exact count and its arguments, for example `store.Received(1).AddAsync(Arg.Is<OutgoingMessage>(m => m.ContentType == contentType), Arg.Any<CancellationToken>())`. Verify with `DidNotReceive` each call that the branch must not make, for example the call of the other branch. Use `Received.InOrder` when the order of the calls is part of the behavior.
4. In a contract test (section 9), assert only the behavior that a caller of the interface sees, never a call inside one implementation, because each implementation reaches the same result with other calls.
5. Assert a specific value. A test that only asserts "not null" passes with a wrong value.
6. Assert a collection whole, with `Assert.Equal` on the expected collection. Give the collection under test at least two items.
7. Assert several related values in one `Assert.Equal` on a tuple. The failure then shows all of them.
8. Write no `if`, no loop, and no `try`/`catch` in a test body. Make another test or another data row.
9. Await each asynchronous call, and never block on a result. Pass `TestContext.Current.CancellationToken` to each call that takes a token. A test class gets it from `private static CancellationToken Ct => TestContext.Current.CancellationToken;`. A test of the cancellation behavior is the one case that uses a token of its own, and a guard against a hang is another. Make such a token from `CancellationTokenSource.CreateLinkedTokenSource(Ct)`, and then cancel it, or set its time with `CancelAfter`. So a stop of the test runner also stops the test.
10. Assert an exception with `Assert.Throws<T>` or `Assert.ThrowsAsync<T>`, or with `Record.Exception` and `Assert.IsType<T>`.
11. Several Arrange, Act, Assert blocks in one test are allowed only for a workflow whose steps share an expensive setup. Label each block with the step, assert after each Act, and stop at a few steps.

**A failure.** Assert a stable identifier, never a text:

1. Assert the `ErrorType` or its code, for example `Assert.Equal(Errors.Unavailable, error?.Type)`.
2. For an options error, assert the options type, the options name, or the name of the failed property, for example with `OptionsFailures.FailedProperties`.
3. For a setup error that has no code, assert the start of the message that names the setup methods, as the startup validation tests do. Do not assert the rest of the text.
4. Never assert a translated text, a display name, or a formatted date or number.
5. A test of a web UI asserts a label or another text only when its source defines no id or key for the text, or when the UI supports only one language. When the text comes from a text resource or has a text id, assert the id or the key, never the text that a translation or a culture changes.
6. In an input validation test, put a comment at the end of the Arrange line that makes the input not valid.

## 5. Build the test data with factories

Each test project builds its test data with factory methods with valid defaults. When a test needs realistic data, for example many names, addresses, or texts for an integration test or a seeder, use the Bogus package with a fixed seed. Add it only to the test project that needs it. Do not use it where a factory with a few literal values is enough.

1. Give each input type a factory, for example `TestMessages.AReceivedMessage(...)` or `AChannelEntry(...)`. Name it for what it makes, and for its state.
2. A factory returns a valid object by default. Named parameters with defaults let a test change one field in one line.
3. Write a failure case as the valid object from the factory plus one changed field, with the comment of section 4.
4. Never build a large object inline in a test body when a factory can make it.
5. Search the test projects for an existing factory before you write one. When a second class needs a factory, move it to a shared file in `TestTypes`, and do not copy it.
6. Put a complex argument into a named local in the Arrange, never inside the Act call.
7. Bind the result of a factory to a local, and read each asserted value from that local. Never write the default of a factory again as a literal in the Act or the Assert.
8. A value that the test chooses and uses in two parts gets one local or one `const`.
9. A factory has no condition for two callers and no assertion. Write a second factory or add a parameter.
10. Name an assertion helper for the requirement that it checks, pass the expected values as parameters, and make its failure name the field.

## 6. Use test doubles

1. Use NSubstitute for a collaborator with behavior. Do not double a record, a value type, or a pure function.
2. Double the direct collaborators of the SUT only. A double two calls away shows that the SUT is too large.
3. Never double the SUT, and never replace a member of the SUT. A member that a test must replace is a collaborator that the SUT does not declare yet.
4. Read the time only through `TimeProvider`. The test uses `FakeTimeProvider`, which starts at `2030-01-01T00:00:00Z` (`TestHost.Start`).
5. Use `FakeLogger` and the fake log collector for log records. Assert the event id and the level, not the text.
6. Test only the public API and the DI setup methods. Never add `InternalsVisibleTo`.
7. A doubled connection for an error case: throw the exception of the provider from the double, or from an EF Core interceptor before the open, so that no connection opens.

## 7. Build the DI container from the setup methods

1. A test of one class needs no container. Construct the SUT, and give it the doubles.
2. When a test needs a container, call the same setup methods that a host calls: `AddMsgHatch` and the `Use...` methods of the package. Never copy their registrations into a test.
3. Start from `TestHost.CreateServices(...)` of the test project, which adds the configuration, the fake clock, and the fake logging. Build with `TestHost.Build`, which validates the scopes and the registrations.
4. Replace a registration after the setup call, with a factory overload of the setup method or with `Replace`. A registration before the setup call loses.
5. Keep the replaced registrations few, and name the reason for each one.

## 8. Keep tests deterministic and independent

1. Inject the time, the random values, and the identifiers. Use fixed values, and give a Bogus faker a fixed seed, so that each run makes the same data.
2. Never sleep to wait. Wait on the task or the signal of the code, with `TestHost.HangGuard` as the upper limit.
3. No test depends on the order of the tests, or on the state that another test left.
4. Each unit test makes its own instances and its own data. Two tests never share a static field, a file, or a key.
5. xUnit runs the test classes in parallel. When a test fails only in parallel, fix the shared state, and do not turn parallel off.
6. A class that changes process state, for example an environment variable, removes the change in `Dispose`, and its tests run in one class.

## 9. One test set for many implementations

A domain interface with more than one implementation gets its use cases once, in a contract base class in `tests/MsgHatch.Domain.Tests.Unit/Contracts` (or the contracts folder of its package).

1. The base class is `public abstract partial class {Interface}TestsBase<T>`, with the type parameter limited to the interface.
2. The constructor of the base takes the implementation and an abstraction of its test data, for example `IInboxStoreTestData`. The base never names a concrete type.
3. Split the base into one partial file for each operation of the interface.
4. A subclass has one constructor that builds the implementation, and no test of its own unless the behavior belongs only to that implementation.
5. The subclass of an in-process implementation, or of an implementation on SQLite or the local file system, is a unit test. The subclass of an implementation on a service is an integration test.
6. A new requirement goes into the base once, so each implementation must meet it.
7. A contract test asserts what a caller of the interface sees, and verifies no call inside an implementation. The calls of one implementation belong to its own unit tests (section 4, rule 3).

## 10. Write an integration test

1. Name the one service that the test covers. Double the other collaborators.
2. Read the server from the configuration: the key of the project in `appsettings.Local.json`, which git ignores, and `appsettings.json` describes. With no value, a fixture can start the server with Testcontainers. Never write an address or a secret in a test.
3. When the test needs an account and has none, skip it. Put the reason once, as a constant on the account or fixture type, and use `[Fact(Skip = X.NotConfigured, SkipUnless = nameof(X.IsConfigured), SkipType = typeof(X))]`.
4. Create the fixture in the constructor or in `IAsyncLifetime.InitializeAsync`, never in a field initializer.
5. Choose the data strategy once for each class:
   - Tests that only read share one scenario, which a class fixture seeds one time.
   - A test that writes has its own data, and the reset runs before and after it.
6. Seed through the store or the migrations of the package, as production writes, never through the raw driver.
7. Separate test data from other data with certainty: a database or a collection whose name starts with the name of the test project (D180, D183), or a container of the run. Delete or drop only such a database.
8. Clean up at the start and at the end, so that the data of a stopped run does no harm.
9. Assert the real contract: the stored shape, the index, the status code, or the payload.

## 11. Keep the README examples tested

1. Each C# block of a package README equals a region `#region readme:{name}` of a test, and each JSON block equals a file `Readme/{name}.json` of a test project of the package.
2. The README check reads the regions and the files of the unit and the integration test project of the package together. A region or a file in both projects fails the check.
3. The check `Readme_Snippets_EqualTheirSources` runs in the unit test project, so each pull request checks each snippet.
4. A README example that needs a server, for example a migration step, stays with its region in the integration test project.
5. Test the options table with `ReadmeCheck.AssertOptionsTables(typeof(...))`.

## 12. Run the test and prove that it can fail

1. Run one test project at a time: `dotnet test tests/{Project}.Tests.Unit`. Use `--filter "FullyQualifiedName~{Class}"` for one class.
2. Read the test count. A run that reports 0 tests is a failure.
3. Break the line of production code that the new test covers. Run the test, and see it fail for the right reason. Restore the code, and run the test again.
4. A bug fix starts with a test that reproduces the defect and fails. Fix the code after that.
5. Never change a test only to make it pass. First decide whether the contract changed or the code broke. A changed contract gets its own commit, which says why.
6. Fix or delete a flaky test. Never add a retry.

## 13. Coverage

1. `pwsh ./coverage.ps1` runs each unit test project with coverage, and fails when the line coverage of the production code is less than 90 %.
2. The coverage leaves out generated code and the EF Core migrations of the tools (D70). Hand-written code always counts.
3. Read an uncovered line as a behavior with no test. Never write a test only to raise the number, and delete a test that asserts nothing.

## 14. Report

After a test change, report:

1. Each test file that you added, moved, or changed.
2. The cases, one line each, with the level.
3. The run command, and the counts of passed, failed, and skipped tests.
4. Each package that you added, with its license, or "no new package". The license must be MIT, Apache-2.0, BSD-2-Clause, BSD-3-Clause, or ISC.
5. Each assertion on a text that you found, with the file, the line, and the stable identifier to assert instead.
6. Each gap that you left, with the reason.
