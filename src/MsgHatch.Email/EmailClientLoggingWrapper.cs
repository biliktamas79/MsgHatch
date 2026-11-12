using System.Runtime.CompilerServices;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Email;

/// <summary>
/// The logging wrapper of an email client (section 4.15). It logs the outgoing message id, and never an address, the
/// subject, or a body of the email.
/// </summary>
internal sealed class EmailClientLoggingWrapper(IEmailClient inner, ILogger<EmailClientLoggingWrapper> logger, TimeProvider time)
    : IEmailClient
{
    [AsyncMethodBuilder(typeof(PoolingAsyncValueTaskMethodBuilder<>))]
    public async ValueTask<ErrorOr<PublishReceipt>> SendAsync(Guid messageId, EmailMessage message, CancellationToken ct)
    {
        var started = time.GetTimestamp();
        var result = await inner.SendAsync(messageId, message, ct);
        LogSend(messageId, result, time.GetElapsedTime(started).TotalMilliseconds);
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

    private void LogSend(Guid messageId, ErrorOr<PublishReceipt> result, double elapsedMs)
    {
        if (result.IsError)
        {
            var level = ErrorLogLevels.For(result.Error);
            logger.SendFailed(
                level, messageId, elapsedMs, result.Error.Type.Code, result.Error.Type.Name, result.Error.Message);
        }
        else
        {
            logger.SendSucceeded(messageId, elapsedMs);
        }
    }

    private void LogCheckHealth(Error? error, double elapsedMs)
    {
        if (error is null)
        {
            logger.CheckHealthSucceeded(elapsedMs);
        }
        else
        {
            var level = ErrorLogLevels.For(error);
            logger.CheckHealthFailed(level, elapsedMs, error.Type.Code, error.Type.Name, error.Message);
        }
    }
}
