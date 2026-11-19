using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Management;

/// <summary>
/// One channel of <see cref="IRegistryQueries"/> (D268): the values of <see cref="IMessageRegistry"/>, with the stored
/// settings on top, the revision of the stored entry, and the current state row of the channel (D278).
/// It has no settings type, so it can go over HTTP.
/// </summary>
/// <param name="Id">The id of the channel, for example <c>Email</c>.</param>
/// <param name="Name">The display name, from the stored entry or from the code.</param>
/// <param name="Description">The description, or null.</param>
/// <param name="Features">The features of the channel.</param>
/// <param name="Enabled">False when the pipeline skips each route on the channel (D118).</param>
/// <param name="Revision">The revision of the stored entry, or 0 for no entry.</param>
/// <param name="Current">
/// The latest state row of the channel in the outbox dispatch store (D277), with its kind, its start, and the kind and the
/// duration of the previous state, or null for a channel with no row, which was never down.
/// </param>
/// <param name="Entry">
/// The stored entry of the channel, or null for no entry and in a host with no registry writer (D286). A null value of the
/// entry is the value of the code.
/// </param>
public sealed record RegistryChannel(
    ChannelId Id, string Name, string? Description, ChannelFeatures Features, bool Enabled, int Revision, ChannelState? Current = null,
    ChannelEntry? Entry = null);
