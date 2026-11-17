using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Templates;

/// <summary>
/// The logging wrapper of a writable template store (section 4.15, D218). It reads through the logging wrapper of the
/// store, and logs each save and each delete at the Information level, and each list at the Debug level. It logs the
/// tenant of its key, and never the text of a template.
/// </summary>
internal sealed class TemplateWriterLoggingWrapper(
    ITemplateStore store, ITemplateWriter writer, TenantKey key, ILogger<TemplateStoreLoggingWrapper> logger, TimeProvider time)
    : ITemplateStore, ITemplateWriter
{
    public ValueTask<ErrorOr<TemplateSource?>> FindAsync(TemplateLookup lookup, CancellationToken ct) => store.FindAsync(lookup, ct);

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<TemplateEntry>>> ListAsync(TemplateQuery query, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await writer.ListAsync(query, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.ListFailed(level, key.Tenant?.Value, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.ListSucceeded(result.Value.Count, key.Tenant?.Value, elapsedMs);
        }

        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<TemplateEntry>> SaveAsync(TemplateLookup lookup, string text, long expectedVersion, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await writer.SaveAsync(lookup, text, expectedVersion, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.SaveFailed(
                level, lookup.Type.Name, lookup.Language, lookup.Part, key.Tenant?.Value, elapsedMs, result.Error.Type.Code,
                result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.SaveSucceeded(lookup.Type.Name, lookup.Language, lookup.Part, result.Value.Version, key.Tenant?.Value, elapsedMs);
        }

        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> DeleteAsync(TemplateLookup lookup, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var error = await writer.DeleteAsync(lookup, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (error is null)
        {
            logger.DeleteSucceeded(lookup.Type.Name, lookup.Language, lookup.Part, key.Tenant?.Value, elapsedMs);
        }
        else
        {
            var level = ErrorLogLevels.For(error);
            logger.DeleteFailed(
                level, lookup.Type.Name, lookup.Language, lookup.Part, key.Tenant?.Value, elapsedMs, error.Type.Code, error.Type.Name,
                error.Message);
        }

        return error;
    }
}
