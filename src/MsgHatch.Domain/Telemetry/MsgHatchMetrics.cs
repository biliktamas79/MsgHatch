using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.Metrics;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Telemetry;

/// <summary>
/// The instruments of the pipeline and the dispatch loops (section 4.21), made one time on the meter of <see cref="IMeterFactory"/>.
/// The callbacks of the gauges read only the state of the loops in memory, and never a store.
/// The tags of a measurement are in a <see cref="TagList"/>, and the metrics have no tenant tag (D133).
/// </summary>
internal sealed class MsgHatchMetrics
{
    private static readonly object _true = true;
    private static readonly object _false = false;
    private static readonly double[] _durationBucketBoundaries = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10];
    private static readonly double[] _delayBucketBoundaries = [0.1, 0.5, 1, 5, 15, 60, 300, 900, 3600, 21600, 86400];

    private readonly Counter<long> _received;
    private readonly Counter<long> _stored;
    private readonly Counter<long> _routesFailed;
    private readonly Histogram<double> _processDuration;
    private readonly Counter<long> _publishTries;
    private readonly Counter<long> _messagesFailed;
    private readonly Histogram<double> _sendDuration;
    private readonly Histogram<double> _deliveryDelay;
    private readonly ConcurrentQueue<(string Queue, Func<int> Length)> _queues = new();
    private readonly ConcurrentQueue<(ChannelId Channel, Func<bool> IsDown)> _channels = new();

    public MsgHatchMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MsgHatchTelemetry.MeterName);
        _received = meter.CreateCounter<long>("msghatch.messages.received", "{message}", "The events of AcceptAsync and ProcessAsync.");
        _stored = meter.CreateCounter<long>("msghatch.messages.stored", "{message}", "The outgoing messages that the pipeline stores.");
        _routesFailed = meter.CreateCounter<long>("msghatch.routes.failed", "{route}", "The routes that the pipeline fails.");
        _processDuration = meter.CreateHistogram(
            "msghatch.process.duration", "s", "The time of one process of an event.", tags: null,
            new InstrumentAdvice<double> { HistogramBucketBoundaries = _durationBucketBoundaries });
        _publishTries = meter.CreateCounter<long>("msghatch.publish.tries", "{try}", "The recorded publish tries.");
        _messagesFailed = meter.CreateCounter<long>("msghatch.messages.failed", "{message}", "The outgoing messages that get the status Failed.");
        _sendDuration = meter.CreateHistogram(
            "msghatch.send.duration", "s", "The time of one call of a transport.", tags: null,
            new InstrumentAdvice<double> { HistogramBucketBoundaries = _durationBucketBoundaries });
        _deliveryDelay = meter.CreateHistogram(
            "msghatch.delivery.delay", "s", "The finish time of a successful try minus the creation time of its message.", tags: null,
            new InstrumentAdvice<double> { HistogramBucketBoundaries = _delayBucketBoundaries });
        meter.CreateObservableGauge("msghatch.queue.length", MeasureQueues, "{message}", "The messages in the work queue of each dispatch loop.");
        meter.CreateObservableGauge("msghatch.channel.down", MeasureChannels, "1", "1 when the last claim of the loop of the channel gave a down state.");
    }

    /// <summary>Adds the work queue of one dispatch loop to the gauge <c>msghatch.queue.length</c>, for the lifetime of the dispatcher.</summary>
    public void ObserveQueue(string queue, Func<int> length) => _queues.Enqueue((queue, length));

    /// <summary>Adds the outbox loop of one channel to the gauge <c>msghatch.channel.down</c>, for the lifetime of the dispatcher.</summary>
    public void ObserveChannel(ChannelId channel, Func<bool> isDown) => _channels.Enqueue((channel, isDown));

    public void RecordTry(OutgoingMessage message, PublishOutcome outcome) =>
        _publishTries.Add(1, new TagList
        {
            { TelemetryNames.MessageType, message.Type.Name }, { TelemetryNames.Channel, message.Channel.Name },
            { TelemetryNames.Outcome, outcome.ToString() }, { TelemetryNames.Test, IsTest(message.Flags) },
        });

    public void RecordFailed(OutgoingMessage message, string? errorName) =>
        _messagesFailed.Add(1, new TagList
        {
            { TelemetryNames.MessageType, message.Type.Name }, { TelemetryNames.Channel, message.Channel.Name },
            { TelemetryNames.ErrorType, errorName },
        });

    public void RecordSendDuration(ChannelId channel, PublishOutcome outcome, TimeSpan elapsed) =>
        _sendDuration.Record(elapsed.TotalSeconds, new TagList
        {
            { TelemetryNames.Channel, channel.Name }, { TelemetryNames.Outcome, outcome.ToString() },
        });

    public void RecordDeliveryDelay(OutgoingMessage message, TimeSpan delay) =>
        _deliveryDelay.Record(delay.TotalSeconds, new TagList
        {
            { TelemetryNames.MessageType, message.Type.Name }, { TelemetryNames.Channel, message.Channel.Name },
        });

    private IEnumerable<Measurement<int>> MeasureQueues() =>
        _queues.Select(static queue => new Measurement<int>(queue.Length(), new KeyValuePair<string, object?>(TelemetryNames.Queue, queue.Queue)));

    private IEnumerable<Measurement<int>> MeasureChannels() =>
        _channels.Select(static channel => new Measurement<int>(
            channel.IsDown() ? 1 : 0, new KeyValuePair<string, object?>(TelemetryNames.Channel, channel.Channel.Name)));

    public void RecordReceived(MessageType type, MessageFlags flags) =>
        _received.Add(1, new TagList { { TelemetryNames.MessageType, type.Name }, { TelemetryNames.Test, IsTest(flags) } });

    public void RecordStored(MessageType type, ChannelId channel, MessageFlags flags) =>
        _stored.Add(1, new TagList
        {
            { TelemetryNames.MessageType, type.Name }, { TelemetryNames.Channel, channel.Name }, { TelemetryNames.Test, IsTest(flags) },
        });

    public void RecordRouteFailed(MessageType type, ChannelId channel, Error error) =>
        _routesFailed.Add(1, new TagList
        {
            { TelemetryNames.MessageType, type.Name }, { TelemetryNames.Channel, channel.Name }, { TelemetryNames.ErrorType, error.Type.Name },
        });

    public void RecordProcessed(MessageType type, MessageFlags flags, Error? error, TimeSpan elapsed)
    {
        var tags = new TagList { { TelemetryNames.MessageType, type.Name }, { TelemetryNames.Test, IsTest(flags) } };
        if (error is not null)
        {
            tags.Add(TelemetryNames.ErrorType, error.Type.Name);
        }

        _processDuration.Record(elapsed.TotalSeconds, tags);
    }

    private static object IsTest(MessageFlags flags) => flags.HasFlag(MessageFlags.Test) ? _true : _false;
}
