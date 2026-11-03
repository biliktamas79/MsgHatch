using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using System.Globalization;
using System.Threading.Channels;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Telemetry;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using static MsgHatch.Domain.Tests.Unit.TestTypes.TestRoutes;

namespace MsgHatch.Domain.Tests.Unit.Dispatch;

/// <summary>The inbox processor through <see cref="IInboxProcessor"/> (section 4.8, the processing of one claimed received message).</summary>
[Collection(SequentialTelemetry.Name)]
public sealed class InboxProcessorUnitTests
{
    private const string _messageTraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
    private static readonly Guid _fixedId = Guid.Parse("11223344-5566-7788-99aa-bbccddeeff00");
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    private readonly WaitRecordingTimeProvider _time = new();
    private readonly IMessageRouter<TestEvent> _router = PipelineDoubles.RouterGiving<TestEvent>(static () => [ARoute("u1", address: "client1@example.com")]);
    private readonly ConcurrentQueue<TenantKey> _routerKeys = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ProcessPendingAsync_ClaimedMessageOfATenant_RunsThePipelineWithTheServicesOfItsTenantAndCompletesIt()
    {
        // Arrange
        await using var provider = Build();
        var message = await AddAsync(provider, AMessage(_t1, "Order 1001"));

        // Act
        var summary = await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Assert
        Assert.Equal(new DispatchSummary(1, 1, 0, 0), summary.Value);
        Assert.Equal([_t1], _routerKeys);
        Assert.Equal(new TestEvent("Order 1001"), RoutedMessage().Event);
        Assert.Equal(DeliveryStatus.Done, Stored(provider, message).Delivery.Status);
        var stored = Assert.Single(provider.GetRequiredService<InMemoryOutboxStore>().GetMessages());
        Assert.Equal(((TenantId?)_t1.Tenant, (object)new TestEnvelope("Order 1001 for client1@example.com")), (stored.Tenant, stored.Content));
    }

    [Fact]
    public async Task ProcessPendingAsync_OldVersion_RoutesTheEventThatTheRegisteredFunctionUpgraded()
    {
        // Arrange
        await using var provider = Build();
        await provider.GetRequiredService<InMemoryInboxStore>().For(TenantKey.None)
            .AddAsync(TestMessages.AReceivedMessage(new TestEventV1("old"), contentVersion: 1), Ct);

        // Act
        await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Assert
        var routed = RoutedMessage();
        Assert.Equal((new TestEvent("old upgraded"), 2), (routed.Event, routed.ContentVersion));
    }

    [Fact]
    public async Task ProcessPendingAsync_EventThatWaitedOneHourAndAnExpiryOfFiveMinutes_GivesAnExpiryFiveMinutesAfterTheAcceptTime()
    {
        // Arrange
        await using var provider = Build(more: static b => b.Services.Configure<RetryOptions>("Test:TestEnvelope", static o => o.Expiry = TimeSpan.FromMinutes(5)));
        var acceptedAtUtc = _time.UtcNow.AddHours(-1);
        await AddAsync(provider, AMessage() with { Delivery = TestMessages.ADeliveryState() with { CreatedAtUtc = acceptedAtUtc, NextTryAtUtc = acceptedAtUtc } });

        // Act
        await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Assert
        var delivery = Assert.Single(provider.GetRequiredService<InMemoryOutboxStore>().GetMessages()).Delivery;
        Assert.Equal(((DateTime?)acceptedAtUtc.AddMinutes(5), _time.UtcNow), (delivery.ExpiresAtUtc, delivery.CreatedAtUtc));
    }

    [Fact]
    public async Task ProcessPendingAsync_NewerVersionOfTheCurrentEventType_FailsWithATransientUnsupportedVersionAndDoesNotRunThePipeline()
    {
        // Arrange
        await using var provider = Build();
        var message = await AddAsync(provider, AMessage() with { ContentVersion = 3 });

        // Act
        var summary = await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Assert
        var delivery = Stored(provider, message).Delivery;
        Assert.Equal(
            (DeliveryStatus.Pending, true, 1),
            (delivery.Status, delivery.LastError?.StartsWith("UnsupportedVersion", StringComparison.Ordinal), summary.Value.Retried));
        Assert.Empty(_router.ReceivedCalls());
    }

