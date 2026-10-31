namespace MsgHatch.Domain.Routing;

/// <summary>The kind of a delivery target.</summary>
public enum DeliveryTargetKind
{
    /// <summary>A user. The target id is the text of a <see cref="Identity.UserId"/>.</summary>
    User,

    /// <summary>A company. The target id is the text of a <see cref="Identity.CompanyId"/>.</summary>
    Company,
}
