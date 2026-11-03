using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Broker.Tests.Unit.Contracts;

/// <summary>
/// The driver of <see cref="BrokerClientTestsBase{TClient}"/>: it makes the broker library behind the client succeed,
/// fail, or wait, and reads back what the library got (section 5.5).
/// </summary>
public interface IBrokerClientTestDriver
{
    /// <summary>Makes the library accept the next publish.</summary>
    /// <returns>The receipt that the client must give for the publish.</returns>
    PublishReceipt Accepts();

    /// <summary>Makes the library fail the next publish, so that the client gives an error of the given type.</summary>
    void Fails(ErrorType type);

    /// <summary>Makes the library wait in the next publish until the token of the client is cancelled.</summary>
    void WaitsUntilCancelled();

    /// <summary>Reads the one message that the library got: the payload, the headers, and the tenant id.</summary>
    (object Payload, IReadOnlyDictionary<string, string?> Headers, string? Tenant) Published();
}
