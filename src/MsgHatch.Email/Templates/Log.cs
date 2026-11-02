using Microsoft.Extensions.Logging;

namespace MsgHatch.Email.Templates;

/// <summary>The log events of the template stores, with event ids of the email area 2000 to 2099 (section 4.16).</summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 2000, Level = LogLevel.Debug,
        Message = "Template store of tenant {Tenant} gave {PartCount} template parts for {MessageType} in {ElapsedMs} ms")]
    public static partial void FindSucceeded(ILogger logger, string messageType, int partCount, string? tenant, double elapsedMs);

    [LoggerMessage(
        EventId = 2001,
        Message = "Template store of tenant {Tenant} failed for {MessageType} after {ElapsedMs} ms: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void FindFailed(
        ILogger logger, LogLevel level, string messageType, string? tenant, double elapsedMs, int errorCode, string errorName,
        string? errorMessage);
}
