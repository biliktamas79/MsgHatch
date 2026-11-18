using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Templates;

/// <summary>
/// The logging wrapper of the template engine of one key (section 4.15). It logs the tenant of its key, and never an
/// address, a payload value, or rendered text.
/// </summary>
/// <typeparam name="TPayload">The type of the template payload.</typeparam>
internal sealed class TemplateEngineLoggingWrapper<TPayload>(
    ITemplateEngine<TPayload> inner, TenantKey key, ILogger<TemplateEngineLoggingWrapper<TPayload>> logger, TimeProvider time)
    : ITemplateEngine<TPayload>
{
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<ITemplate<TPayload>>> PrepareAsync(TemplateRequest request, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.PrepareAsync(request, ct);
        LogPrepare(request, result, time.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<TemplateIssue>>> CheckAsync(TemplateLookup lookup, string text, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.CheckAsync(lookup, text, ct);
        LogCheck(lookup, result, time.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<string>> RenderAsync(TemplateLookup lookup, string text, TPayload payload, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.RenderAsync(lookup, text, payload, ct);
        LogRender(lookup, result, time.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    private void LogRender(TemplateLookup lookup, ErrorOr<string> result, double elapsedMs)
    {
        if (result.IsError)
        {
            var level = CheckLevelOf(result.Error);
            logger.RenderDraftFailed(
                level, lookup.Type.Name, lookup.Part, key.Tenant?.Value, elapsedMs, result.Error.Type.Code,
                result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.RenderDraftSucceeded(lookup.Type.Name, lookup.Part, key.Tenant?.Value, elapsedMs);
        }
    }

    private void LogCheck(TemplateLookup lookup, ErrorOr<IReadOnlyList<TemplateIssue>> result, double elapsedMs)
    {
        if (result.IsError)
        {
            var level = CheckLevelOf(result.Error);
            logger.CheckFailed(
                level, lookup.Type.Name, lookup.Part, key.Tenant?.Value, elapsedMs, result.Error.Type.Code, result.Error.Type.Name,
                result.Error.Message);
        }
        else if (result.Value.Count > 0)
        {
            logger.CheckFoundIssues(lookup.Type.Name, lookup.Part, result.Value.Count, key.Tenant?.Value, elapsedMs);
        }
        else
        {
            logger.CheckSucceeded(lookup.Type.Name, lookup.Part, key.Tenant?.Value, elapsedMs);
        }
    }

    /// <summary>
    /// A text that the check or the render of a draft rejects is an expected answer to the input of an operator, not a fault of
    /// the system.
    /// </summary>
    private static LogLevel CheckLevelOf(Error error) =>
        error.Type == Errors.TemplateInvalid ? LogLevel.Information : ErrorLogLevels.For(error);

    private void LogPrepare(TemplateRequest request, ErrorOr<ITemplate<TPayload>> result, double elapsedMs)
    {
        if (result.IsError)
        {
            var level = LevelOf(request, result.Error);
            logger.PrepareFailed(
                level, request.Type.Name, request.Part, key.Tenant?.Value, elapsedMs, result.Error.Type.Code,
                result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.PrepareSucceeded(request.Type.Name, request.Part, key.Tenant?.Value, elapsedMs);
        }
    }

    private static LogLevel LevelOf(TemplateRequest request, Error error) =>
        request.Optional && error.Type == Errors.NotFound ? LogLevel.Debug : ErrorLogLevels.For(error);
}