    [Fact]
    public async Task ProcessPendingAsync_TokenCancelledDuringThePipeline_RecordsNothingAndTheNextClaimGetsTheMessageAfterItsLease()
    {
        // Arrange
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        _router.RouteAsync(default!, Ct).ReturnsForAnyArgs(
            _ =>
            {
                stop.Cancel();
                return ValueTask.FromResult<ErrorOr<IReadOnlyList<MessageRoute>>>(new Error(Errors.OperationCancelled));
            },
            _ => ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageRoute>>.ForValue([])));
        await using var provider = Build();
        var message = await AddAsync(provider, AMessage());
        var processor = provider.GetRequiredService<IInboxProcessor>();
        await processor.ProcessPendingAsync(10, stop.Token);
        var afterTheStop = Stored(provider, message).Delivery;
        _time.Advance(TimeSpan.FromSeconds(60));

        // Act
        var summary = await processor.ProcessPendingAsync(10, Ct);

        // Assert
        Assert.Equal((0, (DateTime?)null, DeliveryStatus.Pending), (afterTheStop.TryCount, afterTheStop.LastTryAtUtc, afterTheStop.Status));
        Assert.Equal(new DispatchSummary(1, 1, 0, 0), summary.Value);
    }

    [Fact]
    public async Task ProcessPendingAsync_TwoMessages_CreatesANewDiScopeForEach()
    {
        // Arrange
        await using var provider = Build();
        await AddAsync(provider, AMessage());
        await AddAsync(provider, AMessage());

        // Act
        await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Assert
        Assert.Equal([TenantKey.None, TenantKey.None], _routerKeys);
    }

    [Fact]
    public async Task ProcessPendingAsync_TransientRouterError_FailsTheMessageWithTheExactNextTryTime()
    {
        // Arrange
        RouterGives(new Error(Errors.RateLimited, "Too many"));
        await using var provider = Build();
        var message = await AddAsync(provider, AMessage(id: _fixedId));

        // Act
        var summary = await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Assert
        var delivery = Stored(provider, message).Delivery;
        Assert.Equal(
            (DeliveryStatus.Pending, 1, DateTime.Parse("2030-01-01T00:00:05.1178373Z", CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal), "RateLimited: Too many", 1),
            (delivery.Status, delivery.TryCount, delivery.NextTryAtUtc, delivery.LastError, summary.Value.Retried));
    }

    [Fact]
    public async Task ProcessPendingAsync_PermanentRouterError_FailsTheMessagePermanently()
    {
        // Arrange
        RouterGives(new Error(Errors.ValidationFailed));
        await using var provider = Build();
        var message = await AddAsync(provider, AMessage());

        // Act
        var summary = await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Assert
        Assert.Equal((DeliveryStatus.Failed, "ValidationFailed", 1), (Stored(provider, message).Delivery.Status, Stored(provider, message).Delivery.LastError, summary.Value.Failed));
    }

    [Fact]
    public async Task ProcessPendingAsync_TransientRouterErrorOnTheLastTry_FailsTheMessagePermanently()
    {
        // Arrange
        RouterGives(new Error(Errors.RateLimited));
        await using var provider = Build();
        var message = await AddAsync(provider, AMessage(maxTryCount: 1));

        // Act
        await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Assert
        Assert.Equal(DeliveryStatus.Failed, Stored(provider, message).Delivery.Status);
    }

    [Fact]
    public async Task ProcessPendingAsync_RouterThatThrows_FailsTheMessageWithExceptionAndANextTryTime()
    {
        // Arrange
        _router.RouteAsync(default!, Ct).ThrowsForAnyArgs(new InvalidOperationException("defective router"));
        await using var provider = Build();
        var message = await AddAsync(provider, AMessage());

        // Act
        await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Assert
        var delivery = Stored(provider, message).Delivery;
        Assert.Equal((DeliveryStatus.Pending, "Exception: defective router"), (delivery.Status, delivery.LastError));
    }

    [Fact]
    public async Task ProcessPendingAsync_UnregisteredMessageType_FailsTheMessagePermanentlyWithNotFound()
    {
        // Arrange
        await using var provider = Build();
        var message = await AddAsync(provider, AMessage() with { Type = new MessageType("Unknown") });

        // Act
        await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Assert
        var delivery = Stored(provider, message).Delivery;
        Assert.Equal((DeliveryStatus.Failed, true), (delivery.Status, delivery.LastError?.StartsWith("NotFound", StringComparison.Ordinal)));
    }

    [Fact]
    public async Task ProcessPendingAsync_StoreThatThrowsForTheFirstMessage_LogsExceptionAndCompletesTheNextMessage()
    {
        // Arrange
        var (first, second) = (AMessage(), AMessage());
        var store = Substitute.For<IInboxDispatchStore>();
        store.ClaimDueAsync(default, default, default, Ct).ReturnsForAnyArgs(
            ValueTask.FromResult(ErrorOr<ClaimResult<ReceivedMessage>>.ForValue(new ClaimResult<ReceivedMessage>([first, second], null))));
        store.CompleteAsync(first, Arg.Any<DateTime>(), Arg.Any<CancellationToken>()).Throws(new InvalidOperationException("defective store"));
        await using var provider = Build(more: b => b.UseInboxDispatchStore(_ => store).ConfigureInboxDispatch(static o => o.MaxDegreeOfParallelism = 1));

        // Act
        await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Assert
        await store.Received(1).CompleteAsync(second, Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        var record = Assert.Single(provider.LogRecords(), static r => r.Id.Id == 1401);
        Assert.Equal((LogLevel.Error, "500"), (record.Level, record.GetStructuredStateValue("ErrorCode")));
    }

    [Fact]
    public async Task ProcessPendingAsync_TestMessage_GivesTheTestFlagToEachOutgoingMessage()
    {
        // Arrange
        await using var provider = Build();
        await AddAsync(provider, AMessage() with { Flags = MessageFlags.Test });

        // Act
        await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Assert
        Assert.Equal([MessageFlags.Test], provider.GetRequiredService<InMemoryOutboxStore>().GetMessages().Select(static m => m.Flags));
    }

    [Fact]
    public async Task ProcessPendingAsync_ListenerOnTheSource_GivesAProcessSpanInANewTraceWithALinkToTheReceivedMessage()
    {
        // Arrange
        await using var provider = Build();
        var message = await AddAsync(provider, AMessage(_t1) with { TraceParent = _messageTraceParent });
        var spans = new ConcurrentQueue<Activity>();
        using var listener = SequentialTelemetry.ListenTo([MsgHatchTelemetry.SourceName, SequentialTelemetry.TestSource.Name], spans);
        using var parent = SequentialTelemetry.TestSource.StartActivity("test")!;

        // Act
        await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Assert
        var span = Assert.Single(spans, s => Equals(s.GetTagItem("messaging.message.id"), message.Delivery.Id.ToString()));
        var link = Assert.Single(span.Links);
        Assert.Equal(
            ("process TestEvent", ActivityKind.Consumer, default(ActivitySpanId), false, "4bf92f3577b34da6a3ce929d0e0e4736", (object?)"t1"),
            (span.DisplayName, span.Kind, span.ParentSpanId, span.TraceId == parent.TraceId, link.Context.TraceId.ToString(), span.GetTagItem("msghatch.tenant")));
    }

    [Fact]
    public async Task ProcessPendingAsync_ListenerOnTheSource_GivesEachOutgoingMessageTheIdOfTheProcessSpanAsTraceParent()
    {
        // Arrange
        await using var provider = Build();
        var message = await AddAsync(provider, AMessage() with { TraceParent = _messageTraceParent });
        var spans = new ConcurrentQueue<Activity>();
        using var listener = SequentialTelemetry.ListenTo([MsgHatchTelemetry.SourceName], spans);

        // Act
        await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Assert
        var span = Assert.Single(spans, s => Equals(s.GetTagItem("messaging.message.id"), message.Delivery.Id.ToString()));
        Assert.Equal(span.Id, Assert.Single(provider.GetRequiredService<InMemoryOutboxStore>().GetMessages()).TraceParent);
    }

    [Fact]
    public async Task ProcessPendingAsync_ClaimedMessage_RecordsOneProcessDurationAndCountsNoReceivedEventAgain()
    {
        // Arrange
        await using var provider = Build();
        await AddAsync(provider, AMessage());
        var meters = provider.GetRequiredService<IMeterFactory>();
        using var duration = new MetricCollector<double>(meters, MsgHatchTelemetry.MeterName, "msghatch.process.duration");
        using var received = new MetricCollector<long>(meters, MsgHatchTelemetry.MeterName, "msghatch.messages.received");

        // Act
        await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Assert
        Assert.Equal((1, 0), (duration.GetMeasurementSnapshot().Count, received.GetMeasurementSnapshot().Count));
    }

    [Fact]
    public async Task ProcessPendingAsync_BatchSizeZero_ReturnsValidationFailed()
    {
        // Arrange
        await using var provider = Build();

        // Act
        var summary = await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(0, Ct);

        // Assert
        Assert.Equal(Errors.ValidationFailed, summary.Error?.Type);
    }

    [Fact]
    public async Task ProcessPendingAsync_ClaimError_ReturnsTheError()
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        var store = Substitute.For<IInboxDispatchStore>();
        store.ClaimDueAsync(default, default, default, Ct).ReturnsForAnyArgs(ValueTask.FromResult<ErrorOr<ClaimResult<ReceivedMessage>>>(error));
        await using var provider = Build(more: b => b.UseInboxDispatchStore(_ => store));

        // Act
        var summary = await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Assert
        Assert.Same(error, summary.Error);
    }

    [Fact]
    public async Task RunAsync_NoPollingAndAFailWithANextTryTime_ClaimsAgainAtThatTimeWithNoSignal()
    {
        // Arrange
        RouterGives(new Error(Errors.RateLimited));
        var claims = Channel.CreateUnbounded<DateTime>();
        var store = Substitute.For<IInboxDispatchStore>();
        store.ClaimDueAsync(default, default, default, Ct).ReturnsForAnyArgs(
            call => Claimed(claims, call.ArgAt<DateTime>(1), AMessage()),
            call => Claimed(claims, call.ArgAt<DateTime>(1)));
        await using var provider = Build(more: b => b.UseInboxDispatchStore(_ => store).ConfigureInboxDispatch(static o => o.EnablePolling = false));
        await using var running = new RunningLoop(provider.GetRequiredService<IInboxProcessor>().RunAsync);
        await claims.Reader.ReadAsync(Ct);
        var wait = await _time.NextWaitAsync();
        var nextTryAtUtc = (DateTime)store.ReceivedCalls().Single(static c => c.GetMethodInfo().Name == nameof(IInboxDispatchStore.FailAsync)).GetArguments()[3]!;

        // Act
        _time.Advance(wait);

        // Assert
        Assert.Equal(nextTryAtUtc, await claims.Reader.ReadAsync(Ct).AsTask().WaitAsync(TestHost.HangGuard, TimeProvider.System, Ct));
    }

    private static ValueTask<ErrorOr<ClaimResult<ReceivedMessage>>> Claimed(Channel<DateTime> claims, DateTime nowUtc, params ReceivedMessage[] messages)
    {
        claims.Writer.TryWrite(nowUtc);
        return ValueTask.FromResult(ErrorOr<ClaimResult<ReceivedMessage>>.ForValue(new ClaimResult<ReceivedMessage>(messages, null)));
    }

    private ReceivedMessage<TestEvent> RoutedMessage() =>
        (ReceivedMessage<TestEvent>)_router.ReceivedCalls().Single(static c => c.GetMethodInfo().Name == nameof(IMessageRouter<TestEvent>.RouteAsync)).GetArguments()[0]!;

    private void RouterGives(Error error) =>
        _router.RouteAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult<ErrorOr<IReadOnlyList<MessageRoute>>>(error));

    private static ReceivedMessage<TestEvent> AMessage(TenantKey key = default, string text = "text", Guid? id = null, int maxTryCount = 8) =>
        TestMessages.AReceivedMessage(new TestEvent(text), key.Tenant, delivery: TestMessages.ADeliveryState(id) with { MaxTryCount = maxTryCount });

    private static async Task<ReceivedMessage<TestEvent>> AddAsync(IServiceProvider provider, ReceivedMessage<TestEvent> message)
    {
        await provider.GetRequiredService<InMemoryInboxStore>().For(TenantKey.For(message.Tenant)).AddAsync(message, Ct);
        return message;
    }

    private static ReceivedMessage Stored(IServiceProvider provider, ReceivedMessage message) =>
        provider.GetRequiredService<InMemoryInboxStore>().GetMessages().Single(m => m.Tenant == message.Tenant && m.Delivery.Id == message.Delivery.Id);

    private ServiceProvider Build(Action<MsgHatchBuilder>? more = null)
    {
        var services = TestHost.CreateServices(_time);
        var builder = services.AddMsgHatch(static o => o.EnableLogging = false)
            .AddTestChannel()
            .UseInMemoryStores()
            .AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, m => m
                .WithRouter((_, key) =>
                {
                    _routerKeys.Enqueue(key);
                    return _router;
                })
                .WithUpgrade(1, TestJsonContext.Default.TestEventV1, static v1 => new TestEvent($"{v1.Value} upgraded"))
                .AsChannel<TestEnvelope, TestEnvelopeComposer>(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope));
        more?.Invoke(builder);
        return services.Build();
    }
}
