using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using MsgHatch.Data.EFCore.Providers;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Data.EFCore.Model;

/// <summary>
/// The boundary of the database operations of an EF Core store: each operation gets its own context from the pooled
/// factory (D130), and returns an <see cref="Error"/> in place of an exception. A cancelled token gives
/// <see cref="Errors.OperationCancelled"/>. Then the classifiers of the host, the dialect of the provider package, and the
/// built-in mapping classify the exception, and the first result that is not null wins (D141, D322). The built-in mapping
/// gives <see cref="Errors.Unavailable"/> for a database failure, and <see cref="Errors.SerializationFailed"/> for a stored
/// value that a person changed into an invalid value. A provider that is not relational gives
/// <see cref="Errors.NotFound"/> (D175, D318).
/// </summary>
/// <typeparam name="TContext">The context of the store.</typeparam>
/// <param name="factory">The pooled factory of the contexts.</param>
/// <param name="classifiers">The exception classifiers of the host, in the order of their registration, or null for none.</param>
internal sealed class StoreContexts<TContext>(IDbContextFactory<TContext> factory, IEnumerable<IExceptionClassifier<TContext>>? classifiers)
    where TContext : DbContext
{
    private readonly IExceptionClassifier<TContext>[] _classifiers = [.. classifiers ?? []];

    public async ValueTask<ErrorOr<T>> RunAsync<T>(Func<TContext, ValueTask<ErrorOr<T>>> operation, CancellationToken ct)
    {
        IEntityFrameworkStoreDialect? dialect = null;
        try
        {
            await using var context = await factory.CreateDbContextAsync(ct);
            dialect = StoreOptionsExtension.Of(context).Dialect;
            return context.Database.IsRelational()
                ? await operation(context)
                : new Error(Errors.NotFound, $"The EF Core stores need a relational provider, and {context.Database.ProviderName} is not relational.");
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new Error(Errors.OperationCancelled);
        }
        catch (Exception exception) when (ErrorOf(exception, dialect) is { } error)
        {
            return error;
        }
    }

    public async ValueTask<Error?> RunAsync(Func<TContext, ValueTask<Error?>> operation, CancellationToken ct)
    {
        var result = await RunAsync<bool>(async context => await operation(context) is { } error ? error : true, ct);
        return result.Error;
    }

    private Error? ErrorOf(Exception exception, IEntityFrameworkStoreDialect? dialect) =>
        (_classifiers.Classify(exception) ?? DialectErrorTypeOf(exception, dialect)) is { } type
            ? new Error(type, exception.Message, exception)
            : BuiltInErrorOf(exception);

    private static ErrorType? DialectErrorTypeOf(Exception exception, IEntityFrameworkStoreDialect? dialect) =>
        dialect is not null && DatabaseExceptionOf(exception) is { } databaseException ? dialect.ErrorTypeOf(databaseException) : null;

    private static DbException? DatabaseExceptionOf(Exception? exception) => exception switch
    {
        null => null,
        DbException databaseException => databaseException,
        _ => DatabaseExceptionOf(exception.InnerException),
    };

    private static Error? BuiltInErrorOf(Exception exception) => exception switch
    {
        DbException or DbUpdateException or RetryLimitExceededException or TimeoutException
            or InvalidOperationException { InnerException: DbException } => new Error(Errors.Unavailable, exception.Message, exception),
        FormatException or ArgumentException or JsonException => new Error(Errors.SerializationFailed, exception.Message, exception),
        _ => null,
    };
}
