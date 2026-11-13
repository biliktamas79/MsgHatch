using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Management;

/// <summary>
/// The delivery manager (section 4.19) through the setup of a host, with substitutes for the stores and the dispatch
/// signal. The test channel registers the content type <c>Test:TestEnvelope</c> with three tries and
/// <c>Test:OtherEnvelope</c> with the default of eight tries.
/// </summary>
public sealed partial class DeliveryManagerUnitTests : IAsyncDisposable
{
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));
    private static readonly MessageType _type = new("TestEvent");
    private readonly FakeTimeProvider _time = TestHost.NewFakeTime();
    private readonly IOutboxStore _outbox = Substitute.For<IOutboxStore>();
    private readonly IInboxStore _inbox = Substitute.For<IInboxStore>();
    private readonly IOutboxDispatchStore _outboxDispatch = Substitute.For<IOutboxDispatchStore>();
    private readonly IInboxDispatchStore _inboxDispatch = Substitute.For<IInboxDispatchStore>();
    private readonly IDispatchSignal _signal = Substitute.For<IDispatchSignal>();
    private readonly List<ServiceProvider> _providers = [];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTime Now => _time.GetUtcNow().UtcDateTime;

    public async ValueTask DisposeAsync()
    {
        foreach (var provider in _providers)
        {
            await provider.DisposeAsync();
        }
    }

    [Fact]
    public async Task GetChannelsAsync_RegisteredChannels_GivesEachWithItsDownStateItsPendingCountsAndItsOldestDueTime()
    {
        // Arrange
        var pause = new ChannelDown(TestChannel.Id, ChannelDownReason.Operator, Now, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, "note");
        _outboxDispatch.GetDownChannelsAsync(Ct).Returns(ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelDown>>.ForValue([pause])));
        IReadOnlyList<MessageCount> counts =
        [
            new(_type, TestChannel.Id, DeliveryStatus.Pending, Test: false, 2, Now.AddMinutes(-5)),
            new(_type, TestChannel.Id, DeliveryStatus.Pending, Test: true, 1, Now.AddMinutes(-9)),
            new(new MessageType("OtherTestEvent"), TestChannel.Id, DeliveryStatus.Pending, Test: false, 4, Now.AddMinutes(-1)),
            new(_type, TestChannel.ThirdId, DeliveryStatus.Pending, Test: false, 7, Now),
        ];
        _outboxDispatch.CountAsync(new TrafficQuery(Status: DeliveryStatus.Pending, IncludeTest: true), Ct)
            .Returns(ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageCount>>.ForValue(counts)));

        // Act
        var channels = await Manager().GetChannelsAsync(Ct);

        // Assert
        Assert.Equal(
            [
                new ChannelStatus(TestChannel.Descriptor, pause, 6, 1, Now.AddMinutes(-9)),
                new ChannelStatus(TestChannel.SecondDescriptor, Down: null, 0, 0, OldestDueAtUtc: null),
            ],
            channels.Value);
        Assert.Equal(2, _outboxDispatch.ReceivedCalls().Count());
    }

    [Fact]
    public async Task GetTrafficAsync_Query_GivesTheCountsOfTheInboxAndTheOutboxOfTheTenant()
    {
        // Arrange
        var query = new TrafficQuery(Status: DeliveryStatus.Failed);
        IReadOnlyList<MessageCount> inbox = [new(_type, null, DeliveryStatus.Failed, Test: false, 1, null)];
        IReadOnlyList<MessageCount> outbox = [new(_type, TestChannel.Id, DeliveryStatus.Failed, Test: false, 3, null)];
        _inbox.CountAsync(query, Ct).Returns(ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageCount>>.ForValue(inbox)));
        _outbox.CountAsync(query, Ct).Returns(ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageCount>>.ForValue(outbox)));

        // Act
        var report = await Manager(_t1).GetTrafficAsync(query, Ct);

        // Assert
        Assert.Equal((inbox, outbox), (report.Value.Inbox, report.Value.Outbox));
    }

    [Fact]
    public async Task GetOutgoingMessageAsync_StoredMessage_GivesItsSummaryTraceAndTriesWithNoEnvelope()
    {
        // Arrange
        var message = TestMessages.AnOutgoingMessage(trace: new TestTrace("rule"));
        IReadOnlyList<PublishTry> tries = [PublishTry.Cancel(message.Delivery.Id, 1, Now)];
        _outbox.GetAsync(message.Delivery.Id, Ct)
            .Returns(ValueTask.FromResult(ErrorOr<OutgoingMessageHistory?>.ForValue(new OutgoingMessageHistory(message, tries))));

        // Act
        var detail = await Manager().GetOutgoingMessageAsync(message.Delivery.Id, Ct);

        // Assert
        Assert.Equal(new OutgoingMessageDetail(OutgoingMessageSummary.Of(message), message.Trace, tries), detail.Value);
    }

    [Fact]
    public async Task GetOutgoingMessageAsync_UnknownIdOrIdOfAnotherTenant_GivesNotFound()
    {
        // Arrange
        _outbox.GetAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<OutgoingMessageHistory?>.ForValue(null)));

        // Act
        var detail = await Manager(_t1).GetOutgoingMessageAsync(Guid.NewGuid(), Ct);

        // Assert
        Assert.Equal(Errors.NotFound, detail.Error?.Type);
    }

    [Fact]
    public async Task GetOutgoingContentAsync_StoredMessage_GivesTheWholeMessageWithItsEnvelope()
    {
        // Arrange
        var message = TestMessages.AnOutgoingMessage();
        _outbox.GetAsync(message.Delivery.Id, Ct)
            .Returns(ValueTask.FromResult(ErrorOr<OutgoingMessageHistory?>.ForValue(new OutgoingMessageHistory(message, []))));

        // Act
        var content = await Manager().GetOutgoingContentAsync(message.Delivery.Id, Ct);

        // Assert
        Assert.Same(message, content.Value);
    }

    [Fact]
    public async Task GetReceivedContentAsync_StoredMessage_GivesTheWholeMessageWithItsEvent()
    {
        // Arrange
        var message = TestMessages.AReceivedMessage(new TestEvent("text"));
        _inbox.GetAsync(message.Delivery.Id, Ct).Returns(ValueTask.FromResult(ErrorOr<ReceivedMessage?>.ForValue(message)));

        // Act
        var content = await Manager().GetReceivedContentAsync(message.Delivery.Id, Ct);

        // Assert
        Assert.Same(message, content.Value);
    }

    [Fact]
    public async Task GetReceivedContentAsync_UnknownId_GivesNotFound()
    {
        // Arrange
        _inbox.GetAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<ReceivedMessage?>.ForValue(null)));

        // Act
        var content = await Manager().GetReceivedContentAsync(Guid.NewGuid(), Ct);

        // Assert
        Assert.Equal(Errors.NotFound, content.Error?.Type);
    }

    [Fact]
    public async Task GetReceivedMessagesAsync_NoInboxStore_GivesAnEmptyPage()
    {
        // Act
        var page = await Manager(withInbox: false).GetReceivedMessagesAsync(new InboxQuery(), Ct);

        // Assert
        Assert.Equal((0, (MessageCursor?)null), (page.Value.Items.Count, page.Value.Next));
    }

    [Fact]
    public async Task GetTrafficAsync_NoInboxStore_GivesEmptyInboxCounts()
    {
        // Arrange
        _outbox.CountAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageCount>>.ForValue([])));

        // Act
        var report = await Manager(withInbox: false).GetTrafficAsync(new TrafficQuery(), Ct);

        // Assert
        Assert.Empty(report.Value.Inbox);
    }

    [Fact]
    public async Task GetReceivedContentAsync_NoInboxStore_GivesNotFound()
    {
        // Act
        var content = await Manager(withInbox: false).GetReceivedContentAsync(Guid.NewGuid(), Ct);

        // Assert
        Assert.Equal(Errors.NotFound, content.Error?.Type);
    }

    [Fact]
    public async Task GetOutgoingMessagesAsync_Query_GivesThePageOfTheTenantStore()
    {
        // Arrange
        var query = new OutboxQuery(Status: DeliveryStatus.Failed, Limit: 1);
        var page = new MessagePage<OutgoingMessageSummary>([ASummary()], new MessageCursor(Now, Guid.NewGuid()));
        _outbox.QueryAsync(query, Ct).Returns(ValueTask.FromResult(ErrorOr<MessagePage<OutgoingMessageSummary>>.ForValue(page)));

        // Act
        var result = await Manager(_t1).GetOutgoingMessagesAsync(query, Ct);

        // Assert
        Assert.Same(page, result.Value);
    }

    private IDeliveryManager Manager(TenantKey key = default, bool withInbox = true)
    {
        var services = TestHost.CreateServices(_time);
        var builder = services.AddMsgHatch()
            .AddTestChannel()
            .AddSecondTestChannel()
            .UseRouter(typeof(TestOpenRouter<>))
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, static m => m
                .AsChannel(
                    TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>(),
                    configure: static channel => channel.WithRetry(static o => o.MaxTryCount = 3)))
            .AddMessage("OtherTestEvent", 1, TestJsonContext.Default.OtherTestEvent, static m => m
                .AsChannel(TestChannel.Id, "OtherEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<OtherTestEvent>()))
            .UseOutboxStore((_, _) => _outbox)
            .UseOutboxDispatchStore(_ => _outboxDispatch)
            .UseDispatchSignal(_ => _signal);
        WithInbox(builder, withInbox);
        var provider = services.Build();
        _providers.Add(provider);
        return provider.GetRequiredKeyedService<IDeliveryManager>(key);
    }

    private void WithInbox(MsgHatchBuilder builder, bool withInbox)
    {
        if (withInbox)
        {
            builder.UseInboxStore((_, _) => _inbox).UseInboxDispatchStore(_ => _inboxDispatch);
        }
    }

    private static OutgoingMessageSummary ASummary(string contentType = "Test:TestEnvelope") =>
        OutgoingMessageSummary.Of(TestMessages.AnOutgoingMessage()) with { ContentType = contentType };

    private static ReceivedMessageSummary AReceivedSummary() => ReceivedMessageSummary.Of(TestMessages.AReceivedMessage(new TestEvent("text")));
}
