namespace MsgHatch.Subscriptions.Resolution;

/// <summary>Why a target gets a message (D92).</summary>
public enum TargetSource
{
    /// <summary>A user of the scope of the event.</summary>
    ScopeUser,

    /// <summary>The group of a company scope, on a channel with group delivery.</summary>
    CompanyGroup,

    /// <summary>The company of a subscription for the company.</summary>
    Company,

    /// <summary>A named user of a subscription for named users.</summary>
    NamedUser,
}
