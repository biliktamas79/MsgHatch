using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>The upgrade of the claimed outgoing messages of one content type, with no envelope type (section 4.13).</summary>
internal interface IEnvelopeUpgrade
{
    string ContentType { get; }

    /// <summary>Upgrades a claimed outgoing message of an old envelope version to the current version.</summary>
    /// <returns>
    /// The message of the current version, or <see cref="Errors.UnsupportedVersion"/> for a version with no upgrade, for
    /// example a version that is newer than the code (D25).
    /// </returns>
    ErrorOr<OutgoingMessage> Upgrade(OutgoingMessage message);
}
