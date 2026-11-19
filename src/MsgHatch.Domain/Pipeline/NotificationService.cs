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
        Boundary.RunAsync(
            static state => state.Pipeline.ProcessAsync(state.Event, state.Context, state.Ct),
            (Pipeline: pipeline, Event: domainEvent, Context: context, Ct: ct), ct);

    public ValueTask<ErrorOr<Guid>> AcceptAsync<TEvent>(TEvent domainEvent, MessageContext context, CancellationToken ct) =>
        Boundary.RunAsync(
            static state => state.Pipeline.AcceptAsync(state.Event, state.Context, state.Ct),
            (Pipeline: pipeline, Event: domainEvent, Context: context, Ct: ct), ct);

    public ValueTask<ErrorOr<IReadOnlyList<RouteResult>>> PreviewAsync<TEvent>(
        TEvent domainEvent, MessageContext context, IReadOnlyList<MessageRoute>? routes, CancellationToken ct) =>
        Boundary.RunAsync(
            static state => state.Pipeline.PreviewAsync(state.Event, state.Context, state.Routes, state.Ct),
            (Pipeline: pipeline, Event: domainEvent, Context: context, Routes: routes, Ct: ct), ct);
}
