using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Resolution;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit.Management;

/// <summary>
/// The subscription management API with the examples M1 to M22 of <c>docs/subscription-domain.md</c>, on the in-memory
/// store with the shared scenario and a directory substitute (section 4.17).
/// </summary>
public sealed partial class SubscriptionManagerUnitTests : IAsyncLifetime
{
    private readonly FakeTimeProvider _time = TestHost.NewFakeTime();
    private ServiceProvider _provider = null!;
    private AsyncServiceScope _scope;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private ISubscriptionManager Manager => _scope.ServiceProvider.GetRequiredService<ISubscriptionManager>();

    private ISubscriptionStore Store => _provider.GetRequiredService<ISubscriptionStore>();

    public static TheoryData<string, string?, string?, string?, string> MessageTypeRows => new()
    {
        { "M2: no filter", null, null, null, "A: #1 Default; B: #2 Default; C: #3 Default; D: ; E: " },
        { "M3: user 1", null, null, "1", "A: #1 Default; B: #6 User; C: #3 Default; D: ; E: #7 User" },
        { "M4: company Acme", null, "100", null, "A: #1 Default; B: #4 Company #10 Company; C: #3 Default; D: #11 Company; E: #5 Company" },
        { "M5: B and user 2", "B", null, "2", "B: #4 Company" },
        { "M19: D and user 2", "D", null, "2", "D: #11 Company" },
    };

    public static TheoryData<string, string?, string?, SubscriptionLevel?, string> SubscriptionRows => new()
    {
        { "M7: company Acme", null, "100", null, "#10, #11, #4, #5" },
        { "M8: message type B", "B", null, null, "#10, #2, #4, #6" },
        { "M9: level User", null, null, SubscriptionLevel.User, "#12, #6, #7" },
    };

    public async ValueTask InitializeAsync()
    {
        _provider = ManagerHost.Build(_time);
        _scope = _provider.CreateAsyncScope();
        await _provider.SeedScenarioAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
    }

    [Fact]
    public async Task GetChannelsAsync_RegisteredChannels_GivesEachChannelWithItsFeatures_M1()
    {
        // Arrange
        var manager = Manager;

        // Act
        var channels = await manager.GetChannelsAsync(Ct);

        // Assert
        Assert.Equal(
            [
                ("Email", ChannelFeatures.Languages | ChannelFeatures.RichText),
                ("SignalR", ChannelFeatures.Languages | ChannelFeatures.RawData | ChannelFeatures.GroupDelivery),
                ("Broker", ChannelFeatures.RawData | ChannelFeatures.GroupDelivery),
            ],
            channels.Value.Select(static channel => (channel.Id.Name, channel.Features)));
    }

    [Theory]
    [MemberData(nameof(MessageTypeRows))]
    public async Task GetMessageTypesAsync_Filters_GivesTheAppliedSubscriptionsWithTheirLevels(
        string example, string? type, string? company, string? user, string expected)
    {
        // Arrange
        var query = new MessageTypeQuery(
            type is null ? null : new MessageType(type), company is null ? null : new CompanyId(company), user is null ? null : new UserId(user));

        // Act
        var states = await Manager.GetMessageTypesAsync(query, Ct);

        // Assert
        Assert.Equal((example, expected), (example, Describe(states.Value)));
    }

    [Fact]
    public async Task GetMessageTypesAsync_UnknownUser_GivesNotFound_M6()
    {
        // Arrange
        var query = new MessageTypeQuery(User: new UserId("99"));

        // Act
        var states = await Manager.GetMessageTypesAsync(query, Ct);

        // Assert
        Assert.Equal(404, states.Error?.Type.Code);
    }

    [Fact]
    public async Task GetMessageTypesAsync_MessageTypeD_GivesItsRegisteredChannels()
    {
        // Arrange
        var query = new MessageTypeQuery(new MessageType("D"));

        // Act
        var states = await Manager.GetMessageTypesAsync(query, Ct);

        // Assert
        Assert.Equal(["Email", "SignalR", "Broker"], Assert.Single(states.Value).Channels.Select(static channel => channel.Name));
    }

    [Theory]
    [MemberData(nameof(SubscriptionRows))]
    public async Task GetSubscriptionsAsync_Filters_GivesTheStoredSubscriptionsThatMatch(
        string example, string? type, string? company, SubscriptionLevel? level, string expected)
    {
        // Arrange
        var query = new SubscriptionQuery(type is null ? null : new MessageType(type), company is null ? null : new CompanyId(company), Level: level);

        // Act
        var subscriptions = await Manager.GetSubscriptionsAsync(query, Ct);

        // Assert
        Assert.Equal((example, expected), (example, Labels(subscriptions.Value)));
    }

    [Fact]
    public async Task GetSubscriptionAsync_StoredId_GivesTheSubscription()
    {
        // Arrange
        var id = Scenario.S6.Id;

        // Act
        var subscription = await Manager.GetSubscriptionAsync(id, Ct);

        // Assert
        Assert.Equal(Scenario.S6, subscription.Value);
    }

    [Fact]
    public async Task GetSubscriptionAsync_UnknownId_GivesNotFound()
    {
        // Arrange
        var id = Scenario.LabelId(50);

        // Act
        var subscription = await Manager.GetSubscriptionAsync(id, Ct);

        // Assert
        Assert.Equal(404, subscription.Error?.Type.Code);
    }

    private static string Describe(IReadOnlyList<MessageTypeState> states) =>
        string.Join("; ", states.Select(static state =>
            $"{state.Type.Name}: {string.Join(" ", state.AppliedSubscriptions.Select(static s => $"{Scenario.Label(s.Id)} {s.Owner.Level}"))}"));

    private static string Labels(IReadOnlyList<Subscription> subscriptions) =>
        string.Join(", ", subscriptions.Select(static subscription => Scenario.Label(subscription.Id)).Order(StringComparer.Ordinal));

    private async Task<string> ResolveAsync(string type, string scope)
    {
        var subscriptions = await Store.QueryAsync(new SubscriptionQuery(new MessageType(type)), Ct);
        var resolution = SubscriptionResolver.Resolve(
            Scenario.ChannelsOf(type), Scenario.ScopeOf(scope), subscriptions.Value, Scenario.Users, Scenario.Companies, "en");
        return string.Join("; ", resolution.Deliveries.Select(Scenario.Describe).Order(StringComparer.Ordinal));
    }

    private async Task<int> StoredCountAsync() => (await Store.QueryAsync(new SubscriptionQuery(), Ct)).Value.Count;
}
