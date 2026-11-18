using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Management;

/// <summary>
/// The logging wrapper of the registry queries (section 4.15, D244, D269). It logs each call with the tenant of its key,
/// the count or the error, and the duration, and never a display name or a description.
/// </summary>
internal sealed class RegistryQueriesLoggingWrapper(
    IRegistryQueries inner, TenantKey key, ILogger<RegistryQueriesLoggingWrapper> logger, TimeProvider time) : IRegistryQueries
{
    private string? Tenant => key.Tenant?.Value;

    public static IRegistryQueries Wrap(IServiceProvider tenantServices, TenantKey key, IRegistryQueries inner) =>
        new RegistryQueriesLoggingWrapper(
            inner, key, Loggers.For<RegistryQueriesLoggingWrapper>(tenantServices), tenantServices.GetRequiredService<TimeProvider>());

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<RegistryChannel>>> GetChannelsAsync(CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetChannelsAsync(ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.GetChannelsFailed(
                level, Tenant, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.GetChannelsSucceeded(result.Value.Count, Tenant, elapsedMs);
        }

        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<ChannelState>>> GetChannelHistoryAsync(ChannelId channel, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetChannelHistoryAsync(channel, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.GetChannelHistoryFailed(
                level, channel.Name, Tenant, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.GetChannelHistorySucceeded(result.Value.Count, channel.Name, Tenant, elapsedMs);
        }

        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<IReadOnlyList<RegistryMessageType>>> GetMessageTypesAsync(CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetMessageTypesAsync(ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.GetMessageTypesFailed(
                level, Tenant, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.GetMessageTypesSucceeded(result.Value.Count, Tenant, elapsedMs);
        }

        return result;
    }
}
