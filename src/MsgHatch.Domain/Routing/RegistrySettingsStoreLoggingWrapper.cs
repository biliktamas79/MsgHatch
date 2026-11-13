using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Routing;

/// <summary>
/// The logging wrapper of the registry settings store (section 4.15). It logs the name and the revision of an entry,
/// and never its display name or its description.
/// </summary>
internal sealed class RegistrySettingsStoreLoggingWrapper(
    IRegistrySettingsStore inner, ILogger<RegistrySettingsStoreLoggingWrapper> logger, TimeProvider time) : IRegistrySettingsStore
{
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<RegistryEntries>> GetEntriesAsync(CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.GetEntriesAsync(ct);
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.StoreReadFailed(level, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.StoreReadSucceeded(result.Value.Channels.Count, result.Value.MessageTypes.Count, elapsedMs);
        }

        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> SaveChannelEntryAsync(ChannelEntry entry, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var error = await inner.SaveChannelEntryAsync(entry, ct);
        LogChange(nameof(SaveChannelEntryAsync), entry.Channel.Name, entry.Revision, error, started);
        return error;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> SaveMessageTypeEntryAsync(MessageTypeEntry entry, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var error = await inner.SaveMessageTypeEntryAsync(entry, ct);
        LogChange(nameof(SaveMessageTypeEntryAsync), entry.Type.Name, entry.Revision, error, started);
        return error;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> DeleteChannelEntryAsync(ChannelId channel, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var error = await inner.DeleteChannelEntryAsync(channel, ct);
        LogChange(nameof(DeleteChannelEntryAsync), channel.Name, revision: null, error, started);
        return error;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> DeleteMessageTypeEntryAsync(MessageType type, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var error = await inner.DeleteMessageTypeEntryAsync(type, ct);
        LogChange(nameof(DeleteMessageTypeEntryAsync), type.Name, revision: null, error, started);
        return error;
    }

    private void LogChange(string operation, string entry, int? revision, Error? error, long started)
    {
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (error is null)
        {
            logger.StoreChangeSucceeded(operation, entry, revision, elapsedMs);
        }
        else
        {
            var level = ErrorLogLevels.For(error);
            logger.StoreChangeFailed(level, operation, entry, revision, elapsedMs, error.Type.Code, error.Type.Name, error.Message);
        }
    }
}
