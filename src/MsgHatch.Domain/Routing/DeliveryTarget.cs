namespace MsgHatch.Domain.Routing;

/// <summary>The receiver of one message: a user, a company, or all users of the tenant of the message.</summary>
/// <param name="Kind">The kind of the target.</param>
/// <param name="Id">
/// The text of the <see cref="Identity.UserId"/> or of the <see cref="Identity.CompanyId"/>, or empty for a tenant target.
/// </param>
public readonly record struct DeliveryTarget(DeliveryTargetKind Kind, string Id);
