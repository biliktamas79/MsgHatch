namespace MsgHatch.Domain.Dispatch;

/// <summary>Why a channel is down (D106). A new member comes only at the end (section 4.13).</summary>
public enum ChannelDownReason
{
    /// <summary>A dispatch loop found transient errors in a row. A successful health check ends it.</summary>
    Errors,

    /// <summary>A person paused the channel. Only a resume of a person ends it.</summary>
    Operator,
}
