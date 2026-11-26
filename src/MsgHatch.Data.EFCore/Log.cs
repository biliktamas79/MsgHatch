using Microsoft.Extensions.Logging;

namespace MsgHatch.Data.EFCore;

/// <summary>The log events of the EF Core stores, with the event ids 2700 to 2799.</summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 2700, Level = LogLevel.Warning,
        Message = "Dispatch signal notify of the topic {Topic} after the commit of the host failed: {ErrorCode} {ErrorName} {ErrorMessage}")]
    public static partial void HostCommitNotifyFailed(this ILogger logger, string topic, int errorCode, string errorName, string? errorMessage);
}
