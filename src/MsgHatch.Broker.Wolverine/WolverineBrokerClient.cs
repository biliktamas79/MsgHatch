using System.Text.Json;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using Wolverine;
using Wolverine.Runtime.Routing;

namespace MsgHatch.Broker.Wolverine;

/// <summary>
/// The broker client of Wolverine (D134). It sends the payload with <see cref="IMessageBus.SendAsync{T}"/>, which
/// routes by the run-time type of the payload, with the headers and the tenant id in <see cref="DeliveryOptions"/>.
/// A payload type with no Wolverine route gives the permanent <see cref="Errors.NotFound"/>, because
/// <see cref="IMessageBus.PublishAsync{T}"/> would drop the message with no error.
/// Wolverine gives no message id, so the receipt has no provider id. Wolverine writes the id of
/// <see cref="System.Diagnostics.Activity.Current"/>, the <c>send</c> span of MsgHatch, into its header <c>parent-id</c>.
/// </summary>
/// <param name="bus">
/// The factory of the message bus of one publish. <c>UseWolverineBroker</c> gives a new <c>MessageBus</c> on the
/// runtime of the host, because Wolverine registers <see cref="IMessageBus"/> as a scoped service.
/// </param>
public sealed class WolverineBrokerClient(Func<IMessageBus> bus) : IBrokerClient
{
    /// <inheritdoc />
    public async ValueTask<ErrorOr<PublishReceipt>> PublishAsync(BrokerPublish publish, CancellationToken ct)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            await bus().SendAsync(publish.Payload, OptionsOf(publish)).AsTask().WaitAsync(ct);
            return new PublishReceipt(ProviderMessageId: null);
        }
        catch (Exception exception)
        {
            return ErrorOf(exception, ct);
        }
    }

    /// <summary>Returns null, because Wolverine has no general health check of a transport (section 4.9).</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null.</returns>
    public ValueTask<Error?> CheckHealthAsync(CancellationToken ct) => ValueTask.FromResult<Error?>(null);

    private static DeliveryOptions OptionsOf(BrokerPublish publish)
    {
        var options = new DeliveryOptions { TenantId = publish.Tenant?.Value };
        foreach (var (name, value) in publish.Headers)
        {
            options.Headers[name] = value;
        }

        return options;
    }

    private static Error ErrorOf(Exception exception, CancellationToken ct) => exception switch
    {
        OperationCanceledException when ct.IsCancellationRequested => new Error(Errors.OperationCancelled),
        IndeterminateRoutesException => WolverineError(Errors.NotFound, exception),
        JsonException or NotSupportedException => WolverineError(Errors.Rejected, exception),
        _ => WolverineError(Errors.Unavailable, exception),
    };

    private static Error WolverineError(ErrorType type, Exception exception) =>
        new(type, $"Wolverine: {exception.GetType().Name}", exception);
}
