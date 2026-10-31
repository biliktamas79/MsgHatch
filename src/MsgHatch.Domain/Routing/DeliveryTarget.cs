namespace MsgHatch.Domain.Routing;

/// <summary>The receiver of one message: a user or a company.</summary>
/// <param name="Kind">The kind of the target.</param>
/// <param name="Id">The text of the <see cref="Identity.UserId"/> or of the <see cref="Identity.CompanyId"/>.</param>
public readonly record struct DeliveryTarget(DeliveryTargetKind Kind, string Id);
