using Microsoft.Extensions.DependencyInjection;
using ExampleApp.Tests.Unit.TestTypes;
using MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions;
using MsgHatch.Subscriptions.Management;
using static ExampleApp.Tests.Unit.TestTypes.ExampleHost;

namespace ExampleApp.Tests.Unit;

/// <summary>The startup work of the example (D157): the scenario one time, and the events at each start.</summary>
public sealed class ExampleStartupUnitTests : IDisposable
{
    private readonly Dictionary<string, string?> _database = TestDatabases.NewSection();
    private readonly WaitRecordingTimeProvider _time = new();

    public void Dispose() => _database.Delete();

    [Fact]
    public async Task RunAsync_TwoStartsOnOneFile_WritesTheScenarioOneTimeAndAcceptsTheEventsAtEachStart()
    {
        // Arrange
        var services = CreateServices(_database, _time);
        services.AddExampleMsgHatch();
        await using var provider = services.Build();

        // Act
        await ExampleStartup.RunAsync(provider, Ct);
        await ExampleStartup.RunAsync(provider, Ct);

        // Assert
        await using var scope = provider.CreateAsyncScope();
        var subscriptions = await scope.ServiceProvider.GetRequiredService<ISubscriptionManager>().GetSubscriptionsAsync(new SubscriptionQuery(), Ct);
        var inbox = await provider.GetRequiredService<IInboxDispatchStore>().ClaimDueAsync(100, _time.UtcNow, TimeSpan.FromMinutes(1), Ct);
        Assert.Equal(
            (12, "A B C D E F C A B C D E F C"),
            (subscriptions.Value.Count, string.Join(' ', inbox.Value.Messages.Select(static message => message.Type.Name))));
    }
}
