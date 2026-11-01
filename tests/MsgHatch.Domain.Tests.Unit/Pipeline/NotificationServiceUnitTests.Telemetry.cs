using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.Metrics.Testing;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Telemetry;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using static MsgHatch.Domain.Tests.Unit.TestTypes.TestRoutes;

namespace MsgHatch.Domain.Tests.Unit.Pipeline;

/// <summary>
/// The pipeline telemetry (D131 to D133). These tests are in this class, so that they run one after another: an
/// <see cref="ActivityListener"/> is global, and xUnit runs the test classes in parallel.
/// </summary>
public sealed partial class NotificationServiceUnitTests
{
    private const string _address = "client1@example.com";
    private static readonly ActivitySource _testSource = new("MsgHatch.Domain.Tests.Unit");

    [Fact]
    public async Task ProcessAsync_ListenerOnTheSource_GivesOneProcessSpanOfKindConsumerAsAChildOfTheCurrentActivity()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1"), ARoute("u2")];
        var context = AContext();
        await using var provider = _doubles.Build(_time);
        var spans = new ConcurrentQueue<Activity>();
        using var listener = ListenTo([MsgHatchTelemetry.SourceName, _testSource.Name], spans);
        using var parent = _testSource.StartActivity("test")!;

        // Act
        await ProcessAsync(provider, context);

