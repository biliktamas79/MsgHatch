using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Management;

/// <summary>
/// The queries of the registry for a management client, for example an admin page (D268): the channels and the message
/// types of <see cref="IMessageRegistry"/>, with the revision of each stored entry. The registry belongs to the whole
/// system (D114), so each tenant key gives the same values (D269).
/// </summary>
public interface IRegistryQueries
{
    /// <summary>Gives each registered channel, with the revision of its stored entry, or 0 for no entry.</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The channels in the order of the registry, or the error of the registry writer.</returns>
    ValueTask<ErrorOr<IReadOnlyList<RegistryChannel>>> GetChannelsAsync(CancellationToken ct);

    /// <summary>Gives each registered message type, with the revision of its stored entry, or 0 for no entry.</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The message types in the order of the registry, or the error of the registry writer.</returns>
    ValueTask<ErrorOr<IReadOnlyList<RegistryMessageType>>> GetMessageTypesAsync(CancellationToken ct);
}
