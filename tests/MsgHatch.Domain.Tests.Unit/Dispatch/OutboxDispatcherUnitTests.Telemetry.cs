using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Telemetry;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;
using static MsgHatch.Domain.Tests.Unit.TestTypes.OutboxDispatchDoubles;

namespace MsgHatch.Domain.Tests.Unit.Dispatch;

/// <summary>The dispatch telemetry (D131 to D133): the <c>send</c> span and the instruments of the outbox loops.</summary>
public sealed partial class OutboxDispatcherUnitTests
{
    private const string _messageTraceParent = "00-4bf92f3577b34da6a3ce929d0e0e4736-00f067aa0ba902b7-01";
    private const string _address = "client1@example.com";

    [Fact]
    public async Task DispatchPendingAsync_ListenerOnTheSource_GivesOneSendSpanOfKindClientInANewTraceWithALinkToTheMessage()
    {
        // Arrange
        var message = AMessage(tenant: new TenantId("t1")) with { TraceParent = _messageTraceParent };
        _doubles.ClaimsGive(TestChannel.Id, Claim(message));
        var transport = TestTransports.For(TestChannel.Id);
        await using var provider = _doubles.Build(transports: transport);
        var spans = new ConcurrentQueue<Activity>();
        using var listener = SequentialTelemetry.ListenTo([MsgHatchTelemetry.SourceName, SequentialTelemetry.TestSource.Name], spans);
        using var parent = SequentialTelemetry.TestSource.StartActivity("test")!;

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var span = Assert.Single(spans, s => Equals(s.GetTagItem("messaging.message.id"), message.Delivery.Id.ToString()));
        var link = Assert.Single(span.Links);
        Assert.Equal(
            ("send Test", ActivityKind.Client, default(ActivitySpanId), false, "4bf92f3577b34da6a3ce929d0e0e4736", "00f067aa0ba902b7"),
            (span.DisplayName, span.Kind, span.ParentSpanId, span.TraceId == parent.TraceId, link.Context.TraceId.ToString(), link.Context.SpanId.ToString()));
        Assert.Equal(
            [
                "messaging.system=msghatch", "messaging.operation.type=send", "messaging.operation.name=send",
                "messaging.destination.name=Test", $"messaging.message.id={message.Delivery.Id}", "msghatch.message_type=TestEvent",
                "msghatch.channel=Test", "msghatch.test=False", "msghatch.try_number=1", "msghatch.tenant=t1", "msghatch.outcome=Succeeded",
            ],
            span.TagObjects.Select(static tag => $"{tag.Key}={tag.Value}"));
        _ = transport.Received(1).SendAsync(message, null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).RecordTryAsync(
            message, Arg.Is<PublishTry>(t => t.Outcome == PublishOutcome.Succeeded), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_MessageWithNoTraceParent_GivesASendSpanWithNoLink()
    {
        // Arrange
        var message = AMessage();
        _doubles.ClaimsGive(TestChannel.Id, Claim(message));
        var transport = TestTransports.For(TestChannel.Id);
        await using var provider = _doubles.Build(transports: transport);
        var spans = new ConcurrentQueue<Activity>();
        using var listener = SequentialTelemetry.ListenTo([MsgHatchTelemetry.SourceName], spans);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Empty(Assert.Single(spans, s => Equals(s.GetTagItem("messaging.message.id"), message.Delivery.Id.ToString())).Links);
        _ = transport.Received(1).SendAsync(message, null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).RecordTryAsync(
            message, Arg.Is<PublishTry>(t => t.Outcome == PublishOutcome.Succeeded), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_TransientError_GivesTheSpanStatusErrorAndTheErrorTypeWithNoErrorMessage()
    {
        // Arrange
        var message = AMessage();
        _doubles.ClaimsGive(TestChannel.Id, Claim(message));
        var transport = TestTransports.For(TestChannel.Id, new Error(Errors.RateLimited, $"Too many for {_address}"));
        await using var provider = _doubles.Build(transports: transport);
        var spans = new ConcurrentQueue<Activity>();
        using var listener = SequentialTelemetry.ListenTo([MsgHatchTelemetry.SourceName], spans);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var span = Assert.Single(spans, s => Equals(s.GetTagItem("messaging.message.id"), message.Delivery.Id.ToString()));
        Assert.Equal(
            (ActivityStatusCode.Error, (string?)null, (object?)"RateLimited", (object?)"TransientError"),
            (span.Status, span.StatusDescription, span.GetTagItem("error.type"), span.GetTagItem("msghatch.outcome")));
        _ = transport.Received(1).SendAsync(message, null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).RecordTryAsync(
            message, Arg.Is<PublishTry>(t => t.Outcome == PublishOutcome.TransientError), Arg.Is<DateTime?>(d => d != null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_MessageWithAChannelAddressAndATenant_NoSpanAttributeOrMetricTagHoldsTheAddressOrTheTenant()
    {
        // Arrange
        var message = TestMessages.AnOutgoingMessage(new TestEnvelope(_address), tenant: new TenantId("t1"));
        _doubles.ClaimsGive(TestChannel.Id, Claim(message));
        var transport = TestTransports.For(TestChannel.Id);
        await using var provider = _doubles.Build(transports: transport);
        using var tries = new MetricCollector<long>(provider.GetRequiredService<IMeterFactory>(), MsgHatchTelemetry.MeterName, "msghatch.publish.tries");
        var spans = new ConcurrentQueue<Activity>();
        using var listener = SequentialTelemetry.ListenTo([MsgHatchTelemetry.SourceName], spans);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var span = Assert.Single(spans, s => Equals(s.GetTagItem("messaging.message.id"), message.Delivery.Id.ToString()));
        Assert.DoesNotContain(span.TagObjects, static tag => tag.Value?.ToString()?.Contains(_address, StringComparison.Ordinal) == true);
        var measurement = Assert.Single(tries.GetMeasurementSnapshot());
        Assert.False(measurement.Tags.ContainsKey("msghatch.tenant"));
        Assert.DoesNotContain(measurement.Tags.Values, static value => value?.ToString()?.Contains(_address, StringComparison.Ordinal) == true);
        _ = transport.Received(1).SendAsync(message, null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).RecordTryAsync(
            message, Arg.Is<PublishTry>(t => t.Outcome == PublishOutcome.Succeeded), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_TransientErrorAndASuccess_CountsEachRecordedTryWithItsOutcome()
    {
        // Arrange
        var (first, second) = (AMessage(), AMessage());
        _doubles.ClaimsGive(TestChannel.Id, Claim(first, second));
        var transport = TestTransports.For(TestChannel.Id);
        transport.SendAsync(default!, default, Ct).ReturnsForAnyArgs(
            ValueTask.FromResult<ErrorOr<PublishReceipt>>(new Error(Errors.RateLimited)),
            ValueTask.FromResult<ErrorOr<PublishReceipt>>(new PublishReceipt("provider-1")));
        await using var provider = _doubles.Build(static o => o.MaxDegreeOfParallelism = 1, transports: transport);
        using var tries = new MetricCollector<long>(provider.GetRequiredService<IMeterFactory>(), MsgHatchTelemetry.MeterName, "msghatch.publish.tries");

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Equal(
            [(1L, "TestEvent", "Test", "TransientError", false), (1L, "TestEvent", "Test", "Succeeded", false)],
            tries.GetMeasurementSnapshot().Select(static m => (
                m.Value, (string?)m.Tags["msghatch.message_type"], (string?)m.Tags["msghatch.channel"], (string?)m.Tags["msghatch.outcome"],
                (bool?)m.Tags["msghatch.test"])));
        _ = transport.Received(1).SendAsync(first, null, Arg.Any<CancellationToken>());
        _ = transport.Received(1).SendAsync(second, null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).RecordTryAsync(
            first, Arg.Is<PublishTry>(t => t.Outcome == PublishOutcome.TransientError), Arg.Is<DateTime?>(d => d != null), Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).RecordTryAsync(
            second, Arg.Is<PublishTry>(t => t.Outcome == PublishOutcome.Succeeded), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_PermanentError_AddsOneFailedMessageWithItsErrorType()
    {
        // Arrange
        var message = AMessage();
        _doubles.ClaimsGive(TestChannel.Id, Claim(message));
        var transport = TestTransports.For(TestChannel.Id, new Error(Errors.Rejected));
        await using var provider = _doubles.Build(transports: transport);
        using var failed = new MetricCollector<long>(provider.GetRequiredService<IMeterFactory>(), MsgHatchTelemetry.MeterName, "msghatch.messages.failed");

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var measurement = Assert.Single(failed.GetMeasurementSnapshot());
        Assert.Equal((1L, (object?)"Rejected", (object?)"Test"), (measurement.Value, measurement.Tags["error.type"], measurement.Tags["msghatch.channel"]));
        _ = transport.Received(1).SendAsync(message, null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).RecordTryAsync(
            message, Arg.Is<PublishTry>(t => t.Outcome == PublishOutcome.PermanentError), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_SuccessNinetySecondsAfterTheCreation_RecordsADeliveryDelayOfNinetySeconds()
    {
        // Arrange
        var message = AMessage();
        _doubles.ClaimsGive(TestChannel.Id, Claim(message));
        var transport = TestTransports.For(TestChannel.Id);
        await using var provider = _doubles.Build(transports: transport);
        using var delay = new MetricCollector<double>(provider.GetRequiredService<IMeterFactory>(), MsgHatchTelemetry.MeterName, "msghatch.delivery.delay");
        _doubles.Time.Advance(TimeSpan.FromSeconds(90));

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Equal(90, Assert.Single(delay.GetMeasurementSnapshot()).Value);
        _ = transport.Received(1).SendAsync(message, null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).RecordTryAsync(
            message, Arg.Is<PublishTry>(t => t.Outcome == PublishOutcome.Succeeded && t.FinishedAtUtc == _doubles.Now), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_SendThatTakesTwoSeconds_RecordsASendDurationOfTwoSecondsWithTheChannelAndTheOutcome()
    {
        // Arrange
        var message = AMessage();
        _doubles.ClaimsGive(TestChannel.Id, Claim(message));
        var transport = TestTransports.For(TestChannel.Id);
        transport.SendAsync(default!, default, Ct).ReturnsForAnyArgs(_ =>
        {
            _doubles.Time.Advance(TimeSpan.FromSeconds(2));
            return ValueTask.FromResult<ErrorOr<PublishReceipt>>(new PublishReceipt("provider-1"));
        });
        await using var provider = _doubles.Build(transports: transport);
        using var duration = new MetricCollector<double>(provider.GetRequiredService<IMeterFactory>(), MsgHatchTelemetry.MeterName, "msghatch.send.duration");

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var measurement = Assert.Single(duration.GetMeasurementSnapshot());
        Assert.Equal((2d, (object?)"Test", (object?)"Succeeded"), (measurement.Value, measurement.Tags["msghatch.channel"], measurement.Tags["msghatch.outcome"]));
        _ = transport.Received(1).SendAsync(message, null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).RecordTryAsync(
            message, Arg.Is<PublishTry>(t => t.Outcome == PublishOutcome.Succeeded), null, Arg.Any<CancellationToken>());
    }

    public static TheoryData<PublishOutcome> SendOutcomeRows => new()
    {
        PublishOutcome.Succeeded, PublishOutcome.TransientError, PublishOutcome.PermanentError, PublishOutcome.Expired, PublishOutcome.Suppressed,
    };

    [Theory]
    [MemberData(nameof(SendOutcomeRows))]
    public async Task DispatchPendingAsync_EachOutcomeOfASend_TagsTheTryAndTheSendDurationWithTheNameOfTheOutcome(PublishOutcome outcome)
    {
        // Arrange
        var message = outcome switch
        {
            PublishOutcome.Expired => AMessage(expiresAtUtc: _doubles.Now.AddMinutes(-1)),
            PublishOutcome.Suppressed => AMessage(flags: MessageFlags.Test), // the default test delivery is Suppress
            _ => AMessage(),
        };
        _doubles.ClaimsGive(TestChannel.Id, Claim(message));
        var transport = outcome switch
        {
            PublishOutcome.TransientError => TestTransports.For(TestChannel.Id, new Error(Errors.RateLimited)),
            PublishOutcome.PermanentError => TestTransports.For(TestChannel.Id, new Error(Errors.Rejected)),
            _ => TestTransports.For(TestChannel.Id),
        };
        await using var provider = _doubles.Build(transports: transport);
        var meters = provider.GetRequiredService<IMeterFactory>();
        using var tries = new MetricCollector<long>(meters, MsgHatchTelemetry.MeterName, "msghatch.publish.tries");
        using var duration = new MetricCollector<double>(meters, MsgHatchTelemetry.MeterName, "msghatch.send.duration");

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var sent = outcome is PublishOutcome.Expired or PublishOutcome.Suppressed ? "" : outcome.ToString(); // no transport call, so no send duration
        Assert.Equal(
            (outcome.ToString(), sent),
            (string.Join(",", tries.GetMeasurementSnapshot().Select(static m => (string?)m.Tags["msghatch.outcome"])),
                string.Join(",", duration.GetMeasurementSnapshot().Select(static m => (string?)m.Tags["msghatch.outcome"]))));
        _ = _doubles.Store.Received(1).RecordTryAsync(
            message, Arg.Is<PublishTry>(t => t.Outcome == outcome), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_AllWorkersBusy_QueueLengthGivesTheMessagesInTheWorkQueue()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage(), AMessage(), AMessage()));
        var sendStarted = new TaskCompletionSource();
        var transport = TestTransports.NeverCompleting(TestChannel.Id, sendStarted);
        await using var provider = _doubles.Build(
            static o => (o.EnablePolling, o.MaxDegreeOfParallelism, o.QueueCapacity) = (false, 1, 3), transports: transport);
        using var length = new MetricCollector<int>(provider.GetRequiredService<IMeterFactory>(), MsgHatchTelemetry.MeterName, "msghatch.queue.length");
        await using var running = new RunningLoop(provider.GetRequiredService<IOutboxDispatcher>().RunAsync);
        await sendStarted.Task.WaitAsync(TestHost.HangGuard, TimeProvider.System, Ct);
        await _doubles.NextClaimAsync();
        await _doubles.NextClaimAsync();

        // Act
        length.RecordObservableInstruments();

        // Assert
        var measurement = Assert.Single(length.GetMeasurementSnapshot());
        Assert.Equal((2, (object?)"Test"), (measurement.Value, measurement.Tags["msghatch.queue"]));
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.Id, 3, TestHost.Start.UtcDateTime, TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.Id, 1, TestHost.Start.UtcDateTime, TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
        _ = transport.Received(1).SendAsync(Arg.Any<OutgoingMessage>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_ClaimWithADownStateAndThenOneWithNone_ChannelDownGivesOneAndThenZero()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, ClaimDown(ADownState(_doubles.Now.AddSeconds(30))), Claim());
        await using var provider = _doubles.Build(transports: TestTransports.For(TestChannel.Id));
        var dispatcher = provider.GetRequiredService<IOutboxDispatcher>();
        using var down = new MetricCollector<int>(provider.GetRequiredService<IMeterFactory>(), MsgHatchTelemetry.MeterName, "msghatch.channel.down");
        await dispatcher.DispatchPendingAsync(10, Ct);
        down.RecordObservableInstruments();

        // Act
        await dispatcher.DispatchPendingAsync(10, Ct);
        down.RecordObservableInstruments();

        // Assert
        Assert.Equal([(1, (object?)"Test"), (0, "Test")], down.GetMeasurementSnapshot().Select(static m => (m.Value, m.Tags["msghatch.channel"])));
        _ = _doubles.Store.Received(2).ClaimDueAsync(TestChannel.Id, 10, _doubles.Now, TimeSpan.FromSeconds(60), Ct);
    }
}
