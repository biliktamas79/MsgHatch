using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Email;
using MsgHatch.Subscriptions.Api.Client.DependencyInjection;
using MsgHatch.Subscriptions.Api.Client.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using static MsgHatch.Subscriptions.Tests.Unit.Contracts.SubscriptionCommandsTestRows;

namespace MsgHatch.Subscriptions.Api.Client.Tests.Unit;

/// <summary>
/// A host that reads over HTTP and writes in process, and the reverse, on one subscription store that the server and the
/// client host share (D231): a write of one side gives the new revision to a read of the other side.
/// </summary>
public sealed class SubscriptionApiClientMixedHostsUnitTests
{
    private readonly InMemorySubscriptionStore _store = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task ReadsOverHttpAndWritesInProcess_CreateAndUpdateInProcess_EachReadOverHttpGivesTheNewRevision()
    {
        // Arrange
        var time = TestHost.NewFakeTime();
        await using var host = await HttpManagementHost.StartAsync<ISubscriptionQueries>(
            ServicesOnTheStore(time),
            (services, configureHttp) => HttpManagementHost.AddSameServices(services, ServicesOnTheStore(time))
                .UseSubscriptionQueriesApiClient(HttpManagementHost.BaseAddress, configureHttp));
        var commands = host.Client.GetRequiredService<ISubscriptionCommands>();
        var queries = host.Service(TenantKey.None);

        // Act
        var created = await commands.CreateAsync(Draft(User("2"), "C", EmailChannel.Id, new EmailChannelSettings("hu")), Ct);
        var firstRead = await queries.GetSubscriptionAsync(created.Value.Id, Ct);
        var updated = await commands.UpdateAsync(created.Value.Id, 1, Draft(User("2"), "C", EmailChannel.Id, new EmailChannelSettings("en")), Ct);
        var secondRead = await queries.GetSubscriptionAsync(created.Value.Id, Ct);

        // Assert
        Assert.Equal((created.Value, updated.Value, 2), (firstRead.Value, secondRead.Value, secondRead.Value.Revision));
    }

    [Fact]
    public async Task WritesOverHttpAndReadsInProcess_CreateOverHttp_ReadInProcessGivesTheSubscription()
    {
        // Arrange
        var time = TestHost.NewFakeTime();
        await using var host = await HttpManagementHost.StartAsync<ISubscriptionCommands>(
            ServicesOnTheStore(time),
            (services, configureHttp) => HttpManagementHost.AddSameServices(services, ServicesOnTheStore(time))
                .UseSubscriptionCommandsApiClient(HttpManagementHost.BaseAddress, configureHttp));
        var commands = host.Service(TenantKey.None);
        var queries = host.Client.GetRequiredService<ISubscriptionQueries>();

        // Act
        var created = await commands.CreateAsync(Draft(User("2"), "C", EmailChannel.Id, new EmailChannelSettings("hu")), Ct);
        var read = await queries.GetSubscriptionAsync(created.Value.Id, Ct);

        // Assert
        Assert.Equal(created.Value, read.Value);
    }

    private Action<IServiceCollection> ServicesOnTheStore(TimeProvider time) => ManagerHost.Services(time, store: _ => _store);
}
