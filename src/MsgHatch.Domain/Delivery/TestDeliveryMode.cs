namespace MsgHatch.Domain.Delivery;

/// <summary>How a channel handles a test message (D83). A new member comes only at the end (section 4.13).</summary>
public enum TestDeliveryMode
{
    /// <summary>Records a try with the outcome <see cref="PublishOutcome.Suppressed"/>, and sends nothing. The default.</summary>
    Suppress,

    /// <summary>Sends as for a normal message.</summary>
    Deliver,

    /// <summary>Sends to <see cref="TestDeliveryOptions.RedirectTo"/> in place of the target of the message.</summary>
    Redirect,
}
