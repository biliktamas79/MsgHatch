using System.Diagnostics;

namespace MsgHatch.Domain.Telemetry;

/// <summary>The activity source, and the attribute and tag names of section 4.21.</summary>
internal static class TelemetryNames
{
    public const string MessagingSystem = "messaging.system";
    public const string MessagingOperationType = "messaging.operation.type";
    public const string MessagingOperationName = "messaging.operation.name";
    public const string MessagingDestinationName = "messaging.destination.name";
    public const string MessagingMessageId = "messaging.message.id";
    public const string ErrorType = "error.type";
    public const string MessageType = "msghatch.message_type";
    public const string Channel = "msghatch.channel";
    public const string Test = "msghatch.test";
    public const string Tenant = "msghatch.tenant";
    public const string Routes = "msghatch.routes";
    public const string Stored = "msghatch.stored";
    public const string Failed = "msghatch.failed";
    public const string Skipped = "msghatch.skipped";
    public const string System = "msghatch";
    public const string ProcessOperation = "process";

    public static readonly ActivitySource Source = new(MsgHatchTelemetry.SourceName);
}
