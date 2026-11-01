using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Pipeline;

/// <summary>
/// The notification service. It is the outer boundary of the pipeline: it turns a cancellation into
/// <see cref="Errors.OperationCancelled"/>, and an unexpected exception of consumer code into <see cref="Errors.Exception"/> (section 4.12).
/// </summary>
internal sealed class NotificationService(NotificationPipeline pipeline) : INotificationService
{
    public async ValueTask<ErrorOr<NotificationResult>> ProcessAsync<TEvent>(
        TEvent domainEvent, MessageContext context, CancellationToken ct)
    {
        try
        {
            return await pipeline.ProcessAsync(domainEvent, context, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new Error(Errors.OperationCancelled);
        }
        catch (Exception exception)
        {
            return new Error(Errors.Exception, exception.Message, exception);
        }
    }
}
