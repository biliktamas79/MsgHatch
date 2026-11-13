namespace MsgHatch.Domain.Routing;

/// <summary>The stored registry settings: the entries of the channels and of the message types (section 4.20).</summary>
/// <param name="Channels">The channel entries, in the ordinal order of the channel names.</param>
/// <param name="MessageTypes">The message type entries, in the ordinal order of the message type names.</param>
public sealed record RegistryEntries(IReadOnlyList<ChannelEntry> Channels, IReadOnlyList<MessageTypeEntry> MessageTypes);
