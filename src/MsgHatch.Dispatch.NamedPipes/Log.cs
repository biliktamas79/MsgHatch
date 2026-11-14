using Microsoft.Extensions.Logging;

namespace MsgHatch.Dispatch.NamedPipes;

/// <summary>The log events of the named pipe dispatch signal, with the event ids 3100 to 3199 (section 4.16).</summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 3100, Level = LogLevel.Warning,
        Message = "The named pipe dispatch signal cannot serve the pipe of topic {Topic}, so the loop of the topic waits for its due times and its poll")]
    public static partial void PipeNotServed(this ILogger logger, string topic, Exception exception);

    [LoggerMessage(
        EventId = 3101, Level = LogLevel.Warning,
        Message = "The named pipe dispatch signal cannot reach the pipe of topic {Topic} after {FailedConnects} connects in a row. A process of the same OS user with the same pipe name prefix must wait for the topic (D191), else the loop of the topic finds the messages only by its poll")]
    public static partial void PipeNotReached(this ILogger logger, string topic, int failedConnects);

    [LoggerMessage(
        EventId = 3102, Level = LogLevel.Information,
        Message = "The named pipe dispatch signal reaches the pipe of topic {Topic} again")]
    public static partial void PipeReachedAgain(this ILogger logger, string topic);
}
