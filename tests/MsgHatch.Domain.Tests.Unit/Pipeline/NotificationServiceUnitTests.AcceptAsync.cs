using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Pipeline;

public sealed partial class NotificationServiceUnitTests
{
    [Fact]
    public async Task AcceptAsync_EventWithATenantAndFlags_StoresAPendingReceivedMessageAtTheFakeTime()
    {
        // Arrange
        await using var provider = BuildWithInMemoryInbox();
        var context = AContext(_tenant, MessageFlags.Test);
        var now = _time.GetUtcNow().UtcDateTime;

        // Act
        var accepted = await AcceptAsync(provider, new TestEvent("Order 1001"), context);

        // Assert
        var stored = Assert.IsType<ReceivedMessage<TestEvent>>(Assert.Single(provider.GetRequiredService<InMemoryInboxStore>().GetMessages()));
        Assert.Equal(context.MessageId, accepted.Value);
        Assert.Equal(
            (context.MessageId, DeliveryStatus.Pending, 0, 8, now, now, (TenantId?)_tenant, MessageFlags.Test, new MessageType("TestEvent"), 2, new TestEvent("Order 1001")),
            (stored.Delivery.Id, stored.Delivery.Status, stored.Delivery.TryCount, stored.Delivery.MaxTryCount, stored.Delivery.CreatedAtUtc,
                stored.Delivery.NextTryAtUtc, stored.Tenant, stored.Flags, stored.Type, stored.ContentVersion, stored.Event));
    }

    [Fact]
    public async Task AcceptAsync_SameContextTwoTimes_GivesTheSameInboxIdAndStoresOneMessage()
    {
        // Arrange
        await using var provider = BuildWithInMemoryInbox();
        var context = AContext(_tenant);
        var first = await AcceptAsync(provider, new TestEvent("text"), context);

        // Act
        var second = await AcceptAsync(provider, new TestEvent("text"), context);

        // Assert
        Assert.Equal(first.Value, second.Value);
        Assert.Single(provider.GetRequiredService<InMemoryInboxStore>().GetMessages());
    }

    [Fact]
    public async Task AcceptAsync_SameIdWithTwoTenants_StoresTwoInboxMessages()
    {
        // Arrange
        await using var provider = BuildWithInMemoryInbox();
        var id = Guid.NewGuid();
        await AcceptAsync(provider, new TestEvent("text"), AContext(_tenant, id: id));

        // Act
        await AcceptAsync(provider, new TestEvent("text"), AContext(new TenantId("t2"), id: id));

        // Assert
        Assert.Equal(
            [(id, (TenantId?)_tenant), (id, new TenantId("t2"))],
            provider.GetRequiredService<InMemoryInboxStore>().GetMessages().Select(static m => (m.Delivery.Id, m.Tenant)));
    }

    [Fact]
    public async Task AcceptAsync_InboxRetryMaxTryCountInTheConfiguration_GivesTheMessageThatMaxTryCount()
    {
        // Arrange
        await using var provider = BuildWithInMemoryInbox(new Dictionary<string, string?> { ["MsgHatch:Retry:Inbox:MaxTryCount"] = "3" });

        // Act
        await AcceptAsync(provider, new TestEvent("text"), AContext());

        // Assert
        Assert.Equal(3, Assert.Single(provider.GetRequiredService<InMemoryInboxStore>().GetMessages()).Delivery.MaxTryCount);
    }

    [Fact]
    public async Task AcceptAsync_ContextWithATenant_UsesTheInboxStoreOfItsKey()
    {
        // Arrange
        await using var provider = _doubles.Build(_time);
        var context = AContext(_tenant);

        // Act
        await AcceptAsync(provider, new TestEvent("text"), context);

        // Assert
        await _doubles.Inbox(_t1).ReceivedWithAnyArgs(1).AddAsync(default!, Ct);
        Assert.Equal(context.MessageId, Assert.Single(_doubles.Accepted).Delivery.Id);
    }

    [Fact]
    public async Task AcceptAsync_UnregisteredEventType_ReturnsNotFound()
    {
        // Arrange
        await using var provider = _doubles.Build(_time);

        // Act
        var accepted = await AcceptAsync(provider, new OtherTestEvent("text"), AContext());

        // Assert
        Assert.Equal(Errors.NotFound, accepted.Error?.Type);
    }

    [Fact]
    public async Task AcceptAsync_InboxStoreError_ReturnsTheError()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        _doubles.Inbox(TenantKey.None).AddAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(expected));
        await using var provider = _doubles.Build(_time);

        // Act
        var accepted = await AcceptAsync(provider, new TestEvent("text"), AContext());

        // Assert
        Assert.Same(expected, accepted.Error);
    }

    [Fact]
    public async Task AcceptAsync_NoInboxStore_ReturnsNotFound()
    {
        // Arrange
        var services = TestHost.CreateServices(_time);
        services.AddMsgHatch()
            .AddTestChannel()
            .UseOutboxStore(static (_, _) => Substitute.For<Domain.Outbox.IOutboxStore>())
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .WithRouter<TwoRouteTestRouter>()
                .AsChannel(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>()));
        await using var provider = services.Build();

        // Act
        var accepted = await AcceptAsync(provider, new TestEvent("text"), AContext());

        // Assert
        Assert.Equal(Errors.NotFound, accepted.Error?.Type);
    }

    private ServiceProvider BuildWithInMemoryInbox(IReadOnlyDictionary<string, string?>? configuration = null)
    {
        var services = TestHost.CreateServices(_time, configuration);
        services.AddMsgHatch()
            .AddTestChannel()
            .UseInMemoryStores()
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .WithRouter<TwoRouteTestRouter>()
                .AsChannel<TestEnvelope, TestEnvelopeComposer>(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope));
        return services.Build();
    }

    private static async Task<ErrorOr<Guid>> AcceptAsync<TEvent>(IServiceProvider provider, TEvent domainEvent, MessageContext context)
    {
        await using var scope = provider.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<INotificationService>().AcceptAsync(domainEvent, context, Ct);
    }
}
