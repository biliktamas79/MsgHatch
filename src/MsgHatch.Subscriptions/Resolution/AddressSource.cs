namespace MsgHatch.Subscriptions.Resolution;

/// <summary>Where the address of a delivery comes from (D92).</summary>
public enum AddressSource
{
    /// <summary>No address, because the profile has no address for the channel, for example for a SignalR target.</summary>
    None,

    /// <summary>The profile of the target user.</summary>
    UserProfile,

    /// <summary>The profile of the target company.</summary>
    CompanyProfile,
}
