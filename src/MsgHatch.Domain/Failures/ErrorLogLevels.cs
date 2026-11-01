using Microsoft.Extensions.Logging;

namespace MsgHatch.Domain.Failures;

/// <summary>The log level of a failed call of a logging wrapper (section 4.15).</summary>
internal static class ErrorLogLevels
{
    public static LogLevel For(Error error) =>
        error.Type == Errors.OperationCancelled ? LogLevel.Information
        : error.Type.IsTransient ? LogLevel.Warning
        : LogLevel.Error;
}
