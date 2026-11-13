using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Data.EFCore;

/// <summary>
/// The boundary of the database operations of an EF Core store: each operation gets its own context from the pooled
/// factory (D130), and returns an <see cref="Error"/> in place of an exception. A database failure gives
/// <see cref="Errors.Unavailable"/>, a stored value that a person changed into an invalid value gives
/// <see cref="Errors.SerializationFailed"/>, a cancelled token gives <see cref="Errors.OperationCancelled"/>, and a
/// provider with no SQL statements of the stores gives <see cref="Errors.NotFound"/> (D175).
/// </summary>
/// <typeparam name="TContext">The context of the store.</typeparam>
/// <param name="factory">The pooled factory of the contexts.</param>
internal sealed class StoreContexts<TContext>(IDbContextFactory<TContext> factory)
    where TContext : DbContext
{
    public async ValueTask<ErrorOr<T>> RunAsync<T>(Func<TContext, ValueTask<ErrorOr<T>>> operation, CancellationToken ct)
    {
        try
        {
            await using var context = await factory.CreateDbContextAsync(ct);
            return StoreModel.HasStatements(context.Database.ProviderName)
                ? await operation(context)
                : new Error(Errors.NotFound, $"The EF Core stores have no SQL statements for the provider {context.Database.ProviderName}.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new Error(Errors.OperationCancelled);
        }
        catch (Exception exception) when (ErrorOf(exception) is { } error)
        {
            return error;
        }
    }

    public async ValueTask<Error?> RunAsync(Func<TContext, ValueTask<Error?>> operation, CancellationToken ct)
    {
        var result = await RunAsync<bool>(async context => await operation(context) is { } error ? error : true, ct);
        return result.Error;
    }

    private static Error? ErrorOf(Exception exception) => exception switch
    {
        DbException or DbUpdateException or RetryLimitExceededException or TimeoutException
            or InvalidOperationException { InnerException: DbException } => new Error(Errors.Unavailable, exception.Message, exception),
        FormatException or ArgumentException or JsonException => new Error(Errors.SerializationFailed, exception.Message, exception),
        _ => null,
    };
}
