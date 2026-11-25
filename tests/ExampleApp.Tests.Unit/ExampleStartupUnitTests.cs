using ExampleApp.Channels;
using ExampleApp.Startup;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;
using ExampleApp.Tests.Unit.TestTypes;
using MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Channels.Email;
using MsgHatch.Channels.SignalR;
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
    [Fact]
    public async Task SeedChannelHistoryAsync_NewFile_WritesTheRowsOfEmailAndSignalRWithTheirTimesAndNoRowOfBroker_D288()
    {
        // Arrange
        await using var provider = AProvider();

        // Act
        await ExampleStartup.SeedChannelHistoryAsync(provider, Ct);

        // Assert
        Assert.Equal(
            [
                "Email 1 Down Errors -40.00:00:00 Unavailable", "Email 2 Up -39.22:00:00",
                "Email 3 Down Errors -12.00:00:00 Unavailable", "Email 4 Up -11.23:15:00",
                "Email 5 Down Operator -5.00:00:00 Example maintenance", "Email 6 Up -4.21:00:00",
                "Email 7 Down Errors -1.02:00:00 Unavailable", "Email 8 Up -1.01:53:00",
                "SignalR 1 Down Errors -20.00:00:00 Unavailable", "SignalR 2 Up -19.18:00:00",
            ],
            await RowsAsync(provider));
    }

    [Fact]
    public async Task SeedChannelHistoryAsync_NewFile_EachChannelIsUpAndTheHistoryOfEmailHasTheRowBeforeTheWindow_D288()
    {
        // Arrange
        await using var provider = AProvider();

        // Act
        await ExampleStartup.SeedChannelHistoryAsync(provider, Ct);

        // Assert
        await using var scope = provider.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredKeyedService<IRegistryQueries>(TenantKey.None);
        var channels = await queries.GetChannelsAsync(Ct);
        var history = await queries.GetChannelHistoryAsync(EmailChannel.Id, Ct);
        Assert.Equal(
            ("Broker:none Email:Up SignalR:Up", 0, 7, "Up -39.22:00:00"),
            (string.Join(' ', channels.Value.OrderBy(static channel => channel.Id.Name, StringComparer.Ordinal)
                    .Select(static channel => $"{channel.Id.Name}:{channel.Current?.Kind.ToString() ?? "none"}")),
                (await Store(provider).GetDownChannelsAsync(Ct)).Value.Count, history.Value.Count,
                $"{history.Value[^1].Kind} {history.Value[^1].SinceUtc - _time.UtcNow:c}"));
    }

    [Fact]
    public async Task SeedChannelHistoryAsync_SecondSeedAndAChannelWithAStateRow_AddNoRow_D288()
    {
        // Arrange
        await using var provider = AProvider();
        await Store(provider).SetChannelDownAsync(
            new ChannelDown(SignalRChannel.Id, ChannelDownReason.Operator, _time.UtcNow.AddMinutes(-1), NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, "Real pause"),
            Ct);
        await ExampleStartup.SeedChannelHistoryAsync(provider, Ct);

        // Act
        await ExampleStartup.SeedChannelHistoryAsync(provider, Ct);

        // Assert
        var rows = await RowsAsync(provider);
        Assert.Equal(
            (8, "SignalR 1 Down Operator -00:01:00 Real pause"),
            (rows.Count(static row => row.StartsWith("Email ", StringComparison.Ordinal)), string.Join('|', rows.Where(static row => row.StartsWith("SignalR ", StringComparison.Ordinal)))));
    }

    private ServiceProvider AProvider()
    {
        var services = CreateServices(_database, _time);
        services.AddExampleMsgHatch();
        return services.Build();
    }

    private static IOutboxDispatchStore Store(IServiceProvider provider) => provider.GetRequiredService<IOutboxDispatchStore>();

    /// <summary>The state rows of each channel as text, by channel and version: the kind, the reason, the start before now, and the error name or the note.</summary>
    private async Task<List<string>> RowsAsync(IServiceProvider provider)
    {
        var rows = await Store(provider).GetChannelStatesAsync(DateTime.MinValue, Ct);
        return [.. rows.Value.OrderBy(static row => row.Channel.Name, StringComparer.Ordinal).ThenBy(static row => row.Version)
            .Select(row => $"{row.Channel.Name} {row.Version} {row.Kind}{(row.Reason is { } reason ? $" {reason}" : "")} {row.SinceUtc - _time.UtcNow:c}"
                + ((row.ErrorName ?? row.Note) is { } detail ? $" {detail}" : ""))];
    }
}
