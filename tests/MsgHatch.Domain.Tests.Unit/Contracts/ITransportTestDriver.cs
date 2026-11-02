using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

/// <summary>
/// The driver of <see cref="TransportTestsBase{TTransport}"/>: it makes the provider behind the transport succeed, fail,
/// or wait (section 5.5).
/// </summary>
public interface ITransportTestDriver
{
    /// <summary>Gives a valid outgoing message with the envelope of the channel of the transport.</summary>
    OutgoingMessage AMessage();

    /// <summary>Makes the provider accept the next send.</summary>
    /// <returns>The receipt that the transport must give for the send.</returns>
    PublishReceipt Succeeds();

    /// <summary>Makes the provider fail the next send, so that the transport gives an error of the given type.</summary>
    void Fails(ErrorType type);

    /// <summary>Makes the provider wait in the next send until its token is cancelled.</summary>
    void WaitsUntilCancelled();
}
