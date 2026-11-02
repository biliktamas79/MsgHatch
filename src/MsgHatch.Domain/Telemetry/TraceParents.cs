using System.Diagnostics;

namespace MsgHatch.Domain.Telemetry;

/// <summary>The W3C trace context of a message (D132): the id of a span, and the link of a later span to it.</summary>
// ponytail: traceparent only, add the tracestate if a vendor needs it
internal static class TraceParents
{
    public static string? Of(Activity? activity) => activity is { IdFormat: ActivityIdFormat.W3C } ? activity.Id : null;

    public static string? Current() => Of(Activity.Current);

    public static ActivityLink[]? LinksTo(string? traceParent) =>
        traceParent is not null && ActivityContext.TryParse(traceParent, traceState: null, out var context) ? [new ActivityLink(context)] : null;

    /// <summary>Starts a span in a new trace, with a link to the trace context of a message (D132).</summary>
    public static Activity? StartInANewTrace(string name, ActivityKind kind, string? linkedTraceParent)
    {
        if (!TelemetryNames.Source.HasListeners())
        {
            return null;
        }

        Activity.Current = null;
        return TelemetryNames.Source.StartActivity(name, kind, parentContext: default, tags: null, links: LinksTo(linkedTraceParent));
    }
}
