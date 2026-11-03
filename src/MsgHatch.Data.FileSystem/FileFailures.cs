using System.Text.Json;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Data.FileSystem;

/// <summary>
/// The boundary of the file operations of the stores: a store returns an <see cref="Error"/>, and does not throw. A file
/// system failure gives <see cref="Errors.Unavailable"/>, and a file that a person changed into invalid JSON gives
/// <see cref="Errors.SerializationFailed"/>.
/// </summary>
internal static class FileFailures
{
    public static Error? Run(Func<Error?> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception exception) when (ErrorOf(exception) is { } error)
        {
            return error;
        }
    }

    public static ErrorOr<T> Run<T>(Func<ErrorOr<T>> operation)
    {
        try
        {
            return operation();
        }
        catch (Exception exception) when (ErrorOf(exception) is { } error)
        {
            return error;
        }
    }

    private static Error? ErrorOf(Exception exception) => exception switch
    {
        IOException or UnauthorizedAccessException => new Error(Errors.Unavailable, exception.Message, exception),
        JsonException => new Error(Errors.SerializationFailed, exception.Message, exception),
        _ => null,
    };
}
