using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Broker.DependencyInjection;
using MsgHatch.Broker.Tests.Unit.TestTypes;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Broker.Tests.Unit.DependencyInjection;

/// <summary>The setup methods of the broker package (section 4.7): <c>AsBroker</c> and <c>UseBrokerClient</c>.</summary>
public sealed class BrokerExtensionsUnitTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AsBroker_Registration_AddsTheContentTypeBrokerWithItsNameAndVersion()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseInMemoryStores().AddTestEvent();
        await using var provider = services.Build();

        // Act
        var entries = provider.GetRequiredService<IContentTypeRegistry>().Entries;

        // Assert
        Assert.Equal(
            [("Settings:Broker", 1, typeof(BrokerChannelSettings)), ("Broker:TestMessage", 2, typeof(OutgoingMessage<BrokerEnvelope<TestPayload>>))],
            entries.Where(static entry => entry.ContentType.Contains("Broker", StringComparison.Ordinal))
                .Select(static entry => (entry.ContentType, entry.Version, entry.ClrType))
                .OrderByDescending(static entry => entry.ContentType, StringComparer.Ordinal));
    }

    [Fact]
    public async Task ProcessAsync_BrokerWithNoRetryConfiguration_StoresEnvelopesWith8TriesAndNoExpiry()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseInMemoryStores().AddTestEvent();
        await using var provider = services.Build();

        // Act
        await using (var scope = provider.CreateAsyncScope())
        {
            await scope.ServiceProvider.GetRequiredService<INotificationService>()
                .ProcessAsync(new TestEvent("1001"), new MessageContext(Guid.NewGuid(), Tenant: null, MessageFlags.None), Ct);
        }

        // Assert
        Assert.Equal(
            [
                (new DeliveryTarget(DeliveryTargetKind.Company, "c1"), "Broker:TestMessage", 8, (DateTime?)null, new BrokerEnvelope<TestPayload>(new TestPayload("c1"), BrokerRoutesRouter.Headers)),
                (new DeliveryTarget(DeliveryTargetKind.User, "u1"), "Broker:TestMessage", 8, null, new BrokerEnvelope<TestPayload>(new TestPayload("u1"))),
            ],
            provider.GetRequiredService<InMemoryOutboxStore>().GetMessages().Select(static message => (
                message.Target, message.ContentType, message.Delivery.MaxTryCount, message.Delivery.ExpiresAtUtc,
                ((OutgoingMessage<BrokerEnvelope<TestPayload>>)message).Envelope)));
    }

    [Fact]
    public async Task UseBrokerClient_ClientType_RegistersTheClientAndTheBrokerTransport()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseInMemoryStores().UseBrokerClient<FixedReceiptBrokerClient>();
        await using var provider = services.Build();
        var transport = Assert.Single(provider.GetServices<ITransport>());

        // Act
        var result = await transport.SendAsync(BrokerMessages.AnOutgoingMessage(), redirectTo: null, Ct);

        // Assert
        Assert.Equal((BrokerChannel.Id, FixedReceiptBrokerClient.Receipt), (transport.Channel, result.Value));
    }

    [Fact]
    public async Task UseBrokerClient_PublisherHostWithNoMessageType_AppliesTheTestDeliveryOfTheConfiguration()
    {
        // Arrange
        var client = Substitute.For<IBrokerClient>();
        client.PublishAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult<ErrorOr<PublishReceipt>>(new PublishReceipt(null)));
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { ["MsgHatch:TestDelivery:Broker:Mode"] = "Deliver" });
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Publisher).UseInMemoryStores().UseBrokerClient(_ => client);
        await using var provider = services.Build();
        await provider.GetRequiredService<IOutboxStore>().AddAsync([BrokerMessages.AnOutgoingMessage(flags: MessageFlags.Test)], Ct);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        await client.ReceivedWithAnyArgs(1).PublishAsync(default, Ct);
    }
}
