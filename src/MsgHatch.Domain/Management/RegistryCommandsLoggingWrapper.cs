using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Management;

/// <summary>
/// The logging wrapper of the registry commands (section 4.15, D244, D283). It logs each check with the channel, the
/// tenant of its key, the error name of the health check and the change, or the error of the command, and the duration.
/// </summary>
internal sealed class RegistryCommandsLoggingWrapper(
    IRegistryCommands inner, TenantKey key, ILogger<RegistryCommandsLoggingWrapper> logger, TimeProvider time) : IRegistryCommands
{
    public static IRegistryCommands Wrap(IServiceProvider tenantServices, TenantKey key, IRegistryCommands inner) =>
        new RegistryCommandsLoggingWrapper(
            inner, key, Loggers.For<RegistryCommandsLoggingWrapper>(tenantServices), tenantServices.GetRequiredService<TimeProvider>());

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<ChannelCheck>> CheckChannelAsync(ChannelId channel, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.CheckChannelAsync(channel, ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.CheckChannelFailed(
                level, channel.Name, key.Tenant?.Value, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.CheckChannelSucceeded(channel.Name, result.Value.ErrorName, result.Value.Changed, key.Tenant?.Value, elapsedMs);
        }

        return result;
    }
}
