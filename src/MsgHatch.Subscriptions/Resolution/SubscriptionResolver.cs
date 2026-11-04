using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.Resolution;

/// <summary>
/// The resolution of step 4 of the routing procedure of the subscription router (section 4.17, D146). It merges the
/// subscriptions of the three owner levels for each channel (D72), applies the mandatory subscriptions (D88) and the
/// fixed subscriptions for the company and for named users (D89, D90), and keeps one effective subscription for each
/// delivery target and channel (D91). It does no I/O, so the subscription manager and a consumer can use it to preview
/// the applied subscriptions.
/// </summary>
public static class SubscriptionResolver
{
    /// <summary>Resolves the subscriptions of one event.</summary>
    /// <param name="channels">The registered channels of the message type, with their features.</param>
    /// <param name="scope">The scope of the event. A duplicate user gives one delivery.</param>
    /// <param name="subscriptions">
    /// The subscriptions of the message type: the defaults, those of the companies, and those of the users. A
    /// subscription of a company or a user that the event does not concern is ignored.
    /// </param>
    /// <param name="users">
    /// The loaded user profiles: those of the scope, or of the company of a company scope, and those of the named
    /// users. An unknown user gives no delivery.
    /// </param>
    /// <param name="companies">The loaded company profiles, for the language and the address of a company target.</param>
    /// <param name="defaultLanguage">The language when no other rule gives one, <c>MsgHatchOptions.DefaultLanguage</c>.</param>
    /// <returns>One route for each delivery target and channel with its <see cref="ResolutionTrace"/>, and the opt-out decisions.</returns>
    public static SubscriptionResolution Resolve(
        IReadOnlyList<ChannelDescriptor> channels,
        NotificationScope scope,
        IReadOnlyList<Subscription> subscriptions,
        IReadOnlyList<UserProfile> users,
        IReadOnlyList<CompanyProfile> companies,
        string defaultLanguage)
    {
        ArgumentNullException.ThrowIfNull(channels);
        ArgumentNullException.ThrowIfNull(subscriptions);
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(companies);
        ArgumentNullException.ThrowIfNull(defaultLanguage);
        return new ResolutionRun(channels, subscriptions, users, companies, defaultLanguage).Run(scope);
    }
}
