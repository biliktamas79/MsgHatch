using MsgHatch.Domain.Delivery;

namespace MsgHatch.Domain.Management;

/// <summary>
/// The place of one message in the order of a page: newest first, so <c>CreatedAtUtc</c> descending, then <c>Id</c>
/// descending (D112). The next page starts after the message of the cursor.
/// </summary>
/// <param name="CreatedAtUtc">The creation time of the message.</param>
/// <param name="Id">The id of the message.</param>
public readonly record struct MessageCursor(DateTime CreatedAtUtc, Guid Id)
{
    /// <summary>Gives the cursor of a message.</summary>
    /// <param name="delivery">The delivery state of the message.</param>
    /// <returns>The cursor.</returns>
    public static MessageCursor Of(DeliveryState delivery) => new(delivery.CreatedAtUtc, delivery.Id);

    /// <summary>
    /// Tells whether a message comes after this cursor in the order of a page: it is older, or it has the same time and
    /// a smaller id. Ids compare as <see cref="Guid.CompareTo(Guid)"/>, which is the order of their text in the format <c>D</c>.
    /// </summary>
    /// <param name="delivery">The delivery state of the message.</param>
    /// <returns>True when the message comes after the cursor.</returns>
    public bool Precedes(DeliveryState delivery) =>
        delivery.CreatedAtUtc < CreatedAtUtc || (delivery.CreatedAtUtc == CreatedAtUtc && delivery.Id.CompareTo(Id) < 0);
}
