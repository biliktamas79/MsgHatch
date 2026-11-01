using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Pipeline;

public sealed partial class NotificationServiceUnitTests
{
    [Fact]
    public async Task ProcessAsync_InMemoryStoresTestRouterAndTestComposer_StoresTwoMessagesInTheInMemoryOutbox()
    {
        // Arrange
        var services = TestHost.CreateServices(_time);
        services.AddMsgHatch()
            .AddTestChannel()
            .UseInMemoryStores()
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .WithRouter<TwoRouteTestRouter>()
                .AsChannel<TestEnvelope, TestEnvelopeComposer>(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope));
        await using var provider = services.Build();

        // Act
        var result = await ProcessAsync(provider, new TestEvent("Order 1001"), AContext(_tenant));

        // Assert
        Assert.Equal(2, result.Value.Stored);
        Assert.Equal(
            [
                ((TenantId?)_tenant, new MessageType("TestEvent"), new DeliveryTarget(DeliveryTargetKind.User, "u1"),
                    (RouteTrace)new TestTrace("TestRouter"), (object)new TestEnvelope("Order 1001 for client1@example.com")),
                (_tenant, new MessageType("TestEvent"), new DeliveryTarget(DeliveryTargetKind.User, "u2"),
                    new TestTrace("TestRouter"), new TestEnvelope("Order 1001 for client2@example.com")),
            ],
            provider.GetRequiredService<InMemoryOutboxStore>().GetMessages()
                .Select(static m => (m.Tenant, m.Type, m.Target, m.Trace, m.Content)));
    }
}
