using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Email.Templates;

/// <summary>
/// The logging wrapper of a template store (section 4.15). It logs the tenant of its key, which is
/// <see cref="TenantKey.None"/> for the shared store, and never the text of a template.
/// </summary>
internal sealed class TemplateStoreLoggingWrapper(
    ITemplateStore inner, TenantKey key, ILogger<TemplateStoreLoggingWrapper> logger, TimeProvider time)
    : ITemplateStore
{
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<TemplateSource?>> FindAsync(TemplateLookup lookup, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.FindAsync(lookup, ct);
        LogFind(lookup, result, time.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    private void LogFind(TemplateLookup lookup, ErrorOr<TemplateSource?> result, double elapsedMs)
    {
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            Log.FindFailed(
                logger, level, lookup.Type.Name, key.Tenant?.Value, elapsedMs, result.Error.Type.Code,
                result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            var partCount = result.Value?.Parts.Count ?? 0;
            Log.FindSucceeded(logger, lookup.Type.Name, partCount, key.Tenant?.Value, elapsedMs);
        }
    }
}
