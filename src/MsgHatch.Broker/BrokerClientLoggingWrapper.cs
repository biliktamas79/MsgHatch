using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Broker;

/// <summary>
/// The logging wrapper of a broker client (section 4.15). It logs the outgoing message id, and never the payload or a
/// header value.
/// </summary>
internal sealed class BrokerClientLoggingWrapper(IBrokerClient inner, ILogger<BrokerClientLoggingWrapper> logger, TimeProvider time)
    : IBrokerClient
{
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<PublishReceipt>> PublishAsync(BrokerPublish publish, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.PublishAsync(publish, ct);
        LogPublish(publish.MessageId, result, time.GetElapsedTime(started).TotalMilliseconds);
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

    private static LogLevel LevelOf(Error error) =>
        error.Type == Errors.OperationCancelled ? LogLevel.Information
        : error.Type.IsTransient ? LogLevel.Warning
        : LogLevel.Error;

    private void LogPublish(Guid messageId, ErrorOr<PublishReceipt> result, double elapsedMs)
    {
        if (result.IsError)
        {
            var level = LevelOf(result.Error);
            Log.PublishFailed(
                logger, level, messageId, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            Log.PublishSucceeded(logger, messageId, elapsedMs);
        }
    }

    private void LogCheckHealth(Error? error, double elapsedMs)
    {
        if (error is null)
        {
            Log.CheckHealthSucceeded(logger, elapsedMs);
        }
        else
        {
            var level = LevelOf(error);
            Log.CheckHealthFailed(logger, level, elapsedMs, error.Type.Code, error.Type.Name, error.Message);
        }
    }
}
