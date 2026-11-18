using ExampleApp.Channels;
using ExampleApp.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using ExampleApp.Tests.Unit.TestTypes;
using MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Email;
using MsgHatch.Subscriptions;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Verification;
using static ExampleApp.Tests.Unit.TestTypes.ExampleHost;

namespace ExampleApp.Tests.Unit;

/// <summary>
/// The startup work of the example (D157): the scenario one time, and the events at each start. In the <c>Development</c>
/// environment also the routing verification and its log (D162).
/// </summary>
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
        var subscriptions = await scope.ServiceProvider.GetRequiredService<ISubscriptionQueries>().GetSubscriptionsAsync(new SubscriptionQuery(), Ct);
        var inbox = await provider.GetRequiredService<IInboxDispatchStore>().ClaimDueAsync(100, _time.UtcNow, TimeSpan.FromMinutes(1), Ct);
        Assert.Equal(
            (12, "A B C D E F C A B C D E F C"),
            (subscriptions.Value.Count, string.Join(' ', inbox.Value.Messages.Select(static message => message.Type.Name))));
    }

    [Fact]
    public async Task VerifyRoutingAsync_SubscriptionExampleOnSqlite_GivesNoFailedRowAndOnlyTheSkippedSubscription12OfUser2()
    {
        // Arrange
        var services = CreateServices(_database, _time);
        services.AddExampleMsgHatch();
        await using var provider = services.Build();
        await ExampleStartup.RunAsync(provider, Ct);

        // Act
        var report = await ExampleStartup.VerifyRoutingAsync(provider, Ct);

        // Assert
        var counts = Assert.Single(provider.GetFakeLogCollector().GetSnapshot(), static record => record.Id.Id == ExampleLoggerExtensions.RoutingVerifiedId);
        Assert.Equal((0, 24, 1, "24"), (report.Failed, report.Passed, report.Skipped, counts.Field("Passed")));
    }

    [Fact]
    public void LogReport_RowOfEachOutcome_LogsTheCountsAWarningForTheFailedRowAndTheSkippedRow_D162()
    {
        // Arrange
        var logger = new FakeLogger();
        var type = new MessageType("A");
        RoutingCheck[] checks =
        [
            new(type, "basic", SubscriptionOwner.Default, new DeliveryTarget(DeliveryTargetKind.User, "1"), EmailChannel.Id, "en", RoutingCheckOutcome.Passed, null, null, null),
            new(type, "basic", null, null, null, "hu", RoutingCheckOutcome.Failed, 404, "NotFound", null),
            new(type, null, SubscriptionOwner.ForCompany(new CompanyId("200")), null, EmailChannel.Id, null, RoutingCheckOutcome.Skipped, null, null, null),
        ];

        // Act
        ExampleStartup.LogReport(logger, new RoutingVerificationReport(checks));

        // Assert
        Assert.Equal(
            [
                "Information 9002: Routing verification: 1 passed, 1 failed, 1 skipped",
                "Warning 9003: Routing check failed: type A, test case basic, level , channel , language hu: 404 NotFound",
                "Information 9004: Routing check skipped: type A, test case (null), level Company, channel Email, language (null)",
            ],
            logger.Collector.GetSnapshot().Select(static record => $"{record.Level} {record.Id.Id}: {record.Message}"));
    }
}
