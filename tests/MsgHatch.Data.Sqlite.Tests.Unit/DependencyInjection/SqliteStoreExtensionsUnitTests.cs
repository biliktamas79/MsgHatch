using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.Sqlite.DependencyInjection;
using MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Data.Sqlite.Tests.Unit.DependencyInjection;

/// <summary><c>UseSqliteStores</c> alone registers all parts of the package (section 4.7, D60). Each test has its own database file.</summary>
public sealed class SqliteStoreExtensionsUnitTests : IDisposable
{
    private const string _key = "MsgHatch:Sqlite:ConnectionString";

    private readonly Dictionary<string, string?> _section = TestDatabases.NewSection();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTime Now => TestHost.Start.UtcDateTime;

    private string ConnectionString => _section[_key]!;

    public void Dispose() => TestDatabases.Delete(ConnectionString);

    [Fact]
    public async Task UseSqliteStores_Alone_ResolvesEachStoreAsItsLoggingWrapperAroundTheSqliteStores()
    {
        // Arrange
        await using var provider = Build(enableLogging: true);
        var received = TestMessages.AReceivedMessage(new TestEvent("text"));
        var outgoing = TestMessages.AnOutgoingMessage(channel: TestChannel.Id);
        await provider.GetRequiredService<IInboxStore>().AddAsync(received, Ct);
        await provider.GetRequiredService<IOutboxStore>().AddAsync([outgoing], Ct);

        // Act
        var inbox = await provider.GetRequiredService<IInboxDispatchStore>().ClaimDueAsync(10, Now, TimeSpan.FromSeconds(30), Ct);
        var outbox = await provider.GetRequiredService<IOutboxDispatchStore>().ClaimDueAsync(TestChannel.Id, 10, Now, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Equal([1200, 1202, 1300, 1304], provider.LogRecords().Select(static record => record.Id.Id).Order());
        Assert.Equal(
            [received.Delivery.Id, outgoing.Delivery.Id],
            [Assert.Single(inbox.Value.Messages).Delivery.Id, Assert.Single(outbox.Value.Messages).Delivery.Id]);
        Assert.Equal(
            [[received.Delivery.Id.ToString("D")], [outgoing.Delivery.Id.ToString("D")]],
            [.. TestDatabases.Rows(ConnectionString, "SELECT Id FROM MsgHatchInbox UNION ALL SELECT Id FROM MsgHatchOutbox")]);
    }

    [Fact]
    public async Task UseSqliteStores_LoggingDisabled_TheDispatchStoresAreTheOneInstanceOfEachSqliteStore()
    {
        // Arrange
        await using var provider = Build(enableLogging: false);

        // Act
        var inbox = provider.GetRequiredService<IInboxDispatchStore>();
        var outbox = provider.GetRequiredService<IOutboxDispatchStore>();

        // Assert
        Assert.Same(provider.GetRequiredService<SqliteInboxStore>(), inbox);
        Assert.Same(provider.GetRequiredService<SqliteOutboxStore>(), outbox);
    }

    [Fact]
    public async Task UseSqliteStores_ConnectionStringInTheSection_ReachesTheOptionsAndBothStores()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: _section);
        services.AddMsgHatch().UseSqliteStores();
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<SqliteStoreOptions>>().Value;

        // Assert
        Assert.Equal(
            [ConnectionString, ConnectionString, ConnectionString],
            [options.ConnectionString, provider.GetRequiredService<SqliteInboxStore>().ConnectionString, provider.GetRequiredService<SqliteOutboxStore>().ConnectionString]);
    }

    [Fact]
    public async Task UseSqliteStores_ConnectionStringFromCode_WinsOverTheSection()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { [_key] = "Data Source=from-the-section.db" });
        services.AddMsgHatch().UseSqliteStores(ConnectionString);
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<SqliteStoreOptions>>().Value;

        // Assert
        Assert.Equal(ConnectionString, options.ConnectionString);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("Data Source=data/msghatch-{tenant}.db")]
    public async Task UseSqliteStores_NoConnectionStringOrOneWithTheTenantPlaceholder_FailsTheValidationOnConnectionString(string? value)
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { [_key] = value });
        services.AddMsgHatch().UseSqliteStores();
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(() => provider.GetRequiredService<IOptions<SqliteStoreOptions>>().Value);

        // Assert
        var validation = Assert.IsType<OptionsValidationException>(exception);
        var failure = Assert.Single(validation.Failures);
        Assert.Equal("ConnectionString", failure[..failure.IndexOf(':', StringComparison.Ordinal)]);
    }

    [Fact]
    public async Task UseSqliteStores_AfterUseInMemoryStores_NamesBothSetupMethodsForEachStoreInterface()
    {
        // Arrange
        const string names = "UseInMemoryStores and UseSqliteStores select an implementation of ";
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseInMemoryStores().UseSqliteStores(ConnectionString);
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Equal(
            ["IInboxDispatchStore", "IInboxStore", "IOutboxDispatchStore", "IOutboxStore"],
            Assert.IsType<OptionsValidationException>(exception).Failures
                .Where(static failure => failure.StartsWith(names, StringComparison.Ordinal))
                .Select(static failure => failure[names.Length..^1])
                .Order(StringComparer.Ordinal));
    }

    private ServiceProvider Build(bool enableLogging)
    {
        var services = TestHost.CreateServices(configuration: _section);
        services.AddMsgHatch(o => o.EnableLogging = enableLogging)
            .AddTestChannel()
            .UseSqliteStores()
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .WithRouter(static (_, _) => Substitute.For<IMessageRouter<TestEvent>>())
                .AsChannel<TestEnvelope, TestEnvelopeComposer>(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope));
        return services.Build();
    }
}
