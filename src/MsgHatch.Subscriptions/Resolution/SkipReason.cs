namespace MsgHatch.Subscriptions.Resolution;

/// <summary>The rule that removed a subscription for the same target and channel (D92).</summary>
public enum SkipReason
{
    /// <summary>A more specific level has a subscription or an opt-out for the channel (D72).</summary>
    Overridden,

    /// <summary>A mandatory subscription of a higher level applies to the channel (D88).</summary>
    Locked,

    /// <summary>Another subscription gave the same target and channel, and comes first in the merge order (D91).</summary>
    Merged,
}
