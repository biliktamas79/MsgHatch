using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Email.Engines;

/// <summary>
/// The logging wrapper of the template engine of one key (section 4.15). It logs the tenant of its key, and never an
/// address, a payload value, or rendered text.
/// </summary>
/// <typeparam name="TPayload">The type of the template payload.</typeparam>
internal sealed class EmailTemplateEngineLoggingWrapper<TPayload>(
    IEmailTemplateEngine<TPayload> inner, TenantKey key, ILogger<EmailTemplateEngineLoggingWrapper<TPayload>> logger, TimeProvider time)
    : IEmailTemplateEngine<TPayload>
{
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IEmailTemplate<TPayload>>> PrepareAsync(EmailTemplateRequest request, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.PrepareAsync(request, ct);
        LogPrepare(request, result, time.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    private void LogPrepare(EmailTemplateRequest request, ErrorOr<IEmailTemplate<TPayload>> result, double elapsedMs)
    {
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            Log.PrepareFailed(
                logger, level, request.Type.Name, key.Tenant?.Value, elapsedMs, result.Error.Type.Code,
                result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            Log.PrepareSucceeded(logger, request.Type.Name, key.Tenant?.Value, elapsedMs);
        }
    }
}
