using System.Diagnostics;
using System.Diagnostics.Metrics;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Telemetry;

/// <summary>
/// The instruments of the pipeline (section 4.21), made one time on the meter of <see cref="IMeterFactory"/>.
/// The tags of a measurement are in a <see cref="TagList"/>, and the metrics have no tenant tag (D133).
/// </summary>
internal sealed class MsgHatchMetrics
{
    private static readonly object _true = true;
    private static readonly object _false = false;
    private static readonly double[] _durationBucketBoundaries = [0.005, 0.01, 0.025, 0.05, 0.1, 0.25, 0.5, 1, 2.5, 5, 10];

    private readonly Counter<long> _received;
    private readonly Counter<long> _stored;
    private readonly Counter<long> _routesFailed;
    private readonly Histogram<double> _processDuration;

    public MsgHatchMetrics(IMeterFactory meterFactory)
    {
        var meter = meterFactory.Create(MsgHatchTelemetry.MeterName);
        _received = meter.CreateCounter<long>("msghatch.messages.received", "{message}", "The events of ProcessAsync.");
        _stored = meter.CreateCounter<long>("msghatch.messages.stored", "{message}", "The outgoing messages that the pipeline stores.");
        _routesFailed = meter.CreateCounter<long>("msghatch.routes.failed", "{route}", "The routes that the pipeline fails.");
        _processDuration = meter.CreateHistogram(
            "msghatch.process.duration", "s", "The time of one process of an event.", tags: null,
            new InstrumentAdvice<double> { HistogramBucketBoundaries = _durationBucketBoundaries });
    }

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
