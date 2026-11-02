using Microsoft.Extensions.Logging;

namespace MsgHatch.Email.AmazonSES;

/// <summary>The log events of the Amazon SES client, with the event ids 2100 to 2199 (section 4.16).</summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 2100, Level = LogLevel.Critical,
        Message = "Amazon SES refuses each email of the account: {Reason}. An administrator must act.")]
    public static partial void AccountCannotSend(ILogger logger, string reason);
}
