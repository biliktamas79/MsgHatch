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

    private void LogPrepare(TemplateRequest request, ErrorOr<ITemplate<TPayload>> result, double elapsedMs)
    {
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.PrepareFailed(
                level, request.Type.Name, request.Part, key.Tenant?.Value, elapsedMs, result.Error.Type.Code,
                result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.PrepareSucceeded(request.Type.Name, request.Part, key.Tenant?.Value, elapsedMs);
        }
    }
}
