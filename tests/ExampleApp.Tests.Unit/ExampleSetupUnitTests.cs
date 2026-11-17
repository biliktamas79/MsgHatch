using System.Diagnostics;
using System.Globalization;
using ExampleApp.Channels;
using ExampleApp.Notices;
using ExampleApp.Recipients;
using ExampleApp.Startup;
using ExampleApp.Tests.Unit.TestTypes;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Data.Sqlite;
using MsgHatch.Data.Sqlite.DependencyInjection;
using MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.Subscriptions;
using MsgHatch.Subscriptions.Data.Sqlite.DependencyInjection;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Management;
using OpenTelemetry.Metrics;
using OpenTelemetry.Trace;
using static ExampleApp.Tests.Unit.TestTypes.ExampleHost;

namespace ExampleApp.Tests.Unit;

/// <summary>
/// The registration code of the example on its SQLite stores and its SQLite directory: the subscription example of
/// section 1.1, a change through the manager, two tenants (D114), two tenants on two files (D125, D174), and the telemetry (D131, D132). Its telemetry test
/// listens to the spans of MsgHatch, so the class is in the collection <c>Telemetry</c>.
/// </summary>
[Collection(SequentialTelemetry.Name)]
public sealed class ExampleSetupUnitTests : IDisposable
{
    private static readonly TenantId _t1 = new("t1");
    private static readonly TenantId _t2 = new("t2");
    private readonly Dictionary<string, string?> _database = TestDatabases.NewSection();
    private readonly WaitRecordingTimeProvider _time = new();

    public void Dispose() => _database.Delete();

    [Fact]
    public async Task AddExampleMsgHatch_EventsOfTheSubscriptionExample_StoresTheEffectiveResultOfSection11()
    {
        // Arrange
        await using var provider = BuildConsumer();
        await provider.SeedScenarioAsync(TenantKey.None);

        // Act
        await provider.ProcessAsync(new EventA([1, 2], null, "a"));
        await provider.ProcessAsync(new EventB([1, 2], null, "b"));
        await provider.ProcessAsync(new EventC([1, 2], null, "c"));
        await provider.ProcessAsync(new EventD([1, 2], null, "d"));
        await provider.ProcessAsync(new EventE([1, 2], null, "e"));
        await provider.ProcessAsync(new EventC(null, ExampleScenario.Acme, "group"));

        // Assert
        Assert.Equal(
            [
                "- SignalR c:100 CompanyDefaultLanguage C",
                "- SignalR u:1 UserUiLanguage C",
                "- SignalR u:2 UserUiLanguage C",
                "- email admin@acme.example html B értesítés: b",
                "- email client1@acme.example html Notification A: a",
                "- email client1@acme.example text Notification B: b",
                "- email client2@acme.example html A értesítés: a",
                "- email client2@acme.example html D értesítés: d",
                "- email client2@acme.example html Notification E: e",
            ],
            await provider.StoredAsync(_time.UtcNow));
    }

    [Fact]
    public async Task AddExampleMsgHatch_OptOutOfUser2ThroughTheManager_StopsTheNextEmailOfE()
    {
        // Arrange
        await using var provider = BuildConsumer();
        await provider.SeedScenarioAsync(TenantKey.None);
        var before = await provider.ProcessAsync(new EventE([2], null, "e"));
        await using var scope = provider.CreateAsyncScope();
        var optOut = new SubscriptionDraft(
            SubscriptionOwner.ForUser(UserId.From(2)), new MessageType("E"), SubscriptionKind.OptOut, Channel: null, Settings: null);
        Assert.False((await ExampleProcess.ManagerOf(scope.ServiceProvider, TenantKey.None).CreateAsync(optOut, Ct)).IsError);

        // Act
        var after = await provider.ProcessAsync(new EventE([2], null, "e"));

        // Assert
        Assert.Equal((1, 0), (before.Value.Stored, after.Value.Stored));
    }

    [Fact]
    public async Task AddExampleMsgHatch_User1InTwoTenantsOnOneFile_SendsEachEventOnlyToTheUser1OfItsTenant()
    {
        // Arrange
        await using var provider = BuildConsumer();
        await SeedUser1Async(provider, _t1, "one@t1.example");
        await SeedUser1Async(provider, _t2, "one@t2.example");

        // Act
        await provider.ProcessAsync(new EventA([1], null, "a"), _t1);
        await provider.ProcessAsync(new EventA([1], null, "a"), _t2);

        // Assert
        Assert.Equal(
            ["t1 email one@t1.example html Notification A: a", "t2 email one@t2.example html Notification A: a"],
            await provider.StoredAsync(_time.UtcNow));
    }

    [Fact]
    public async Task AddExampleMessages_TwoTenantsOnTwoSqliteFiles_SendsTheEventOfEachTenantAndKeepsItsMessagesInItsFile()
    {
        // Arrange
        using var folder = new TenantDatabaseFolder();
        await using var provider = BuildForEachTenant(folder);
        await SeedUser1Async(provider, _t1, "one@t1.example");
        await SeedUser1Async(provider, _t2, "one@t2.example");
        await provider.AcceptAsync(new EventA([1], null, "a"), _t1);
        await provider.AcceptAsync(new EventA([1], null, "a"), _t2);
        await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);

