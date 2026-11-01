using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;
using static MsgHatch.Domain.Tests.Unit.TestTypes.TestRoutes;

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

    [Fact]
    public async Task ProcessAsync_HostStopsWhileTheSecondChannelComposesThenTheEventComesAgain_StoresEachMessageOneTimeWithNoTry()
    {
        // Arrange
        var secondComposer = AnOtherEnvelopeComposer();
        secondComposer.WhenForAnyArgs(static c => c.ComposeAsync(default!, default, default))
            .Do(static call => call.ArgAt<CancellationToken>(2).ThrowIfCancellationRequested());
        await using var provider = BuildWithInMemoryStoresAndTwoChannels(secondComposer);
        var outbox = provider.GetRequiredService<InMemoryOutboxStore>();
        var context = AContext(_tenant);
        using var stop = new CancellationTokenSource();
        await stop.CancelAsync();

        // Act: the host stops while the second channel composes
        var stopped = await ProcessAsync(provider, context, stop.Token);

        // Assert: the event fails, so the inbox or the broker gives it again, and no message of the first channel is stored
        Assert.Equal(Errors.OperationCancelled, stopped.Error?.Type);
        Assert.Empty(outbox.GetMessages());

        // Act: the event comes again
        var again = await ProcessAsync(provider, context);

        // Assert
        Assert.Equal(2, again.Value.Stored);
        Assert.Equal([("u1", 0), ("u2", 0)], outbox.GetMessages().Select(static m => (m.Target.Id, m.Delivery.TryCount)));
    }

    [Fact]
    public async Task ProcessAsync_SameEventAgainAfterAStopBeforeItsAcknowledgment_KeepsOneCopyOfEachMessageWithNoTry()
    {
        // Arrange
        await using var provider = BuildWithInMemoryStoresAndTwoChannels(AnOtherEnvelopeComposer());
        var outbox = provider.GetRequiredService<InMemoryOutboxStore>();
        var context = AContext(_tenant);
        await ProcessAsync(provider, context); // stored, but the host stops before the inbox or the broker acknowledges the event

        // Act
        await ProcessAsync(provider, context);

        // Assert
        Assert.Equal([("u1", 0), ("u2", 0)], outbox.GetMessages().Select(static m => (m.Target.Id, m.Delivery.TryCount)));
    }

    private static IEnvelopeComposer<TestEvent, OtherTestEnvelope> AnOtherEnvelopeComposer() =>
        PipelineDoubles.EchoComposer<TestEvent, OtherTestEnvelope>(static route => new OtherTestEnvelope(PipelineDoubles.TextOf(route)));

    private ServiceProvider BuildWithInMemoryStoresAndTwoChannels(IEnvelopeComposer<TestEvent, OtherTestEnvelope> secondComposer)
    {
        MessageRoute[] routes = [ARoute("u1"), ARoute("u2", channel: TestChannel.SecondId, settings: new OtherTestChannelSettings())];
        var services = TestHost.CreateServices(_time);
        services.AddMsgHatch()
            .AddTestChannel()
            .AddSecondTestChannel()
            .UseInMemoryStores()
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, m => m
                .WithRouter((_, _) => PipelineDoubles.RouterGiving<TestEvent>(() => routes))
                .AsChannel(
                    TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope,
                    static (_, _) => PipelineDoubles.EchoComposer<TestEvent, TestEnvelope>(static route => new TestEnvelope(PipelineDoubles.TextOf(route))))
                .AsChannel(TestChannel.SecondId, "OtherEnvelope", 1, TestJsonContext.Default.OtherTestEnvelope, (_, _) => secondComposer));
        return services.Build();
    }
}
