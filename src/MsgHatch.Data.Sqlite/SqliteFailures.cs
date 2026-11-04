using System.Text.Json;
using Microsoft.Data.Sqlite;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Data.Sqlite;

/// <summary>
/// The boundary of the database operations of the stores: a store returns an <see cref="Error"/>, and does not throw. A
/// SQLite failure, for example a busy database after the command timeout, gives <see cref="Errors.Unavailable"/>. A
/// down state or a subscription that a person changed into an invalid value gives
/// <see cref="Errors.SerializationFailed"/>.
/// </summary>
internal static class SqliteFailures
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
        SqliteException => new Error(Errors.Unavailable, exception.Message, exception),
        FormatException or ArgumentException or JsonException => new Error(Errors.SerializationFailed, exception.Message, exception),
        _ => null,
    };
}
