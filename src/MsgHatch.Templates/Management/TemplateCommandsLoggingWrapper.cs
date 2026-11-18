using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Templates.Management;

/// <summary>
/// The logging wrapper of the template commands (section 4.15, D244). It logs the tenant of its key, the message type, the
/// language, the part, and the version, and never a template text or a company id (D37).
/// </summary>
internal sealed class TemplateCommandsLoggingWrapper(
    ITemplateCommands inner, TenantKey key, ILogger<TemplateCommandsLoggingWrapper> logger, TimeProvider time)
    : ITemplateCommands
{
    private string? Tenant => key.Tenant?.Value;

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<TemplateEntry>> SaveTemplateAsync(TemplateLookup lookup, string text, long expectedVersion, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.SaveTemplateAsync(lookup, text, expectedVersion, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.SaveTemplateFailed(
                level, lookup.Type.Name, lookup.Language, lookup.Part, Tenant, elapsedMs, result.Error.Type.Code,
                result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.SaveTemplateSucceeded(lookup.Type.Name, lookup.Language, lookup.Part, result.Value.Version, Tenant, elapsedMs);
        }

        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> DeleteTemplateAsync(TemplateLookup lookup, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var error = await inner.DeleteTemplateAsync(lookup, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (error is null)
        {
            logger.DeleteTemplateSucceeded(lookup.Type.Name, lookup.Language, lookup.Part, Tenant, elapsedMs);
        }
        else
        {
            var level = ErrorLogLevels.For(error);
            logger.DeleteTemplateFailed(
                level, lookup.Type.Name, lookup.Language, lookup.Part, Tenant, elapsedMs, error.Type.Code, error.Type.Name,
                error.Message);
        }

        return error;
    }
}
