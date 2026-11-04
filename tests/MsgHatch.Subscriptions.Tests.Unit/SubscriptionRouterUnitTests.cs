using System.Collections.Concurrent;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Subscriptions.Resolution;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Subscriptions.Tests.Unit;

/// <summary>
/// The subscription router through <see cref="INotificationService.ProcessAsync{TEvent}"/>, with substitutes for the
/// subscription store and the recipient directory of each tenant key, and the in-memory outbox (section 4.17).
/// </summary>
public sealed partial class SubscriptionRouterUnitTests
{
    private static readonly TenantId _t1 = new("t1");
    private static readonly TenantId _t2 = new("t2");

    private readonly ConcurrentDictionary<TenantKey, ISubscriptionStore> _stores = new();
    private readonly ConcurrentDictionary<TenantKey, IRecipientDirectory> _directories = new();

    public static TheoryData<string, string[]> CompanyScopeRows => new()
    {
        { "C", ["GetCompanyAsync(c:100)", "GetCompanyUsersAsync(c:100)"] },
        { "G", ["GetCompanyAsync(c:100)"] },
    };

    public static TheoryData<string, string[]> FixedProfileRows => new()
    {
        { "B", ["GetUsersAsync(u:1)", "GetCompanyAsync(c:100)"] },
        { "A", ["GetUsersAsync(u:1)"] },
        { "D", ["GetUsersAsync(u:1)", "GetUsersAsync(u:2)"] },
    };

    public static TheoryData<string> FailingServices => ["store", "directory"];

    [Fact]
    public async Task ProcessAsync_UserScopeWithNoNamedUsers_CallsGetUsersAsyncOneTimeWithTheUsers()
    {
        // Arrange
        await using var provider = BuildScenarioHost();

        // Act
        await provider.ProcessAsync(new EventA(["1", "2"]), _t1);

        // Assert
        Assert.Equal(["GetUsersAsync(u:1,2)"], ScenarioDoubles.Calls(_directories[TenantKey.For(_t1)]));
    }

    [Fact]
    public async Task ProcessAsync_UserScopeWithNoNamedUsers_CallsFindForDeliveryAsyncOneTimeWithTheCompaniesAndTheUsers()
    {
        // Arrange
        await using var provider = BuildScenarioHost();

        // Act
        await provider.ProcessAsync(new EventA(["1", "2"]), _t1);

        // Assert
        Assert.Equal(["FindForDeliveryAsync(A; c:100; u:1,2)"], ScenarioDoubles.Calls(_stores[TenantKey.For(_t1)]));
    }

    [Fact]
    public async Task ProcessAsync_EventOfATenant_UsesOnlyTheStoreAndTheDirectoryOfItsKey()
    {
        // Arrange
        await using var provider = BuildScenarioHost();

        // Act
        await provider.ProcessAsync(new EventA(["1", "2"]), _t1);

        // Assert
        Assert.Equal(("t1", "t1"), (Keys(_stores), Keys(_directories)));
    }

    [Fact]
    public async Task ProcessAsync_EventOfTenantT2WithItsOwnUser1_GivesNoMessage_R19()
    {
        // Arrange
        await using var provider = BuildScenarioHost(
            key => key == TenantKey.For(_t2) ? ScenarioDoubles.AStore([]) : ScenarioDoubles.AScenarioStore(),
            key => key == TenantKey.For(_t2)
                ? ScenarioDoubles.ADirectory([Scenario.Users[0] with { Company = null, UiLanguage = "de" }], [])
                : ScenarioDoubles.AScenarioDirectory());

        // Act
        var result = await provider.ProcessAsync(new EventA(["1"]), _t2);

        // Assert
        Assert.Equal((0, 0, "t2"), (result.Value.Stored, provider.GetRequiredService<InMemoryOutboxStore>().GetMessages().Count, Keys(_stores)));
    }

    [Theory]
    [MemberData(nameof(CompanyScopeRows))]
    public async Task ProcessAsync_CompanyScope_CallsGetCompanyUsersAsyncOnlyForAChannelWithNoGroupDelivery(string type, string[] expected)
    {
        // Arrange
        await using var provider = BuildScenarioHost();

        // Act
        await ProcessScenarioEventAsync(provider, type, users: null, company: "100");

        // Assert
        Assert.Equal((type, string.Join(" ", expected)), (type, string.Join(" ", ScenarioDoubles.Calls(_directories[TenantKey.None]))));
    }

    [Theory]
    [MemberData(nameof(FixedProfileRows))]
    public async Task ProcessAsync_UserScope_LoadsOnlyTheProfilesOfTheFixedSubscriptionsThatApply(string type, string[] expected)
    {
        // Arrange
        await using var provider = BuildScenarioHost();

        // Act
        await ProcessScenarioEventAsync(provider, type, users: ["1"], company: null);

        // Assert
        Assert.Equal((type, string.Join(" ", expected)), (type, string.Join(" ", ScenarioDoubles.Calls(_directories[TenantKey.None]))));
    }

