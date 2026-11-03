using System.Text.Json;
using Microsoft.AspNetCore.SignalR;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.SignalR;

/// <summary>
/// The transport of the SignalR channel (D129). It sends the payload of each <see cref="ISignalREnvelope"/>, with the
/// message type name as the method name, to the SignalR user or company group of the target (<see cref="SignalRUsers"/>,
/// <see cref="SignalRGroups"/>), or to the group of a redirect (section 4.8). SignalR has no delivery guarantee, so a
/// receipt means that SignalR accepted the message, and it has no provider id.
/// </summary>
/// <typeparam name="THub">The hub of the host.</typeparam>
/// <param name="hub">The hub context.</param>
public sealed class SignalRTransport<THub>(IHubContext<THub> hub) : ITransport
    where THub : Hub
{
    /// <inheritdoc />
    public ChannelId Channel => SignalRChannel.Id;

    /// <inheritdoc />
    public async ValueTask<ErrorOr<PublishReceipt>> SendAsync(OutgoingMessage message, string? redirectTo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Content is not ISignalREnvelope envelope)
        {
            return new Error(Errors.ValidationFailed, $"The content of a SignalR message is not an {nameof(ISignalREnvelope)}.");
        }

        var clients = redirectTo is null
            ? ClientsOf(message.Tenant, message.Target)
            : ErrorOr<IClientProxy>.ForValue(hub.Clients.Group(redirectTo));
        if (clients.IsError)
        {
            return clients.Error;
        }

        try
        {
            await clients.Value.SendAsync(message.Type.Name, envelope.Payload, ct);
            return new PublishReceipt(ProviderMessageId: null);
        }
        catch (Exception exception)
        {
            return ErrorOf(exception, ct);
        }
    }

    /// <summary>Returns null, because SignalR has nothing to check (section 4.9).</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null.</returns>
    public ValueTask<Error?> CheckHealthAsync(CancellationToken ct) => ValueTask.FromResult<Error?>(null);

    private ErrorOr<IClientProxy> ClientsOf(TenantId? tenant, DeliveryTarget target) => target.Kind switch
    {
        DeliveryTargetKind.User => UserId.Create(target.Id)
            .ThenIfSuccess(user => ErrorOr<IClientProxy>.ForValue(hub.Clients.User(SignalRUsers.ForUser(tenant, user)))),
        DeliveryTargetKind.Company => CompanyId.Create(target.Id)
            .ThenIfSuccess(company => ErrorOr<IClientProxy>.ForValue(hub.Clients.Group(SignalRGroups.ForCompany(tenant, company)))),
        _ => new Error(Errors.ValidationFailed, $"The target kind {target.Kind} is not known."),
    };

    private static Error ErrorOf(Exception exception, CancellationToken ct) => exception switch
    {
        OperationCanceledException when ct.IsCancellationRequested => new Error(Errors.OperationCancelled),
        JsonException or NotSupportedException => SignalRError(Errors.Rejected, exception),
        _ => SignalRError(Errors.Unavailable, exception),
    };

    private static Error SignalRError(ErrorType type, Exception exception) =>
        new(type, $"SignalR: {exception.GetType().Name}", exception);
}