        // Assert
        var span = Assert.Single(spans, s => s.Source.Name == MsgHatchTelemetry.SourceName && s.ParentSpanId == parent.SpanId);
        Assert.Equal(("process TestEvent", ActivityKind.Consumer), (span.DisplayName, span.Kind));
        Assert.Equal(
            [
                "messaging.system=msghatch", "messaging.operation.type=process", "messaging.operation.name=process",
                "messaging.destination.name=TestEvent", $"messaging.message.id={context.MessageId}",
                "msghatch.message_type=TestEvent", "msghatch.test=False", "msghatch.routes=2", "msghatch.stored=2",
                "msghatch.failed=0", "msghatch.skipped=0",
            ],
            span.TagObjects.Select(static tag => $"{tag.Key}={tag.Value}"));
    }

    [Fact]
    public async Task ProcessAsync_MessageWithATenant_GivesTheTenantAttribute()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1")];
        await using var provider = _doubles.Build(_time);
        var spans = new ConcurrentQueue<Activity>();
        using var listener = ListenTo([MsgHatchTelemetry.SourceName, _testSource.Name], spans);
        using var parent = _testSource.StartActivity("test")!;

        // Act
        await ProcessAsync(provider, AContext(_tenant));

        // Assert
        var span = Assert.Single(spans, s => s.Source.Name == MsgHatchTelemetry.SourceName && s.ParentSpanId == parent.SpanId);
        Assert.Equal("t1", span.GetTagItem("msghatch.tenant"));
    }

    [Fact]
    public async Task ProcessAsync_ListenerOnTheSource_EachOutgoingMessageHasTheIdOfTheProcessSpanAsTraceParent()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1"), ARoute("u2")];
        await using var provider = _doubles.Build(_time);
        var spans = new ConcurrentQueue<Activity>();
        using var listener = ListenTo([MsgHatchTelemetry.SourceName, _testSource.Name], spans);
        using var parent = _testSource.StartActivity("test")!;

        // Act
        await ProcessAsync(provider, AContext());

        // Assert
        var span = Assert.Single(spans, s => s.Source.Name == MsgHatchTelemetry.SourceName && s.ParentSpanId == parent.SpanId);
        Assert.Equal([span.Id, span.Id], _doubles.Stored.Select(static m => m.TraceParent));
    }

    [Fact]
    public async Task ProcessAsync_NoListenerOnTheSource_EachOutgoingMessageHasTheIdOfTheCurrentActivity()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1"), ARoute("u2")];
        await using var provider = _doubles.Build(_time);
        using var listener = ListenTo([_testSource.Name], new ConcurrentQueue<Activity>());
        using var parent = _testSource.StartActivity("test")!;

        // Act
        await ProcessAsync(provider, AContext());

        // Assert
        Assert.Equal([parent.Id, parent.Id], _doubles.Stored.Select(static m => m.TraceParent));
    }

    [Fact]
    public async Task ProcessAsync_FailedEvent_GivesTheSpanStatusErrorAndTheErrorTypeAndNoErrorMessage()
    {
        // Arrange
        _doubles.Router(TenantKey.None).RouteAsync(default!, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult<ErrorOr<IReadOnlyList<MessageRoute>>>(new Error(Errors.Unavailable, "provider down")));
        await using var provider = _doubles.Build(_time);
        var spans = new ConcurrentQueue<Activity>();
        using var listener = ListenTo([MsgHatchTelemetry.SourceName, _testSource.Name], spans);
        using var parent = _testSource.StartActivity("test")!;

        // Act
        await ProcessAsync(provider, AContext());

        // Assert
        var span = Assert.Single(spans, s => s.Source.Name == MsgHatchTelemetry.SourceName && s.ParentSpanId == parent.SpanId);
        Assert.Equal(
            (ActivityStatusCode.Error, (string?)null, (object?)"Unavailable"),
            (span.Status, span.StatusDescription, span.GetTagItem("error.type")));
    }

    [Fact]
    public async Task ProcessAsync_RouterThatThrows_GivesTheErrorTypeExceptionToTheSpanAndTheDuration()
    {
        // Arrange
        _doubles.Router(TenantKey.None).RouteAsync(default!, Ct).ThrowsForAnyArgs(new InvalidOperationException("defect"));
        await using var provider = _doubles.Build(_time);
        using var duration = new MetricCollector<double>(
            provider.GetRequiredService<IMeterFactory>(), MsgHatchTelemetry.MeterName, "msghatch.process.duration");
        var spans = new ConcurrentQueue<Activity>();
        using var listener = ListenTo([MsgHatchTelemetry.SourceName, _testSource.Name], spans);
        using var parent = _testSource.StartActivity("test")!;

        // Act
        await ProcessAsync(provider, AContext());

        // Assert
        var span = Assert.Single(spans, s => s.Source.Name == MsgHatchTelemetry.SourceName && s.ParentSpanId == parent.SpanId);
        var measurement = Assert.Single(duration.GetMeasurementSnapshot());
        Assert.Equal((ActivityStatusCode.Error, (object?)"Exception"), (span.Status, span.GetTagItem("error.type")));
        Assert.Equal(["error.type=Exception", "msghatch.message_type=TestEvent", "msghatch.test=False"], TagsOf(measurement.Tags));
    }

    [Fact]
    public async Task ProcessAsync_Event_RecordsOneReceivedMessageWithItsTags()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1")];
        await using var provider = _doubles.Build(_time);
        using var received = new MetricCollector<long>(
            provider.GetRequiredService<IMeterFactory>(), MsgHatchTelemetry.MeterName, "msghatch.messages.received");

        // Act
        await ProcessAsync(provider, AContext(_tenant, MessageFlags.Test));

        // Assert
        var measurement = Assert.Single(received.GetMeasurementSnapshot());
        Assert.Equal(1, measurement.Value);
        Assert.Equal(["msghatch.message_type=TestEvent", "msghatch.test=True"], TagsOf(measurement.Tags));
    }

    [Fact]
    public async Task ProcessAsync_TwoStoredMessages_RecordsEachStoredMessageWithItsChannel()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1"), ARoute("u2", channel: TestChannel.SecondId, settings: new OtherTestChannelSettings())];
        await using var provider = _doubles.Build(_time);
        using var stored = new MetricCollector<long>(
            provider.GetRequiredService<IMeterFactory>(), MsgHatchTelemetry.MeterName, "msghatch.messages.stored");

        // Act
        await ProcessAsync(provider, AContext());

        // Assert
        Assert.Equal(
            [
                "1: msghatch.channel=Test, msghatch.message_type=TestEvent, msghatch.test=False",
                "1: msghatch.channel=Second, msghatch.message_type=TestEvent, msghatch.test=False",
            ],
            stored.GetMeasurementSnapshot().Select(static m => $"{m.Value}: {string.Join(", ", TagsOf(m.Tags))}"));
    }

    [Fact]
    public async Task ProcessAsync_FailedRoute_RecordsTheFailedRouteWithItsErrorType()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1"), ARoute("u9", settings: new OtherTestChannelSettings())]; // settings of another type
        await using var provider = _doubles.Build(_time);
        using var failed = new MetricCollector<long>(
            provider.GetRequiredService<IMeterFactory>(), MsgHatchTelemetry.MeterName, "msghatch.routes.failed");

        // Act
        await ProcessAsync(provider, AContext());

        // Assert
        var measurement = Assert.Single(failed.GetMeasurementSnapshot());
        Assert.Equal(
            "1: error.type=ValidationFailed, msghatch.channel=Test, msghatch.message_type=TestEvent",
            $"{measurement.Value}: {string.Join(", ", TagsOf(measurement.Tags))}");
    }

    [Fact]
    public async Task ProcessAsync_Event_RecordsTheElapsedTimeOfTheFakeClock()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1")];
        _doubles.Router(TenantKey.None).RouteAsync(default!, Ct).ReturnsForAnyArgs(_ =>
        {
            _time.Advance(TimeSpan.FromSeconds(2));
            return ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageRoute>>.ForValue(_doubles.Routes));
        });
        await using var provider = _doubles.Build(_time);
        using var duration = new MetricCollector<double>(
            provider.GetRequiredService<IMeterFactory>(), MsgHatchTelemetry.MeterName, "msghatch.process.duration");

        // Act
        await ProcessAsync(provider, AContext());

        // Assert
        var measurement = Assert.Single(duration.GetMeasurementSnapshot());
        Assert.Equal(2.0, measurement.Value);
        Assert.Equal(["msghatch.message_type=TestEvent", "msghatch.test=False"], TagsOf(measurement.Tags));
    }

    [Fact]
    public async Task ProcessAsync_TestRouteWithATenantAndAnAddress_NoMetricTagHasTheTenantAndNoTagHasTheAddress()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1", address: _address), ARoute("u2", settings: new OtherTestChannelSettings(), address: _address)];
        await using var provider = _doubles.Build(_time);
        var meters = provider.GetRequiredService<IMeterFactory>();
        using var received = new MetricCollector<long>(meters, MsgHatchTelemetry.MeterName, "msghatch.messages.received");
        using var stored = new MetricCollector<long>(meters, MsgHatchTelemetry.MeterName, "msghatch.messages.stored");
        using var failed = new MetricCollector<long>(meters, MsgHatchTelemetry.MeterName, "msghatch.routes.failed");
        using var duration = new MetricCollector<double>(meters, MsgHatchTelemetry.MeterName, "msghatch.process.duration");
        var spans = new ConcurrentQueue<Activity>();
        using var listener = ListenTo([MsgHatchTelemetry.SourceName, _testSource.Name], spans);
        using var parent = _testSource.StartActivity("test")!;

        // Act
        await ProcessAsync(provider, AContext(_tenant, MessageFlags.Test));

        // Assert
        string[] metricTags =
        [
            .. received.GetMeasurementSnapshot().SelectMany(static m => TagsOf(m.Tags)),
            .. stored.GetMeasurementSnapshot().SelectMany(static m => TagsOf(m.Tags)),
            .. failed.GetMeasurementSnapshot().SelectMany(static m => TagsOf(m.Tags)),
            .. duration.GetMeasurementSnapshot().SelectMany(static m => TagsOf(m.Tags)),
        ];
        var spanTags = spans.Where(s => s.ParentSpanId == parent.SpanId).SelectMany(static s => s.TagObjects).Select(static t => $"{t.Key}={t.Value}");
        Assert.NotEmpty(metricTags);
        Assert.DoesNotContain(metricTags, static tag => tag.StartsWith("msghatch.tenant=", StringComparison.Ordinal));
        Assert.DoesNotContain(metricTags.Concat(spanTags), static tag => tag.Contains(_address, StringComparison.Ordinal));
    }

    private static ActivityListener ListenTo(string[] sourceNames, ConcurrentQueue<Activity> stopped)
    {
        var listener = new ActivityListener
        {
            ShouldListenTo = source => sourceNames.Contains(source.Name),
            Sample = static (ref ActivityCreationOptions<ActivityContext> _) => ActivitySamplingResult.AllDataAndRecorded,
            ActivityStopped = stopped.Enqueue,
        };
        ActivitySource.AddActivityListener(listener);
        return listener;
    }

    private static string[] TagsOf(IReadOnlyDictionary<string, object?> tags) =>
        [.. tags.OrderBy(static tag => tag.Key, StringComparer.Ordinal).Select(static tag => $"{tag.Key}={tag.Value}")];
}
