using System.Collections.Concurrent;
using System.Diagnostics;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>
/// The test classes whose tests listen to the spans of MsgHatch. An <see cref="ActivityListener"/> is global, and a
/// span changes the trace context of the messages, so these classes run one after another, and never in parallel.
/// </summary>
[CollectionDefinition(Name)]
public sealed class SequentialTelemetry
{
    public const string Name = "Telemetry";

    public static readonly ActivitySource TestSource = new("MsgHatch.Domain.Tests.Unit.Telemetry");

    public static ActivityListener ListenTo(string[] sourceNames, ConcurrentQueue<Activity> stopped)
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
}
