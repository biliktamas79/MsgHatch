using Microsoft.Extensions.Logging;

namespace MsgHatch.Data.MongoDb;

/// <summary>The log events of the MongoDB package, with the event ids 2800 to 2899 (section 4.16).</summary>
internal static partial class Log
{
    [LoggerMessage(
        EventId = 2800, Level = LogLevel.Warning,
        Message = "The change stream of the MongoDB dispatch signal does not work, so the loops of the host wait for their due times and their poll until the signal opens it again")]
    public static partial void WatchBroken(this ILogger logger, Exception? exception);

    [LoggerMessage(
        EventId = 2801, Level = LogLevel.Information,
        Message = "The change stream of the MongoDB dispatch signal works again")]
    public static partial void WatchWorksAgain(this ILogger logger);
}
