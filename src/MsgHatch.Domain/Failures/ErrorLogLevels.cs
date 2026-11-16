using Microsoft.Extensions.Logging;

namespace MsgHatch.Domain.Failures;

/// <summary>
/// The log level of a failed call of a logging wrapper (section 4.15). Each wrapper of the library uses it, and a
/// wrapper of another package can use it to log with the same levels.
/// </summary>
public static class ErrorLogLevels
{
    /// <summary>Gives the log level of an error.</summary>
    /// <param name="error">The error of the failed call.</param>
    /// <returns>Information for a cancelled operation, Warning for a transient error, and Error for any other error.</returns>
    public static LogLevel For(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return error.Type == Errors.OperationCancelled ? LogLevel.Information
            : error.Type.IsTransient ? LogLevel.Warning
            : LogLevel.Error;
    }
}
