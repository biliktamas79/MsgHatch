using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Routing;

/// <summary>
/// The logging wrapper of the registry writer (section 4.15). It logs each change at the Information level with the
/// name of the entry and its revision (section 4.20), and never a display name or a description.
/// </summary>
internal sealed class RegistryWriterLoggingWrapper(
    IRegistryWriter inner, ILogger<RegistryWriterLoggingWrapper> logger, TimeProvider time) : IRegistryWriter
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
            logger.WriterReadFailed(level, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.WriterReadSucceeded(result.Value.Channels.Count, result.Value.MessageTypes.Count, elapsedMs);
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

    private void LogChange(string operation, string entry, int? revision, Error? error, long started)
    {
        var elapsedMs = time.GetElapsedTime(started).TotalMilliseconds;
        if (error is null)
        {
            logger.WriterChangeSucceeded(operation, entry, revision, elapsedMs);
        }
        else
        {
            var level = ErrorLogLevels.For(error);
            logger.WriterChangeFailed(level, operation, entry, revision, elapsedMs, error.Type.Code, error.Type.Name, error.Message);
        }
    }
}
