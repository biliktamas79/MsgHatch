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
    public async ValueTask<Error?> CheckAsync(TemplateLookup lookup, string text, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var error = await inner.CheckAsync(lookup, text, ct);
        LogCheck(lookup, error, time.GetElapsedTime(started).TotalMilliseconds);
        return error;
    }

    private void LogCheck(TemplateLookup lookup, Error? error, double elapsedMs)
    {
        if (error is null)
        {
            logger.CheckSucceeded(lookup.Type.Name, lookup.Part, key.Tenant?.Value, elapsedMs);
        }
        else
        {
            var level = CheckLevelOf(error);
            logger.CheckFailed(
                level, lookup.Type.Name, lookup.Part, key.Tenant?.Value, elapsedMs, error.Type.Code, error.Type.Name, error.Message);
        }
    }

    /// <summary>A text that the check rejects is an expected answer to the input of an operator, not a fault of the system.</summary>
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
