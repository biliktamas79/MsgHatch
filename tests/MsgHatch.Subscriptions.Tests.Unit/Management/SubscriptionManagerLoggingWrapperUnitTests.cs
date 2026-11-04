using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Email;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Subscriptions.Tests.Unit.Management;

/// <summary>
/// The standard logging wrapper tests for <see cref="ISubscriptionManager"/>, through the manager of
/// <c>UseSubscriptions</c> with the in-memory store, or with a store substitute that fails.
/// </summary>
public sealed class SubscriptionManagerLoggingWrapperUnitTests
{
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, int> SuccessRows => new()
    {
        { "GetChannels", 1120 },
        { "GetMessageTypes", 1122 },
        { "GetSubscriptions", 1124 },
        { "GetSubscription", 1126 },
        { "Create", 1128 },
        { "Update", 1130 },
        { "Delete", 1132 },
    };

    public static TheoryData<string, int> FailureRows => new()
    {
        { "GetMessageTypes", 1123 },
        { "GetSubscriptions", 1125 },
        { "GetSubscription", 1127 },
        { "Create", 1129 },
        { "Update", 1131 },
        { "Delete", 1133 },
    };

    public static TheoryData<int, LogLevel> LevelRows => new()
    {
        { Errors.Unavailable.Code, LogLevel.Warning },
        { Errors.Conflict.Code, LogLevel.Error },
        { Errors.OperationCancelled.Code, LogLevel.Information },
    };

    [Theory]
    [MemberData(nameof(SuccessRows))]
    public async Task Operation_Success_LogsADebugRecordWithItsEventIdAndNoTenant(string operation, int eventId)
    {
        // Arrange
        await using var provider = ManagerHost.Build(TestHost.NewFakeTime());
        await provider.SeedScenarioAsync();
        await using var scope = provider.CreateAsyncScope();

        // Act
        var error = await CallAsync(scope.ServiceProvider.GetRequiredService<ISubscriptionManager>(), operation);

        // Assert
        var record = Assert.Single(ManagerRecords(provider));
        Assert.Equal((operation, (Error?)null, LogLevel.Debug, eventId, (string?)null), (operation, error, record.Level, record.Id.Id, record.GetStructuredStateValue("Tenant")));
    }

    [Theory]
    [MemberData(nameof(FailureRows))]
    public async Task Operation_ErrorOfTheStore_ReturnsItAndLogsAWarningWithItsEventId(string operation, int eventId)
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        await using var provider = ManagerHost.Build(TestHost.NewFakeTime(), store: _ => AFailingStore(error));
        await using var scope = provider.CreateAsyncScope();

        // Act
        var returned = await CallAsync(scope.ServiceProvider.GetRequiredService<ISubscriptionManager>(), operation);

