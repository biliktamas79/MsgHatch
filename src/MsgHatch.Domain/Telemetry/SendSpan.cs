using System.Diagnostics;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Telemetry;

/// <summary>
/// The <c>send {Channel}</c> span of one try that calls a transport (D131, D132): a span of the kind Client in a new
/// trace, with a link to the trace context of the message. Its caller is an async method, so the reset of
/// <see cref="Activity.Current"/> stays inside that method.
/// </summary>
internal readonly struct SendSpan : IDisposable
{
    private readonly Activity? _activity;

    private SendSpan(Activity? activity)
    {
        _activity = activity;
    }

    public static SendSpan Start(OutgoingMessage message, int tryNumber)
    {
        var activity = TraceParents.StartInANewTrace($"send {message.Channel.Name}", ActivityKind.Client, message.TraceParent);
        if (activity is { IsAllDataRequested: true })
        {
            activity.SetTag(TelemetryNames.MessagingSystem, TelemetryNames.System);
            activity.SetTag(TelemetryNames.MessagingOperationType, TelemetryNames.SendOperation);
            activity.SetTag(TelemetryNames.MessagingOperationName, TelemetryNames.SendOperation);
            activity.SetTag(TelemetryNames.MessagingDestinationName, message.Channel.Name);
            activity.SetTag(TelemetryNames.MessagingMessageId, message.Delivery.Id.ToString());
            activity.SetTag(TelemetryNames.MessageType, message.Type.Name);
            activity.SetTag(TelemetryNames.Channel, message.Channel.Name);
            activity.SetTag(TelemetryNames.Test, message.Flags.HasFlag(MessageFlags.Test));
            activity.SetTag(TelemetryNames.TryNumber, tryNumber);
            if (message.Tenant is { } tenant)
            {
                activity.SetTag(TelemetryNames.Tenant, tenant.Value);
            }
        }

        return new SendSpan(activity);
    }

    public void Finish(PublishOutcome outcome, Error? error)
    {
        if (_activity is not { IsAllDataRequested: true })
        {
            return;
        }

        _activity.SetTag(TelemetryNames.Outcome, outcome.ToString());
        if (error is not null)
        {
            _activity.SetStatus(ActivityStatusCode.Error);
            _activity.SetTag(TelemetryNames.ErrorType, error.Type.Name);
        }
    }

    public void Dispose() => _activity?.Dispose();
}
