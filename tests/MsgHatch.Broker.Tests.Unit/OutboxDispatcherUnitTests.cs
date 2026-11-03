using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Broker.DependencyInjection;
using MsgHatch.Broker.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Broker.Tests.Unit;

/// <summary>
/// The broker channel from the event to the broker client, in one host with both roles: the pipeline puts the domain
/// event of <c>WithEventPayload</c> into the outbox, and the dispatcher publishes it through <see cref="IBrokerClient"/>
/// (section 4.8, D139). The broker client is the only substitute.
/// </summary>
public sealed class OutboxDispatcherUnitTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task DispatchPendingAsync_EventWithTheEventAsItsPayload_PublishesTheDomainEventForEachRouteThroughTheBrokerClient()
    {
        // Arrange
        var client = Substitute.For<IBrokerClient>();
        client.PublishAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult<ErrorOr<PublishReceipt>>(new PublishReceipt(null)));
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseInMemoryStores().UseBrokerClient(_ => client)
            .AddMessage("TestEvent", 1, TestJsonContext.Default.TestEvent, static m => m
                .WithRouter<BrokerRoutesRouter>()
                .WithEventPayload(static p => p.AsBroker("TestEvent", 1, BrokerTestJsonContext.Default.BrokerEnvelopeTestEvent)));
        await using var provider = services.Build();
        provider.GetRequiredService<IStartupValidator>().Validate();
        var domainEvent = new TestEvent("1001");
        await ProcessAsync(provider, domainEvent);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Equal(
            [domainEvent, domainEvent],
            client.ReceivedCalls().Select(static call => ((BrokerPublish)call.GetArguments()[0]!).Payload));
    }

    private static async Task ProcessAsync(IServiceProvider provider, TestEvent domainEvent)
    {
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<INotificationService>()
            .ProcessAsync(domainEvent, new MessageContext(Guid.NewGuid(), Tenant: null, MessageFlags.None), Ct);
    }
}
