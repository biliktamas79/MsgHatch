namespace MsgHatch.Domain.Dispatch;

/// <summary>The kind of a state of a channel (D277). A new member comes only at the end (section 4.13).</summary>
public enum ChannelStateKind
{
    /// <summary>The channel sends.</summary>
    Up,

    /// <summary>The channel is down (D106), with a reason.</summary>
    Down,
}
