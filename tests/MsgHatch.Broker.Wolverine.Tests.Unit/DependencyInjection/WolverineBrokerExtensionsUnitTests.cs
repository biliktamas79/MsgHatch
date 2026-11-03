using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Broker.Tests.Unit.TestTypes;
using MsgHatch.Broker.Wolverine.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;
using Wolverine;
using Wolverine.Runtime;

namespace MsgHatch.Broker.Wolverine.Tests.Unit.DependencyInjection;

/// <summary>The setup method of the Wolverine broker package (section 4.7): <c>UseWolverineBroker</c> and its startup rule.</summary>
public sealed class WolverineBrokerExtensionsUnitTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task UseWolverineBroker_Alone_RegistersTheWolverineBrokerClientAndOneTransportOfTheBrokerChannel()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.EnableLogging = false).UseInMemoryStores().UseWolverineBroker();
        await using var provider = services.Build();

        // Act
        var (client, transports) = (provider.GetRequiredService<IBrokerClient>(), provider.GetServices<ITransport>());

        // Assert
        Assert.IsType<WolverineBrokerClient>(client);
        Assert.Equal(BrokerChannel.Id, Assert.Single(transports).Channel);
    }

    [Fact]
    public async Task UseWolverineBroker_RuntimeOfTheHost_PublishesThroughAMessageBusOfThatRuntime()
    {
        // Arrange
        var runtime = Substitute.For<IWolverineRuntime>();
        runtime.When(static r => r.AssertHasStarted()).Do(static _ => throw new WolverineHasNotStartedException());
        var services = TestHost.CreateServices();
        services.AddSingleton(runtime);
        services.AddMsgHatch().UseInMemoryStores().UseWolverineBroker();
        await using var provider = services.Build();

        // Act
        var result = await provider.GetRequiredService<ITransport>().SendAsync(BrokerMessages.AnOutgoingMessage(), redirectTo: null, Ct);

        // Assert
        Assert.IsType<WolverineHasNotStartedException>(result.Error?.Exception);
        runtime.Received(1).AssertHasStarted();
    }

    [Fact]
    public async Task Validate_UseWolverineBrokerWithNoWolverine_ThrowsOptionsValidationException()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Publisher).UseInMemoryStores().UseWolverineBroker();
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.IsType<OptionsValidationException>(exception);
    }

    [Fact]
    public async Task Validate_UseWolverineBrokerWithTheWolverineRuntime_Succeeds()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddSingleton(Substitute.For<IWolverineRuntime>());
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Publisher).UseInMemoryStores().UseWolverineBroker();
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Null(exception);
    }
}
