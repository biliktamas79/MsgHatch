using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Broker;

/// <summary>
/// The transport of the broker channel (D129). It gives the payload of each <see cref="IBrokerEnvelope"/> to
/// <see cref="IBrokerClient"/>, with the headers of the envelope and the headers of the library: <c>msghatch-id</c> with
/// the outgoing message id, so that a receiver can find duplicates, <c>msghatch-tenant</c> for a message of a tenant,
/// <c>msghatch-test</c> for a test message, and <c>msghatch-redirect</c> for a redirect (section 4.8). A header of the
/// library wins over a header of the envelope with the same name.
/// </summary>
/// <param name="client">The broker client.</param>
public sealed class BrokerTransport(IBrokerClient client) : ITransport
{
    /// <inheritdoc />
    public ChannelId Channel => BrokerChannel.Id;

    /// <inheritdoc />
    public ValueTask<ErrorOr<PublishReceipt>> SendAsync(OutgoingMessage message, string? redirectTo, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        if (message.Content is not IBrokerEnvelope envelope)
        {
            return ValueTask.FromResult<ErrorOr<PublishReceipt>>(
                new Error(Errors.ValidationFailed, $"The content of a broker message is not an {nameof(IBrokerEnvelope)}."));
        }

        return client.PublishAsync(
            new BrokerPublish(message.Delivery.Id, message.Tenant, envelope.Payload, HeadersOf(message, envelope, redirectTo)), ct);
    }

    /// <inheritdoc />
    public ValueTask<Error?> CheckHealthAsync(CancellationToken ct) => client.CheckHealthAsync(ct);

    private static Dictionary<string, string> HeadersOf(OutgoingMessage message, IBrokerEnvelope envelope, string? redirectTo)
    {
        var headers = envelope.Headers is null
            ? new Dictionary<string, string>(StringComparer.Ordinal)
            : new Dictionary<string, string>(envelope.Headers, StringComparer.Ordinal);
        headers["msghatch-id"] = message.Delivery.Id.ToString();
        if (message.Tenant is { } tenant)
        {
            headers["msghatch-tenant"] = tenant.Value;
        }

        if (message.Flags.HasFlag(MessageFlags.Test))
        {
            headers["msghatch-test"] = "true";
        }

        if (redirectTo is not null)
        {
            headers["msghatch-redirect"] = redirectTo;
        }

        return headers;
    }
}
