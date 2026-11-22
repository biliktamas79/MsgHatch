using Microsoft.Extensions.Logging;

namespace MsgHatch.Broker.AmazonSns;

/// <summary>The log events of the Amazon SNS broker client, with event ids of the broker area 2300 to 2399 (section 4.16).</summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 2310, Level = LogLevel.Critical,
        Message = "Amazon SNS refuses the messages of a topic: {Reason}. An administrator must act.")]
    public static partial void TopicCannotReceive(this ILogger logger, string reason);
}
