using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Management;

/// <summary>
/// One channel of <see cref="IRegistryQueries"/> (D268): the values of <see cref="IMessageRegistry"/>, with the stored
/// settings on top, and the revision of the stored entry. It has no settings type, so it can go over HTTP.
/// </summary>
/// <param name="Id">The id of the channel, for example <c>Email</c>.</param>
/// <param name="Name">The display name, from the stored entry or from the code.</param>
/// <param name="Description">The description, or null.</param>
/// <param name="Features">The features of the channel.</param>
/// <param name="Enabled">False when the pipeline skips each route on the channel (D118).</param>
/// <param name="Revision">The revision of the stored entry, or 0 for no entry.</param>
public sealed record RegistryChannel(ChannelId Id, string Name, string? Description, ChannelFeatures Features, bool Enabled, int Revision);
