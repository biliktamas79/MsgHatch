using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.MongoDb.DependencyInjection;
using MsgHatch.Data.MongoDb.Tests.Integration.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;
using static MsgHatch.Data.MongoDb.Tests.Integration.TestTypes.SignalWatch;

namespace MsgHatch.Data.MongoDb.Tests.Integration.DependencyInjection;

/// <summary>
/// <c>UseMongoDbDispatchSignal</c> and its startup checks (section 4.7, D190, D303): two hosts on one database of the
/// MongoDB stores, where an add or a notify of the first host wakes a wait of the second host.
/// </summary>
public sealed class MongoDbDispatchSignalExtensionsIntegTests(MongoDbFixture server) : IAsyncDisposable
{
    private readonly List<ServiceProvider> _hosts = [];

    private readonly TestDatabase _database = server.Database($"setup{Guid.NewGuid():N}"[..17]);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }
    }

    [Theory]
    [InlineData("inbox")]
    [InlineData("outbox")]
    [InlineData("notify")]
    public async Task UseMongoDbDispatchSignal_TwoHostsOnOneDatabase_TheChangeOfTheFirstHostWakesTheWaitOfTheSecond(string change)
    {
        // Arrange
        var first = NewHost(static builder => builder.UseMongoDbDispatchSignal());
        var signal = NewHost(static builder => builder.UseMongoDbDispatchSignal()).GetRequiredService<IDispatchSignal>();
        await OpenAsync(signal, _database);
        var topic = change == "inbox" ? DispatchTopic.Inbox : DispatchTopic.ForChannel(TestChannel.Id);
        var wait = signal.WaitAsync(topic, Ct);

        // Act
        await ChangeAsync(first, change, topic);

        // Assert
        Assert.Null(await CompletedAsync(wait));
    }

    [Fact]
    public void Validate_UseMongoDbDispatchSignalWithTheMongoDbStores_Succeeds()
    {
        // Arrange
        var host = NewHost(static builder => builder.UseMongoDbDispatchSignal());

        // Act
        var exception = Record.Exception(host.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }

    [Fact]
    public async Task Validate_UseMongoDbDispatchSignalWithNoMongoDbStores_ThrowsOptionsValidationExceptionThatNamesUseMongoDbStores()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddTestChannel().UseTransport(static _ => TestTransports.For(TestChannel.Id)).UseInMemoryStores()
            .UseMongoDbDispatchSignal();
        await using var host = services.Build();

        // Act
        var exception = Record.Exception(host.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.StartsWith("UseMongoDbDispatchSignal needs the MongoDB stores of UseMongoDbStores", failure, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Inbox")]
    [InlineData("Outbox")]
    public void Validate_NoPollingForOneDispatcher_ThrowsOptionsValidationExceptionForItsOptions(string dispatcher)
    {
        // Arrange
        var host = NewHost(builder => builder.UseMongoDbDispatchSignal()
            .Services.Configure<DispatchOptions>(dispatcher, static o => o.EnablePolling = false));

        // Act
        var exception = Record.Exception(host.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.IsType<OptionsValidationException>(exception);
        Assert.Equal((typeof(DispatchOptions), dispatcher), (failure.OptionsType, failure.OptionsName));
        Assert.StartsWith("UseMongoDbDispatchSignal needs EnablePolling ", Assert.Single(failure.Failures), StringComparison.Ordinal);
    }

    [Fact]
    public void Validate_AfterUseDispatchSignal_NamesBothSetupMethods()
    {
        // Arrange
        var host = NewHost(static builder => builder.UseDispatchSignal(static _ => new InProcessDispatchSignal()).UseMongoDbDispatchSignal());

        // Act
        var exception = Record.Exception(host.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.StartsWith("UseDispatchSignal and UseMongoDbDispatchSignal select ", failure, StringComparison.Ordinal);
    }

    private static async Task ChangeAsync(ServiceProvider host, string change, DispatchTopic topic)
    {
        if (change == "inbox")
        {
            await host.GetRequiredService<IInboxStore>().AddAsync(TestMessages.AReceivedMessage(new TestEvent("text")), Ct);
        }
        else if (change == "outbox")
        {
            await host.GetRequiredService<IOutboxStore>().AddAsync([TestMessages.AnOutgoingMessage(channel: TestChannel.Id)], Ct);
        }
        else
        {
            await host.GetRequiredService<IDispatchSignal>().NotifyAsync(topic, Ct);
        }
    }

    /// <summary>A host with the MongoDB stores on the database of the test, as a separate process on another machine is.</summary>
    private ServiceProvider NewHost(Action<MsgHatchBuilder> more)
    {
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?>
        {
            ["MsgHatch:MongoDb:ConnectionString"] = server.ConnectionString,
            ["MsgHatch:MongoDb:DatabaseName"] = _database.Database.DatabaseNamespace.DatabaseName,
        });
        var builder = services.AddMsgHatch()
            .AddTestChannel()
            .UseTransport(static _ => TestTransports.For(TestChannel.Id))
            .UseMongoDbStores()
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .WithRouter(static (_, _) => Substitute.For<IMessageRouter<TestEvent>>())
                .AsChannel<TestEnvelope, TestEnvelopeComposer>(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope));
        more(builder);
        var host = services.Build();
        _hosts.Add(host);
        return host;
    }
}