        // Assert
        var record = Assert.Single(ManagerRecords(provider));
        Assert.Equal((operation, true, LogLevel.Warning, eventId), (operation, ReferenceEquals(error, returned), record.Level, record.Id.Id));
    }

    [Theory]
    [MemberData(nameof(LevelRows))]
    public async Task GetSubscriptionsAsync_ErrorOfTheStore_LogsTheLevelOfTheError(int errorCode, LogLevel level)
    {
        // Arrange
        var errorType = new[] { Errors.Unavailable, Errors.Conflict, Errors.OperationCancelled }.Single(type => type.Code == errorCode);
        await using var provider = ManagerHost.Build(TestHost.NewFakeTime(), store: _ => AFailingStore(new Error(errorType)));
        await using var scope = provider.CreateAsyncScope();

        // Act
        await CallAsync(scope.ServiceProvider.GetRequiredService<ISubscriptionManager>(), "GetSubscriptions");

        // Assert
        var record = Assert.Single(ManagerRecords(provider));
        Assert.Equal((errorCode, level, 1125), (errorCode, record.Level, record.Id.Id));
    }

    [Fact]
    public async Task GetSubscriptionsAsync_ManagerOfAKey_LogsTheTenantOfTheKey()
    {
        // Arrange
        await using var provider = ManagerHost.Build(TestHost.NewFakeTime());
        await using var scope = provider.CreateAsyncScope();

        // Act
        await scope.ServiceProvider.GetRequiredKeyedService<ISubscriptionManager>(_t1).GetSubscriptionsAsync(new SubscriptionQuery(), Ct);

        // Assert
        Assert.Equal("t1", Assert.Single(ManagerRecords(provider)).GetStructuredStateValue("Tenant"));
    }

    [Fact]
    public async Task UseSubscriptions_EnableLoggingFalse_RegistersNoLoggingWrapperOfTheManager()
    {
        // Arrange
        await using var provider = ManagerHost.Build(TestHost.NewFakeTime(), enableLogging: false);
        await using var scope = provider.CreateAsyncScope();

        // Act
        await scope.ServiceProvider.GetRequiredService<ISubscriptionManager>().GetSubscriptionsAsync(new SubscriptionQuery(), Ct);

        // Assert
        Assert.Empty(ManagerRecords(provider));
    }

    [Fact]
    public async Task Operations_ProfilesWithEmailAddresses_NoLogRecordContainsAnAddress()
    {
        // Arrange
        await using var provider = ManagerHost.Build(TestHost.NewFakeTime());
        await provider.SeedScenarioAsync();
        await using var scope = provider.CreateAsyncScope();
        var manager = scope.ServiceProvider.GetRequiredService<ISubscriptionManager>();

        // Act
        await manager.GetMessageTypesAsync(new MessageTypeQuery(User: new UserId("1")), Ct);
        await manager.CreateAsync(
            new SubscriptionDraft(
                SubscriptionOwner.ForCompany(Scenario.Acme), new MessageType("A"), SubscriptionKind.Channel, EmailChannel.Id,
                new EmailChannelSettings(), SubscriptionRecipients.ForUsers([new UserId("2")])),
            Ct);

        // Assert
        Assert.Equal((2, false), (ManagerRecords(provider).Count, provider.LogRecords().Any(static record => record.ContainsText("@"))));
    }

    private static List<FakeLogRecord> ManagerRecords(IServiceProvider provider) =>
        [.. provider.LogRecords().Where(static record => record.Id.Id is >= 1120 and <= 1133)];

    private static ISubscriptionStore AFailingStore(Error error)
    {
        var store = Substitute.For<ISubscriptionStore>();
        store.FindForDeliveryAsync(default, default!, default!, default).ReturnsForAnyArgs(error);
        store.QueryAsync(default!, default).ReturnsForAnyArgs(error);
        store.GetAsync(default, default).ReturnsForAnyArgs(error);
        store.AddAsync(default!, default).ReturnsForAnyArgs(error);
        store.UpdateAsync(default!, default, default).ReturnsForAnyArgs(error);
        store.DeleteAsync(default, default).ReturnsForAnyArgs(error);
        return store;
    }

    private static async Task<Error?> CallAsync(ISubscriptionManager manager, string operation)
    {
        var draft = new SubscriptionDraft(
            SubscriptionOwner.ForUser(new UserId("1")), new MessageType("B"), SubscriptionKind.Channel, EmailChannel.Id,
            new EmailChannelSettings("en", EmailFormat.Html));
        return operation switch
        {
            "GetChannels" => (await manager.GetChannelsAsync(Ct)).Error,
            "GetMessageTypes" => (await manager.GetMessageTypesAsync(new MessageTypeQuery(), Ct)).Error,
            "GetSubscriptions" => (await manager.GetSubscriptionsAsync(new SubscriptionQuery(), Ct)).Error,
            "GetSubscription" => (await manager.GetSubscriptionAsync(Scenario.S6.Id, Ct)).Error,
            "Create" => (await manager.CreateAsync(draft with { Type = new MessageType("A") }, Ct)).Error,
            "Update" => (await manager.UpdateAsync(Scenario.S6.Id, 1, draft, Ct)).Error,
            _ => await manager.DeleteAsync(Scenario.S7.Id, Ct),
        };
    }
}
