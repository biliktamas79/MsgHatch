using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Pipeline;

/// <summary>
/// The notification service. It is the outer boundary of the pipeline: it turns a cancellation into
/// <see cref="Errors.OperationCancelled"/>, and an unexpected exception of consumer code into <see cref="Errors.Exception"/> (section 4.12).
/// </summary>
internal sealed class NotificationService(NotificationPipeline pipeline) : INotificationService
{
    public ValueTask<ErrorOr<NotificationResult>> ProcessAsync<TEvent>(
        TEvent domainEvent, MessageContext context, CancellationToken ct) =>
        AtBoundaryAsync(() => pipeline.ProcessAsync(domainEvent, context, ct), ct);

    public ValueTask<ErrorOr<IReadOnlyList<RouteResult>>> PreviewAsync<TEvent>(
        TEvent domainEvent, MessageContext context, IReadOnlyList<MessageRoute>? routes, CancellationToken ct) =>
        AtBoundaryAsync(() => pipeline.PreviewAsync(domainEvent, context, routes, ct), ct);

    private static async ValueTask<ErrorOr<T>> AtBoundaryAsync<T>(Func<ValueTask<ErrorOr<T>>> run, CancellationToken ct)
    {
        try
        {
            return await run();
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
