using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Data.EFCore.Providers;
using MsgHatch.Data.EFCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Data.EFCore.Tests.Unit.DependencyInjection;

/// <summary>
/// <c>UseEntityFrameworkStores</c> on the EF Core SQLite provider: the stores run on each relational provider, and the
/// startup check refuses a provider that is not relational (D175, D318).
/// </summary>
public sealed partial class EntityFrameworkStoreExtensionsUnitTests
{
    private const string _inMemoryProvider = "Microsoft.EntityFrameworkCore.InMemory";

    private readonly SqliteTestDatabase _database = new();

    public static TheoryData<string, bool, int?, int> ClassifierRows => new()
    {
        { "the classifier of the host over the dialect", true, 403, 409 },
        { "the dialect after a classifier with no result", false, 403, 403 },
        { "the built-in mapping after a classifier and a dialect with no result", false, null, 503 },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTime Now => TestHost.Start.UtcDateTime;

    [Fact]
    public async Task UseEntityFrameworkStores_ConfigSectionPath_BindsTheOptionsUnderTheRoot()
    {
        // Arrange
        const string schema = "root";
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?>
        {
            ["Messaging:EntityFramework:Schema"] = schema,
            ["MsgHatch:EntityFramework:TextCollation"] = "RTRIM",
        });
        services.AddMsgHatch(configSectionPath: "Messaging").UseEntityFrameworkStores(o => o.UseSqlite(_database.ConnectionString));
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<EntityFrameworkStoreOptions>>().Value;

        // Assert
        Assert.Equal((schema, (string?)null), (options.Schema, options.TextCollation));
    }

    [Fact]
    public async Task UseEntityFrameworkStores_Sqlite_PassesTheStartupValidationAndClaimsTheAddedMessages()
    {
        // Arrange
        await using var provider = Build(o => o.UseSqlite(_database.ConnectionString));
        await _database.ResetAsync(provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>(), Ct);
        var received = TestMessages.AReceivedMessage(new TestEvent("text"));
        var outgoing = TestMessages.AnOutgoingMessage(channel: TestChannel.Id);
        await provider.GetRequiredService<IInboxStore>().AddAsync(received, Ct);
        await provider.GetRequiredService<IOutboxStore>().AddAsync([outgoing], Ct);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);
        var inbox = await provider.GetRequiredService<IInboxDispatchStore>().ClaimDueAsync(10, Now, TimeSpan.FromSeconds(30), Ct);
        var outbox = await provider.GetRequiredService<IOutboxDispatchStore>().ClaimDueAsync(TestChannel.Id, 10, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Null(exception);
        Assert.Equal(
            [received.Delivery.Id, outgoing.Delivery.Id],
            [Assert.Single(inbox.Value.Messages).Delivery.Id, Assert.Single(outbox.Value.Messages).Delivery.Id]);
    }

