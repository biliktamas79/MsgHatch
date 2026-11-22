using Microsoft.Extensions.Logging;

namespace MsgHatch.Dispatch.RabbitMq;

/// <summary>The log events of the RabbitMQ dispatch signal, with the event ids 3200 to 3299 (section 4.16).</summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 3200, Level = LogLevel.Warning,
        Message = "The RabbitMQ dispatch signal has no connection to the broker, so the loops wait for their due times and their poll until it connects again after {Pause}")]
    public static partial void NoConnection(this ILogger logger, TimeSpan pause, Exception exception);

    [LoggerMessage(
        EventId = 3201, Level = LogLevel.Information,
        Message = "The RabbitMQ dispatch signal is connected to the broker again")]
    public static partial void ConnectedAgain(this ILogger logger);
}
