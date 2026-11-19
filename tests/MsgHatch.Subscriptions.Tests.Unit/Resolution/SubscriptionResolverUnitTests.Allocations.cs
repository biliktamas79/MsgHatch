using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Email;
using MsgHatch.Subscriptions.Resolution;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit.Resolution;

/// <summary>
/// The allocations of the resolution (section 4.11, D282). The resolution is synchronous and has no dependency, so a
/// unit test measures its bytes on the thread of the call.
/// </summary>
public sealed partial class SubscriptionResolverUnitTests
{
    private const long _maxBytesForEachFurtherUser = 500;

    [Fact]
    public void Resolve_HundredUsersOfAnOptOutOfTheirCompany_AllocatesAtMost500BytesForEachFurtherUser()
    {
        // Arrange
        var acme = new CompanyId("100");
        IReadOnlyList<ChannelDescriptor> channels = [EmailChannel.Descriptor];
        IReadOnlyList<Subscription> subscriptions =
        [
            ASubscription(SubscriptionOwner.Default, SubscriptionKind.Channel, new EmailChannelSettings()),
            ASubscription(SubscriptionOwner.ForCompany(acme), SubscriptionKind.OptOut, settings: null),
        ];
        IReadOnlyList<CompanyProfile> companies = [new(acme, "hu", new Dictionary<ChannelId, string>())];
        UserProfile[] users =
        [
            .. Enumerable.Range(1, 100).Select(user => new UserProfile(
                new UserId($"{user}"), acme, "en", new Dictionary<ChannelId, string> { [EmailChannel.Id] = $"client{user}@example.com" })),
        ];
        var oneUser = NotificationScope.ForUsers((UserId[])[users[0].Id]);
        var hundredUsers = NotificationScope.ForUsers((UserId[])[.. users.Select(static user => user.Id)]);
        var resolution = SubscriptionResolver.Resolve(channels, hundredUsers, subscriptions, users, companies, _defaultLanguage); // the first call warms up

        // Act
        var forOneUser = BytesOf(() => SubscriptionResolver.Resolve(channels, oneUser, subscriptions, users, companies, _defaultLanguage));
        var forHundredUsers = BytesOf(() => SubscriptionResolver.Resolve(channels, hundredUsers, subscriptions, users, companies, _defaultLanguage));

        // Assert
        Assert.Equal(
            (0, 100, true),
            (resolution.Deliveries.Count, resolution.OptOuts.Count, (forHundredUsers - forOneUser) / 99 <= _maxBytesForEachFurtherUser));
    }

    private static long BytesOf(Func<SubscriptionResolution> resolve)
    {
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = resolve();
        return GC.GetAllocatedBytesForCurrentThread() - before;
    }

    private static Subscription ASubscription(SubscriptionOwner owner, SubscriptionKind kind, EmailChannelSettings? settings) =>
        new(
            Guid.NewGuid(), 1, owner, new MessageType("A"), kind, kind == SubscriptionKind.Channel ? EmailChannel.Id : null, settings,
            SubscriptionRecipients.ScopeUsers, Mandatory: false, TestSubscriptions.CreatedAtUtc, TestSubscriptions.CreatedAtUtc);
}
