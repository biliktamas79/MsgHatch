using BenchmarkDotNet.Attributes;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Email;
using MsgHatch.Subscriptions.Resolution;

namespace MsgHatch.Subscriptions.Tests.Performance;

/// <summary>
/// The resolution of one event with 1, 100, and 10,000 users of one company (section 4.11), on profiles that the
/// benchmark loaded before the measurement. The result itself allocates: one route and one trace for each user.
/// </summary>
[MemoryDiagnoser]
public class SubscriptionResolverPerfTests
{
    private readonly IReadOnlyList<ChannelDescriptor> _channels = [EmailChannel.Descriptor];
    private NotificationScope _scope;
    private UserProfile[] _users = [];

    [Params(1, 100, 10_000)]
    public int Users { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        _users = PerfSubscriptions.UsersOf(Users);
        _scope = NotificationScope.ForUsers((UserId[])[.. _users.Select(static user => user.Id)]);
    }

    [Benchmark]
    public SubscriptionResolution Resolve() =>
        SubscriptionResolver.Resolve(_channels, _scope, PerfSubscriptions.Subscriptions, _users, PerfSubscriptions.Companies, "en");
}
