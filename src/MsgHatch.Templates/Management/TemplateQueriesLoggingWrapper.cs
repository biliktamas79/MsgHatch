using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Templates.Management;

/// <summary>
/// The logging wrapper of the template queries (section 4.15, D244). It logs the tenant of its key, the message type, the
/// language, the part, and counts, and never a template text, a company id, or the message of an issue (D37).
/// </summary>
internal sealed class TemplateQueriesLoggingWrapper(
    ITemplateQueries inner, TenantKey key, ILogger<TemplateQueriesLoggingWrapper> logger, TimeProvider time)
    : ITemplateQueries
{
    private string? Tenant => key.Tenant?.Value;

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<TemplateEntry>>> GetTemplatesAsync(TemplateQuery query, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetTemplatesAsync(query, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.GetTemplatesFailed(
                level, Tenant, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.GetTemplatesSucceeded(result.Value.Count, Tenant, elapsedMs);
        }

        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<TemplateSource>> GetTemplateAsync(TemplateLookup lookup, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetTemplateAsync(lookup, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.GetTemplateFailed(
                level, lookup.Type.Name, lookup.Language, lookup.Part, Tenant, elapsedMs, result.Error.Type.Code,
                result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.GetTemplateSucceeded(lookup.Type.Name, lookup.Language, lookup.Part, Tenant, elapsedMs);
        }

        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<TemplatePartInfo>>> GetTemplatePartsAsync(MessageType? type, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetTemplatePartsAsync(type, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.GetTemplatePartsFailed(
                level, Tenant, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.GetTemplatePartsSucceeded(result.Value.Count, Tenant, elapsedMs);
        }

        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<TemplateIssue>>> CheckTemplateAsync(TemplateLookup lookup, string text, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.CheckTemplateAsync(lookup, text, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.CheckTemplateFailed(
                level, lookup.Type.Name, lookup.Language, lookup.Part, Tenant, elapsedMs, result.Error.Type.Code,
                result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.CheckTemplateSucceeded(lookup.Type.Name, lookup.Language, lookup.Part, result.Value.Count, Tenant, elapsedMs);
        }

        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<TemplatePreview>>> PreviewTemplateAsync(TemplateLookup lookup, string text, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.PreviewTemplateAsync(lookup, text, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = PreviewLevelOf(result.Error);
            logger.PreviewTemplateFailed(
                level, lookup.Type.Name, lookup.Language, lookup.Part, Tenant, elapsedMs, result.Error.Type.Code,
                result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.PreviewTemplateSucceeded(lookup.Type.Name, lookup.Language, lookup.Part, result.Value.Count, Tenant, elapsedMs);
        }

        return result;
    }

    /// <summary>
    /// A draft that does not render is the input of an operator, who sees a new preview after each pause in typing, not a
    /// fault of the system.
    /// </summary>
    private static LogLevel PreviewLevelOf(Error error) =>
        error.Type == Errors.TemplateInvalid ? LogLevel.Information : ErrorLogLevels.For(error);
}
