using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Broker;

/// <summary>
/// The client of a broker library, for example Wolverine (section 4.6, D134). The broker transport publishes each broker
/// message through it. A client returns an <see cref="Error"/> for a failure, and does not throw. A caller must not await
/// one returned <see cref="ValueTask{TResult}"/> two times.
/// </summary>
public interface IBrokerClient
{
    /// <summary>
    /// Publishes the payload with its run-time type and all headers. The transport builds the headers, so a client adds no
    /// header of its own. A client can also give the message id and the tenant to the matching fields of its library.
    /// </summary>
    /// <param name="publish">The message.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The receipt of the library, or the error.</returns>
    ValueTask<ErrorOr<PublishReceipt>> PublishAsync(BrokerPublish publish, CancellationToken ct);

    /// <summary>Checks that the broker accepts messages.</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null when the broker accepts messages, or when the library has nothing to check, else the error.</returns>
    ValueTask<Error?> CheckHealthAsync(CancellationToken ct);
}