        // Act
        var sent = await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Equal(2, sent.Value.Succeeded);
        Assert.Equal(
            [["inbox t1 Done", "outbox t1 Done"], ["inbox t2 Done", "outbox t2 Done"], []],
            [MessagesIn(folder, "msghatch-t1.db"), MessagesIn(folder, "msghatch-t2.db"), MessagesIn(folder, "msghatch.db")]);
    }

    [Fact]
    public async Task AddExampleTelemetry_OneAcceptedEvent_GivesAProcessSpanAndASendSpanInANewTraceWithALinkToIt()
    {
        // Arrange
        List<Activity> spans = [];
        var services = CreateServices(_database, _time);
        services.AddExampleMsgHatch().UseEmailClient<LogEmailClient>();
        services.AddExampleTelemetry().WithTracing(tracing => tracing.AddInMemoryExporter(spans));
        await using var provider = services.Build();
        _ = provider.GetRequiredService<TracerProvider>();
        await provider.SeedScenarioAsync(TenantKey.None);
        var messageId = (await provider.AcceptAsync(new EventA([1], null, "a"))).Value;

        // Act
        await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var process = Assert.Single(spans, span => Equals(span.GetTagItem("messaging.message.id"), messageId.ToString()));
        var send = Assert.Single(spans, span => span.DisplayName == "send Email" && span.Links.Any(link => link.Context.SpanId == process.SpanId));
        Assert.Equal(("process A", ActivityKind.Client, false), (process.DisplayName, send.Kind, send.TraceId == process.TraceId));
    }

    [Fact]
    public async Task AddExampleTelemetry_OneSentEvent_GivesTheDurationAndDelayHistogramsTheirBucketBoundaries()
    {
        // Arrange
        List<Metric> metrics = [];
        var services = CreateServices(_database, _time);
        services.AddExampleMsgHatch().UseEmailClient<LogEmailClient>();
        services.AddExampleTelemetry().WithMetrics(meters => meters.AddInMemoryExporter(metrics));
        await using var provider = services.Build();
        var meterProvider = provider.GetRequiredService<MeterProvider>();
        await provider.SeedScenarioAsync(TenantKey.None);
        await provider.AcceptAsync(new EventA([1], null, "a"));

        // Act
        await provider.GetRequiredService<IInboxProcessor>().ProcessPendingAsync(10, Ct);
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);
        meterProvider.ForceFlush();

        // Assert
        Assert.Equal(
            [
                "msghatch.process.duration: 0.005 0.01 0.025 0.05 0.1 0.25 0.5 1 2.5 5 10",
                "msghatch.send.duration: 0.005 0.01 0.025 0.05 0.1 0.25 0.5 1 2.5 5 10",
                "msghatch.delivery.delay: 0.1 0.5 1 5 15 60 300 900 3600 21600 86400",
            ],
            [BoundsOf(metrics, "msghatch.process.duration"), BoundsOf(metrics, "msghatch.send.duration"), BoundsOf(metrics, "msghatch.delivery.delay")]);
    }

    private ServiceProvider BuildConsumer()
    {
        var services = CreateServices(_database, _time);
        services.AddExampleMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer);
        return services.Build();
    }

    /// <summary>
    /// A host of the example messages with one SQLite file for each of the tenants t1 and t2, and the system file for
    /// the directory, which keeps the tenant of each row (D174).
    /// </summary>
    private ServiceProvider BuildForEachTenant(TenantDatabaseFolder folder)
    {
        var services = CreateServices(folder.Section, _time);
        services.AddSingleton(new FixedTenants([_t1, _t2]));
        services.Configure<SqliteStoreOptions>(o => o.ConnectionString = folder.ConnectionStringOf("msghatch.db"));
        services.AddMsgHatch()
            .UseSubscriptions()
            .UseRecipientDirectory<ExampleRecipientDirectory>()
            .UseSqliteTenantStores<FixedTenantSource>()
            .UseSqliteTenantSubscriptionStore()
            .UseFileTemplates(ExampleSetup.TemplateFolder)
            .AddExampleMessages()
            .UseEmailClient<LogEmailClient>();
        return services.Build();
    }

    private static string?[] MessagesIn(TenantDatabaseFolder folder, string fileName) =>
    [
        .. TestDatabases.Rows(
                folder.ConnectionStringOf(fileName),
                "SELECT 'inbox ' || TenantId || ' ' || Status FROM MsgHatchInbox UNION ALL SELECT 'outbox ' || TenantId || ' ' || Status FROM MsgHatchOutbox")
            .Select(static row => row[0]),
    ];

    private static async Task SeedUser1Async(IServiceProvider provider, TenantId tenant, string email)
    {
        var key = TenantKey.For(tenant);
        await using var scope = provider.CreateAsyncScope();
        Assert.Null(await ExampleProcess.DirectoryOf(scope.ServiceProvider, key).SaveUserAsync(new ExampleUser(1, CompanyId: null, "en", email), Ct));
        Assert.False((await ExampleProcess.ManagerOf(scope.ServiceProvider, key).CreateAsync(ExampleScenario.Subscriptions[0], Ct)).IsError);
    }

    private static string BoundsOf(List<Metric> metrics, string name)
    {
        var metric = metrics.Last(metric => metric.Name == name);
        List<string> bounds = [];
        foreach (ref readonly var point in metric.GetMetricPoints())
        {
            foreach (var bucket in point.GetHistogramBuckets())
            {
                if (!double.IsPositiveInfinity(bucket.ExplicitBound))
                {
                    bounds.Add(bucket.ExplicitBound.ToString(CultureInfo.InvariantCulture));
                }
            }
        }

        return $"{name}: {string.Join(' ', bounds.Distinct())}";
    }
}
