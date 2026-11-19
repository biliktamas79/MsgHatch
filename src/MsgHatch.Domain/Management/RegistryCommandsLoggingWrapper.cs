using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Management;

/// <summary>
/// The logging wrapper of the registry commands (section 4.15, D244, D283, D284). It logs each check with the channel, the
/// tenant of its key, the error name of the health check and the change, or the error of the command, and the duration.
/// It logs each save and each reset with the channel or the message type, the tenant of its key, and the new revision or
/// the error, and never a display name or a description.
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

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<ChannelEntry>> SaveChannelAsync(ChannelEntry entry, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.SaveChannelAsync(entry, ct);
        LogChange(nameof(SaveChannelAsync), entry.Channel.Name, result.IsError ? entry.Revision : result.Value.Revision, result.Error, started);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<MessageTypeEntry>> SaveMessageTypeAsync(MessageTypeEntry entry, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.SaveMessageTypeAsync(entry, ct);
        LogChange(nameof(SaveMessageTypeAsync), entry.Type.Name, result.IsError ? entry.Revision : result.Value.Revision, result.Error, started);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> ResetChannelAsync(ChannelId channel, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var error = await inner.ResetChannelAsync(channel, ct);
        LogChange(nameof(ResetChannelAsync), channel.Name, revision: null, error, started);
        return error;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> ResetMessageTypeAsync(MessageType type, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var error = await inner.ResetMessageTypeAsync(type, ct);
        LogChange(nameof(ResetMessageTypeAsync), type.Name, revision: null, error, started);
        return error;
    }

    /// <summary>Logs a save or a reset with the name of the entry and its revision, and never a display name or a description.</summary>
    private void LogChange(string operation, string entry, int? revision, Error? error, long started)
    {
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (error is null)
        {
            logger.ChangeEntrySucceeded(operation, entry, revision, key.Tenant?.Value, elapsedMs);
        }
        else
        {
            var level = ErrorLogLevels.For(error);
            logger.ChangeEntryFailed(level, operation, entry, revision, key.Tenant?.Value, elapsedMs, error.Type.Code, error.Type.Name, error.Message);
        }
    }
}
