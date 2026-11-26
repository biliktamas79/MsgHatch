using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.MongoDb.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Data.MongoDb.Tests.Unit.DependencyInjection;

/// <summary>
/// The startup checks of <c>UseMongoDbDispatchSignal</c> (section 4.7, D190, D303). The validation resolves no client
/// and no signal, so no test sends a command to a server.
/// </summary>
public sealed class MongoDbDispatchSignalExtensionsUnitTests : IAsyncDisposable
{
    private readonly List<ServiceProvider> _hosts = [];

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }
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

    /// <summary>A host with the MongoDB stores on a closed port, which the validation never reaches.</summary>
    private ServiceProvider NewHost(Action<MsgHatchBuilder> more)
    {
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?>
        {
            ["MsgHatch:MongoDb:ConnectionString"] = "mongodb://127.0.0.1:1",
            ["MsgHatch:MongoDb:DatabaseName"] = "setup",
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