    [Fact]
    public async Task UseEntityFrameworkStores_ProviderThatIsNotRelational_FailsTheStartupValidationAndNamesTheProvider()
    {
        // Arrange
        await using var provider = Build(static o => o.UseInMemoryDatabase("msghatch"));

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.Equal(
            (true, true),
            (failure.Contains("UseEntityFrameworkStores", StringComparison.Ordinal), failure.Contains(_inMemoryProvider, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task UseEntityFrameworkStores_SqliteWithADialectOfSqlite_PassesTheStartupValidation()
    {
        // Arrange
        await using var provider = Build(o => o.UseSqlite(_database.ConnectionString).UseEntityFrameworkStoreDialect(new SqliteTestDialect()));

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task UseEntityFrameworkStores_DialectWithNoSchemas_PassesTheStartupValidationAndClaimsFromTheTableWithThePrefix()
    {
        // Arrange
        await using var provider = Build(o => o.UseSqlite(_database.ConnectionString).UseEntityFrameworkStoreDialect(new SqliteTestDialect(hasSchemas: false)));
        await _database.ResetAsync(provider.GetRequiredService<IDbContextFactory<MsgHatchDbContext>>(), Ct);
        var received = TestMessages.AReceivedMessage(new TestEvent("text"));
        await provider.GetRequiredService<IInboxStore>().AddAsync(received, Ct);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);
        var inbox = await provider.GetRequiredService<IInboxDispatchStore>().ClaimDueAsync(10, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Null(exception);
        Assert.Equal(received.Delivery.Id, Assert.Single(inbox.Value.Messages).Delivery.Id);
        Assert.Equal([["1"]], _database.Rows("SELECT count(*) FROM MsgHatchInbox"));
    }

    [Fact]
    public async Task UseEntityFrameworkStores_DialectWithNoSchemasAndASchemaThatIsNotTheDefault_FailsTheStartupValidationAndNamesTheOption()
    {
        // Arrange
        const string schema = "other";
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { ["MsgHatch:EntityFramework:Schema"] = schema });
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseEntityFrameworkStores(
            o => o.UseSqlite(_database.ConnectionString).UseEntityFrameworkStoreDialect(new SqliteTestDialect(hasSchemas: false)));
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.Equal(
            (true, true, true),
            (failure.Contains("UseEntityFrameworkStores", StringComparison.Ordinal), failure.Contains("Schema", StringComparison.Ordinal),
                failure.Contains(schema, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task UseEntityFrameworkStores_DialectOfAnotherProvider_FailsTheStartupValidationAndNamesBothProviders()
    {
        // Arrange
        const string providerName = "Other.Provider";
        await using var provider = Build(
            o => o.UseSqlite(_database.ConnectionString).UseEntityFrameworkStoreDialect(new SqliteTestDialect(providerName: providerName)));

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.Equal(
            (true, true, true),
            (failure.Contains("UseEntityFrameworkStores", StringComparison.Ordinal), failure.Contains(providerName, StringComparison.Ordinal),
                failure.Contains(SqliteTestDialect.SqliteProvider, StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ClaimDueAsync_ProviderThatIsNotRelational_GivesNotFoundForTheInboxAndTheOutbox()
    {
        // Arrange
        var contexts = new PooledDbContextFactory<MsgHatchDbContext>(
            new DbContextOptionsBuilder<MsgHatchDbContext>().UseInMemoryDatabase("msghatch").Options);
        var json = new MsgHatchJson(TestContentTypes.ARegistry());

        // Act
        var inbox = await new EntityFrameworkInboxStore(contexts, json).ClaimDueAsync(10, Now, TimeSpan.FromSeconds(30), Ct);
        var outbox = await new EntityFrameworkOutboxStore(contexts, json).ClaimDueAsync(TestChannel.Id, 10, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Equal([Errors.NotFound, Errors.NotFound], [inbox.Error?.Type, outbox.Error?.Type]);
    }

    [Theory]
    [MemberData(nameof(ClassifierRows))]
    public async Task Stores_TableThatDoesNotExist_GiveTheErrorTypeOfTheFirstStepThatKnowsTheException(
        string row, bool hostKnowsIt, int? dialectCode, int expected)
    {
        // Arrange
        var dialect = new SqliteTestDialect(errorType: dialectCode is { } code ? Errors.Find(code) : null);
        await using var provider = Build(
            o => o.UseSqlite(_database.ConnectionString).UseEntityFrameworkStoreDialect(dialect),
            builder => _ = hostKnowsIt
                ? builder.AddExceptionClassifier<MsgHatchDbContext, ConflictClassifier<MsgHatchDbContext>>()
                : builder.AddExceptionClassifier<MsgHatchDbContext, UnknownClassifier<MsgHatchDbContext>>());

        // Act
        var inbox = await provider.GetRequiredService<IInboxDispatchStore>().ClaimDueAsync(10, Now, TimeSpan.FromSeconds(30), Ct);
        var outbox = await provider.GetRequiredService<IOutboxDispatchStore>().ClaimDueAsync(TestChannel.Id, 10, Now, TimeSpan.FromSeconds(30), Ct);
        var registry = await provider.GetRequiredService<IRegistrySettingsStore>().GetEntriesAsync(Ct);

        // Assert
        Assert.Equal((row, $"{expected},{expected},{expected}"), (row, $"{inbox.Error?.Type.Code},{outbox.Error?.Type.Code},{registry.Error?.Type.Code}"));
    }

    [Fact]
    public async Task ClaimDueAsync_CancelledToken_GivesOperationCancelledAndAsksNoClassifier()
    {
        // Arrange
        var classifier = Substitute.For<IExceptionClassifier<MsgHatchDbContext>>();
        classifier.Classify(Arg.Any<Exception>()).Returns(Errors.Conflict);
        await using var provider = Build(o => o.UseSqlite(_database.ConnectionString), builder => builder.Services.AddSingleton(classifier));
        using var cancelled = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        await cancelled.CancelAsync();

        // Act
        var result = await provider.GetRequiredService<EntityFrameworkInboxStore>().ClaimDueAsync(10, Now, TimeSpan.FromSeconds(30), cancelled.Token);

        // Assert
        Assert.Equal(Errors.OperationCancelled, result.Error?.Type);
        classifier.DidNotReceive().Classify(Arg.Any<Exception>());
    }

    private static ServiceProvider Build(Action<DbContextOptionsBuilder> configure, Action<MsgHatchBuilder>? setup = null)
    {
        var services = TestHost.CreateServices();
        var builder = services.AddMsgHatch(static o => (o.EnableLogging, o.Roles) = (false, MsgHatchRoles.Consumer));
        setup?.Invoke(builder);
        builder
            .AddTestChannel()
            .UseEntityFrameworkStores(configure)
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .WithRouter(static (_, _) => Substitute.For<IMessageRouter<TestEvent>>())
                .AsChannel<TestEnvelope, TestEnvelopeComposer>(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope));
        return services.Build();
    }
}
