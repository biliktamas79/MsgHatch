using Microsoft.Extensions.Logging;

namespace MsgHatch.Dispatch.NamedPipes;

/// <summary>The log events of the named pipe dispatch signal, with the event ids 3100 to 3199 (section 4.16).</summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 3100, Level = LogLevel.Warning,
        Message = "The named pipe dispatch signal cannot serve the pipe of topic {Topic}, so the loop of the topic waits for its due times and its poll")]
    public static partial void PipeNotServed(this ILogger logger, string topic, Exception exception);
}
