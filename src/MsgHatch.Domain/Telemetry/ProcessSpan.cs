using System.Diagnostics;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;

namespace MsgHatch.Domain.Telemetry;

/// <summary>
/// The <c>process {MessageType}</c> span of <c>ProcessAsync</c>, a child of <see cref="Activity.Current"/> (D131, D132).
/// With no listener, no span starts, and the outgoing messages get the trace context of <see cref="Activity.Current"/>.
/// </summary>
// ponytail: traceparent only, add the tracestate if a vendor needs it
internal readonly struct ProcessSpan : IDisposable
{
    private readonly Activity? _activity;

    private ProcessSpan(Activity? activity, string? callerTraceParent)
    {
        _activity = activity;
        CallerTraceParent = callerTraceParent;
        TraceParent = TraceParentOf(activity) ?? callerTraceParent;
    }

    public string? CallerTraceParent { get; }

    public string? TraceParent { get; }

    public static ProcessSpan Start(MessageType type, MessageContext context)
    {
        var callerTraceParent = TraceParentOf(Activity.Current);
        var activity = TelemetryNames.Source.HasListeners()
            ? TelemetryNames.Source.StartActivity($"process {type.Name}", ActivityKind.Consumer)
            : null;
        if (activity is { IsAllDataRequested: true })
        {
            SetStartAttributes(activity, type, context);
        }

        return new ProcessSpan(activity, callerTraceParent);
    }

    public void Finish(ErrorOr<NotificationResult> result)
    {
        if (_activity is not { IsAllDataRequested: true })
        {
            return;
        }

        if (result.IsError)
        {
            _activity.SetStatus(ActivityStatusCode.Error);
            _activity.SetTag(TelemetryNames.ErrorType, result.Error.Type.Name);
            return;
        }

        _activity.SetTag(TelemetryNames.Routes, result.Value.Stored + result.Value.Failed);
        _activity.SetTag(TelemetryNames.Stored, result.Value.Stored);
        _activity.SetTag(TelemetryNames.Failed, result.Value.Failed);
        _activity.SetTag(TelemetryNames.Skipped, 0);
    }

    public void Dispose() => _activity?.Dispose();

    private static void SetStartAttributes(Activity activity, MessageType type, MessageContext context)
    {
        activity.SetTag(TelemetryNames.MessagingSystem, TelemetryNames.System);
        activity.SetTag(TelemetryNames.MessagingOperationType, TelemetryNames.ProcessOperation);
        activity.SetTag(TelemetryNames.MessagingOperationName, TelemetryNames.ProcessOperation);
        activity.SetTag(TelemetryNames.MessagingDestinationName, type.Name);
        activity.SetTag(TelemetryNames.MessagingMessageId, context.MessageId.ToString());
        activity.SetTag(TelemetryNames.MessageType, type.Name);
        activity.SetTag(TelemetryNames.Test, context.Flags.HasFlag(MessageFlags.Test));
        if (context.Tenant is { } tenant)
        {
            activity.SetTag(TelemetryNames.Tenant, tenant.Value);
        }
    }

    private static string? TraceParentOf(Activity? activity) =>
        activity is { IdFormat: ActivityIdFormat.W3C } ? activity.Id : null;
}