    [Theory]
    [MemberData(nameof(FailingServices))]
    public async Task ProcessAsync_ErrorOfTheStoreOrTheDirectory_FailsTheEventWithThatError(string failing)
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        await using var provider = BuildScenarioHost(
            _ => failing == "store" ? AFailingStore(error) : ScenarioDoubles.AScenarioStore(),
            _ => failing == "directory" ? AFailingDirectory(error) : ScenarioDoubles.AScenarioDirectory());

        // Act
        var result = await provider.ProcessAsync(new EventA(["1"]));

        // Assert
        Assert.Equal((failing, true), (failing, ReferenceEquals(error, result.Error)));
    }

    [Fact]
    public async Task ProcessAsync_OptOutThatApplies_LogsADebugRecordWithTheOptOut()
    {
        // Arrange
        await using var provider = BuildScenarioHost();

        // Act
        await provider.ProcessAsync(new EventB(["2"]));

        // Assert
        var record = Assert.Single(provider.LogRecords(), static record => record.Id.Id == 1118);
        Assert.Equal((LogLevel.Debug, Scenario.S4.Id.ToString()), (record.Level, record.GetStructuredStateValue("SubscriptionId")));
    }

    [Fact]
    public async Task ProcessAsync_ProfilesWithEmailAddresses_NoLogRecordContainsAnAddress()
    {
        // Arrange
        await using var provider = BuildScenarioHost();

        // Act
        await provider.ProcessAsync(new EventB(["1", "2"]));

        // Assert
        Assert.DoesNotContain(provider.LogRecords(), static record => record.ContainsText("@acme.example"));
    }

    [Fact]
    public async Task ProcessAsync_StoredMessage_ItsResolutionTraceRoundTripsThroughMsgHatchJson()
    {
        // Arrange
        await using var provider = BuildScenarioHost();
        await provider.ProcessAsync(new EventB(["1"]));
        var json = provider.GetRequiredService<MsgHatchJson>();
        var stored = provider.GetRequiredService<InMemoryOutboxStore>().GetMessages().Single(static message => message.Target.Id == "1");

        // Act
        var readBack = json.DeserializeOutgoing(json.Serialize(stored).Value);

        // Assert
        Assert.Equal(TraceText((ResolutionTrace)stored.Trace!), TraceText((ResolutionTrace)readBack.Value.Trace!));
    }

    [Fact]
    public async Task ProcessAsync_StoredMessage_WritesTheTraceWithItsDiscriminatorAndTheOwnerWithItsIds()
    {
        // Arrange
        await using var provider = BuildScenarioHost();
        await provider.ProcessAsync(new EventB(["1"]));
        var stored = provider.GetRequiredService<InMemoryOutboxStore>().GetMessages().Single(static message => message.Target.Id == "1");

        // Act
        var text = Encoding.UTF8.GetString(provider.GetRequiredService<MsgHatchJson>().Serialize(stored).Value);

        // Assert
        Assert.Equal(
            (true, true),
            (text.Contains("\"$type\":\"Trace:Subscription@1\"", StringComparison.Ordinal),
                text.Contains("\"owner\":{\"level\":\"User\",\"company\":null,\"user\":\"1\"},\"recipients\":\"ScopeUsers\"", StringComparison.Ordinal)));
    }

    private ServiceProvider BuildScenarioHost(
        Func<TenantKey, ISubscriptionStore>? store = null, Func<TenantKey, IRecipientDirectory>? directory = null)
    {
        var services = TestHost.CreateServices();
        services.AddScenarioHost(
            key => _stores.GetOrAdd(key, store ?? (static _ => ScenarioDoubles.AScenarioStore())),
            key => _directories.GetOrAdd(key, directory ?? (static _ => ScenarioDoubles.AScenarioDirectory())));
        return services.Build();
    }

    private static Task<ErrorOr<NotificationResult>> ProcessScenarioEventAsync(
        IServiceProvider provider, string type, string[]? users, string? company) => type switch
        {
            "A" => provider.ProcessAsync(new EventA(users, company)),
            "B" => provider.ProcessAsync(new EventB(users, company)),
            "C" => provider.ProcessAsync(new EventC(users, company)),
            "D" => provider.ProcessAsync(new EventD(users, company)),
            "E" => provider.ProcessAsync(new EventE(users, company)),
            _ => provider.ProcessAsync(new EventG(users, company)),
        };

    private static ISubscriptionStore AFailingStore(Error error)
    {
        var store = Substitute.For<ISubscriptionStore>();
        store.FindForDeliveryAsync(default, default!, default!, default).ReturnsForAnyArgs(error);
        return store;
    }

    private static IRecipientDirectory AFailingDirectory(Error error)
    {
        var directory = Substitute.For<IRecipientDirectory>();
        directory.GetUsersAsync(default!, default).ReturnsForAnyArgs(error);
        return directory;
    }

    private static string TraceText(ResolutionTrace trace) =>
        $"{trace.Applied} {trace.Target} {trace.Language} {trace.Address} {string.Join(", ", trace.Skipped)}";

    private static string Keys<T>(ConcurrentDictionary<TenantKey, T> services) =>
        string.Join(",", services.Keys.Select(static key => key.Tenant?.Value ?? "none").Order(StringComparer.Ordinal));
}
