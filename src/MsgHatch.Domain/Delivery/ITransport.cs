using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Delivery;

/// <summary>
/// The transport of one channel (D129). One transport exists for each channel, and a host that registers it is a
/// publisher of that channel (D101). A transport returns an <see cref="Error"/> for a failure, and does not throw.
/// A caller must not await one returned <see cref="ValueTask{TResult}"/> two times.
/// </summary>
public interface ITransport
{
    /// <summary>The channel of the transport.</summary>
    ChannelId Channel { get; }

    /// <summary>Sends <see cref="OutgoingMessage.Content"/>, the channel envelope of the message.</summary>
    /// <param name="message">The message.</param>
    /// <param name="redirectTo">
    /// A test address or a test group that replaces the target of the message, or null (section 4.8).
    /// </param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The receipt of the provider, or the error.</returns>
    ValueTask<ErrorOr<PublishReceipt>> SendAsync(OutgoingMessage message, string? redirectTo, CancellationToken ct);

    /// <summary>Checks that the channel can send. A transport with nothing to check returns null.</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null when the channel can send, else the error.</returns>
    ValueTask<Error?> CheckHealthAsync(CancellationToken ct);
}
