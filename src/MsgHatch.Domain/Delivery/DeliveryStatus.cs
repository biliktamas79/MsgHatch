namespace MsgHatch.Domain.Delivery;

/// <summary>The status of an inbox or outbox item. A new member comes only at the end (section 4.13).</summary>
public enum DeliveryStatus
{
    /// <summary>The item waits for its next try.</summary>
    Pending,

    /// <summary>The item is processed or sent.</summary>
    Done,

    /// <summary>The item failed, and gets no more tries.</summary>
    Failed,
}
