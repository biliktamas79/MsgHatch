using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

/// <summary>
/// The use cases of <see cref="ISubscriptionQueries"/> with the examples M1 to M9 of <c>docs/subscription-domain.md</c>,
/// on the in-memory store with the shared scenario and a directory substitute (section 4.17, D231). Each implementation
/// gets them with one subclass that only builds it.
/// </summary>
/// <typeparam name="T">The implementation.</typeparam>
public abstract class SubscriptionQueriesTestsBase<T> : IAsyncLifetime
    where T : class, ISubscriptionQueries
{
    private IManagementTestHost<T> _host = null!;

    protected FakeTimeProvider Time { get; } = TestHost.NewFakeTime();

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected T Queries => _host.Service(TenantKey.None);

    public async ValueTask InitializeAsync()
    {
        _host = await StartAsync(ManagerHost.Services(Time));
        await _host.Server.SeedScenarioAsync();
    }

    public async ValueTask DisposeAsync()
    {
        await _host.DisposeAsync();
        GC.SuppressFinalize(this);
    }

    [Fact]
    public async Task GetChannelsAsync_RegisteredChannels_GivesEachChannelWithItsFeatures_M1()
    {
        // Arrange
        var queries = Queries;

        // Act
        var channels = await queries.GetChannelsAsync(Ct);

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
    [MemberData(nameof(SubscriptionQueriesTestRows.MessageTypeRows), MemberType = typeof(SubscriptionQueriesTestRows))]
    public async Task GetMessageTypesAsync_Filters_GivesTheAppliedSubscriptionsWithTheirLevels(
        string example, string? type, string? company, string? user, string expected)
    {
        // Arrange
        var query = new MessageTypeQuery(
            type is null ? null : new MessageType(type), company is null ? null : new CompanyId(company), user is null ? null : new UserId(user));

        // Act
        var states = await Queries.GetMessageTypesAsync(query, Ct);

        // Assert
        Assert.Equal((example, expected), (example, Describe(states.Value)));
    }

    [Fact]
    public async Task GetMessageTypesAsync_UnknownUser_GivesNotFound_M6()
    {
        // Arrange
        var query = new MessageTypeQuery(User: new UserId("99"));

        // Act
        var states = await Queries.GetMessageTypesAsync(query, Ct);

        // Assert
        Assert.Equal(404, states.Error?.Type.Code);
    }

    [Fact]
    public async Task GetMessageTypesAsync_MessageTypeD_GivesItsRegisteredChannels()
    {
        // Arrange
        var query = new MessageTypeQuery(new MessageType("D"));

        // Act
        var states = await Queries.GetMessageTypesAsync(query, Ct);

        // Assert
        Assert.Equal(["Email", "SignalR", "Broker"], Assert.Single(states.Value).Channels.Select(static channel => channel.Name));
    }

    [Theory]
    [MemberData(nameof(SubscriptionQueriesTestRows.SubscriptionRows), MemberType = typeof(SubscriptionQueriesTestRows))]
    public async Task GetSubscriptionsAsync_Filters_GivesTheStoredSubscriptionsThatMatch(
        string example, string? type, string? company, SubscriptionLevel? level, string expected)
    {
        // Arrange
        var query = new SubscriptionQuery(type is null ? null : new MessageType(type), company is null ? null : new CompanyId(company), Level: level);

        // Act
        var subscriptions = await Queries.GetSubscriptionsAsync(query, Ct);

        // Assert
        Assert.Equal((example, expected), (example, Labels(subscriptions.Value)));
    }

    [Fact]
    public async Task GetSubscriptionAsync_StoredId_GivesTheSubscription()
    {
        // Arrange
        var id = Scenario.S6.Id;

        // Act
        var subscription = await Queries.GetSubscriptionAsync(id, Ct);

        // Assert
        Assert.Equal(Scenario.S6, subscription.Value);
    }

    [Fact]
    public async Task GetSubscriptionAsync_UnknownId_GivesNotFound()
    {
        // Arrange
        var id = Scenario.LabelId(50);

        // Act
        var subscription = await Queries.GetSubscriptionAsync(id, Ct);

        // Assert
        Assert.Equal(404, subscription.Error?.Type.Code);
    }

    [Fact]
    public async Task GetMessageTypesAndGetSubscriptionsAsync_NullQuery_ThrowArgumentNullException()
    {
        // Act
        var messageTypes = await Record.ExceptionAsync(async () => await Queries.GetMessageTypesAsync(null!, Ct));
        var subscriptions = await Record.ExceptionAsync(async () => await Queries.GetSubscriptionsAsync(null!, Ct));

        // Assert
        Assert.Equal((typeof(ArgumentNullException), typeof(ArgumentNullException)), (messageTypes?.GetType(), subscriptions?.GetType()));
    }

    /// <summary>Starts a host of the implementation on the services of a test.</summary>
    /// <param name="addServices">Adds the services of the host with the store, the directory, and the registry.</param>
    /// <returns>The started host.</returns>
    protected abstract ValueTask<IManagementTestHost<T>> StartAsync(Action<IServiceCollection> addServices);

    private static string Describe(IReadOnlyList<MessageTypeState> states) =>
        string.Join("; ", states.Select(static state =>
            $"{state.Type.Name}: {string.Join(" ", state.AppliedSubscriptions.Select(static s => $"{Scenario.Label(s.Id)} {s.Owner.Level}"))}"));

    private static string Labels(IReadOnlyList<Subscription> subscriptions) =>
        string.Join(", ", subscriptions.Select(static subscription => Scenario.Label(subscription.Id)).Order(StringComparer.Ordinal));
}
