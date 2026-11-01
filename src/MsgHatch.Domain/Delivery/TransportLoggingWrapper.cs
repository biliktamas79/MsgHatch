using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Delivery;

/// <summary>
/// The logging wrapper of a transport (section 4.15). It logs the tenant of the message, and never the envelope or
/// the redirect address.
/// </summary>
internal sealed class TransportLoggingWrapper(ITransport inner, ILogger<TransportLoggingWrapper> logger, TimeProvider time)
    : ITransport
{
    public ChannelId Channel => inner.Channel;

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<PublishReceipt>> SendAsync(OutgoingMessage message, string? redirectTo, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.SendAsync(message, redirectTo, ct);
        LogSend(message, result, time.GetElapsedTime(started).TotalMilliseconds);
        return result;
    }

    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<Error?> CheckHealthAsync(CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var error = await inner.CheckHealthAsync(ct);
        LogCheckHealth(error, time.GetElapsedTime(started).TotalMilliseconds);
        return error;
    }

    private void LogSend(OutgoingMessage message, ErrorOr<PublishReceipt> result, double elapsedMs)
    {
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            Log.SendFailed(
                logger, level, message.Delivery.Id, message.Type.Name, message.Channel.Name, message.Tenant?.Value,
                message.Flags, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            Log.SendSucceeded(
                logger, message.Delivery.Id, message.Type.Name, message.Channel.Name, message.Tenant?.Value, message.Flags,
                elapsedMs);
        }
    }

    private void LogCheckHealth(Error? error, double elapsedMs)
    {
        if (error is null)
        {
            Log.CheckHealthSucceeded(logger, Channel.Name, elapsedMs);
        }
        else
        {
            var level = ErrorLogLevels.For(error);
            Log.CheckHealthFailed(logger, level, Channel.Name, elapsedMs, error.Type.Code, error.Type.Name, error.Message);
        }
    }
}
