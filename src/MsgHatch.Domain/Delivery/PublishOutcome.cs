namespace MsgHatch.Domain.Delivery;

/// <summary>The outcome of one publish try. A new member comes only at the end (section 4.13).</summary>
public enum PublishOutcome
{
    /// <summary>The transport sent the message.</summary>
    Succeeded,

    /// <summary>The transport failed with an error that a later try can fix.</summary>
    TransientError,

    /// <summary>The transport failed with an error that no later try can fix.</summary>
    PermanentError,

    /// <summary>The message was not sent, because its expiry is in the past (D94).</summary>
    Expired,

    /// <summary>The test message was not sent, because the test delivery of its channel is <c>Suppress</c> (D83).</summary>
    Suppressed,
}
