using Microsoft.Extensions.Logging;

namespace MsgHatch.Email.Resend;

/// <summary>The log events of the Resend client, with the event ids 3000 to 3099 (section 4.16).</summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 3000, Level = LogLevel.Critical,
        Message = "Resend refuses each email of the API key: {Reason}. An administrator must act.")]
    public static partial void ApiKeyCannotSend(ILogger logger, string reason);
}
