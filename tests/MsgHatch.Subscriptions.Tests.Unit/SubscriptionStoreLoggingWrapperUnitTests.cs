using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Subscriptions.Tests.Unit;

/// <summary>The standard logging wrapper tests for <see cref="ISubscriptionStore"/>.</summary>
public sealed class SubscriptionStoreLoggingWrapperUnitTests
{
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));
    private static readonly Subscription _subscription = TestSubscriptions.A();
    private static readonly IReadOnlyList<Subscription> _subscriptions = [_subscription, TestSubscriptions.A(type: "B")];
    private static readonly CompanyId[] _companies = [new("100")];
    private static readonly UserId[] _users = [new("1")];

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, object?> SuccessRows => new()
    {
        { "FindForDelivery", _subscriptions },
        { "Query", _subscriptions },
        { "Get", _subscription },
        { "Add", null },
        { "Update", null },
        { "Delete", null },
    };

    public static TheoryData<string, int> SuccessEventRows => new()
    {
        { "FindForDelivery", 1100 },
        { "Query", 1102 },
        { "Get", 1104 },
        { "Add", 1106 },
        { "Update", 1108 },
        { "Delete", 1110 },
    };

    public static TheoryData<string, int, LogLevel, int> ErrorRows
    {
        get
        {
            var rows = new TheoryData<string, int, LogLevel, int>();
            foreach (var (operation, eventId) in new[] { ("FindForDelivery", 1101), ("Query", 1103), ("Get", 1105), ("Add", 1107), ("Update", 1109), ("Delete", 1111) })
            {
                rows.Add(operation, Errors.Unavailable.Code, LogLevel.Warning, eventId);
                rows.Add(operation, Errors.Conflict.Code, LogLevel.Error, eventId);
                rows.Add(operation, Errors.OperationCancelled.Code, LogLevel.Information, eventId);
            }

            return rows;
        }
    }

    public static TheoryData<string> Operations => ["FindForDelivery", "Query", "Get", "Add", "Update", "Delete"];

    [Theory]
    [MemberData(nameof(SuccessRows))]
    public async Task Operation_InnerSuccess_ReturnsTheResultUnchanged(string operation, object? expected)
    {
        // Arrange
        var inner = AStore(error: null);
        await using var provider = BuildWith(inner);

        // Act
        var result = await CallAsync(provider.GetRequiredKeyedService<ISubscriptionStore>(_t1), operation);

        // Assert
        Assert.Equal((operation, true, (Error?)null), (operation, ReferenceEquals(expected, result.Value), result.Error));
        await ReceivedOneCallAsync(inner, operation);
    }

    [Theory]
    [MemberData(nameof(Operations))]
    public async Task Operation_InnerError_ReturnsTheSameError(string operation)
    {
        // Arrange
        var error = new Error(Errors.Conflict);
        var inner = AStore(error);
        await using var provider = BuildWith(inner);

        // Act
        var result = await CallAsync(provider.GetRequiredKeyedService<ISubscriptionStore>(_t1), operation);

        // Assert
        Assert.Equal((operation, true), (operation, ReferenceEquals(error, result.Error)));
        await ReceivedOneCallAsync(inner, operation);
    }

    [Theory]
    [MemberData(nameof(SuccessEventRows))]
    public async Task Operation_InnerSuccess_LogsADebugRecordWithItsEventIdAndTheTenantOfTheKey(string operation, int eventId)
    {
        // Arrange
        var inner = AStore(error: null);
        await using var provider = BuildWith(inner);

        // Act
        await CallAsync(provider.GetRequiredKeyedService<ISubscriptionStore>(_t1), operation);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((operation, LogLevel.Debug, eventId, "t1"), (operation, record.Level, record.Id.Id, record.GetStructuredStateValue("Tenant")));
        await ReceivedOneCallAsync(inner, operation);
    }

    [Theory]
    [MemberData(nameof(ErrorRows))]
    public async Task Operation_InnerError_LogsTheLevelOfTheErrorWithItsEventId(string operation, int errorCode, LogLevel level, int eventId)
    {
        // Arrange
        var inner = AStore(new Error(ErrorTypeOf(errorCode)));
        await using var provider = BuildWith(inner);

        // Act
        await CallAsync(provider.GetRequiredKeyedService<ISubscriptionStore>(_t1), operation);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((operation, errorCode, level, eventId), (operation, errorCode, record.Level, record.Id.Id));
        await ReceivedOneCallAsync(inner, operation);
    }

    [Fact]
    public async Task FindForDeliveryAsync_StoreOfNoTenant_LogsNoTenant()
    {
        // Arrange
        const string operation = "FindForDelivery";
        var inner = AStore(error: null);
        await using var provider = BuildWith(inner);

        // Act
        await CallAsync(provider.GetRequiredService<ISubscriptionStore>(), operation);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(((string?)null, 1100), (record.GetStructuredStateValue("Tenant"), record.Id.Id));
        await ReceivedOneCallAsync(inner, operation);
    }

    [Fact]
    public async Task UseSubscriptionStore_EnableLoggingFalse_RegistersNoLoggingWrapper()
    {
        // Arrange
        var inner = AStore(error: null);
        await using var provider = BuildWith(inner, enableLogging: false);

        // Act
        var store = provider.GetRequiredKeyedService<ISubscriptionStore>(_t1);

        // Assert
        Assert.Same(inner, store);
    }

    private static ErrorType ErrorTypeOf(int code) =>
        new[] { Errors.Unavailable, Errors.Conflict, Errors.OperationCancelled }.Single(type => type.Code == code);

    private static ISubscriptionStore AStore(Error? error)
    {
        var store = Substitute.For<ISubscriptionStore>();
        store.FindForDeliveryAsync(default, default!, default!, default)
            .ReturnsForAnyArgs(error is null ? ErrorOr<IReadOnlyList<Subscription>>.ForValue(_subscriptions) : error);
        store.QueryAsync(default!, default)
            .ReturnsForAnyArgs(error is null ? ErrorOr<IReadOnlyList<Subscription>>.ForValue(_subscriptions) : error);
        store.GetAsync(default, default).ReturnsForAnyArgs(error is null ? ErrorOr<Subscription?>.ForValue(_subscription) : error);
        store.AddAsync(default!, default).ReturnsForAnyArgs(error);
        store.UpdateAsync(default!, default, default).ReturnsForAnyArgs(error);
        store.DeleteAsync(default, default).ReturnsForAnyArgs(error);
        return store;
    }

    private static async Task<(object? Value, Error? Error)> CallAsync(ISubscriptionStore store, string operation) => operation switch
    {
        "FindForDelivery" => Of(await store.FindForDeliveryAsync(new MessageType("A"), _companies, _users, Ct)),
        "Query" => Of(await store.QueryAsync(new SubscriptionQuery(), Ct)),
        "Get" => Of(await store.GetAsync(_subscription.Id, Ct)),
        "Add" => (null, await store.AddAsync(_subscription, Ct)),
        "Update" => (null, await store.UpdateAsync(_subscription with { Revision = 2 }, 1, Ct)),
        _ => (null, await store.DeleteAsync(_subscription.Id, Ct)),
    };

    private static async Task ReceivedOneCallAsync(ISubscriptionStore inner, string operation)
    {
        switch (operation)
        {
            case "FindForDelivery":
                await inner.Received(1).FindForDeliveryAsync(new MessageType("A"), _companies, _users, Ct);
                break;
            case "Query":
                await inner.Received(1).QueryAsync(new SubscriptionQuery(), Ct);
                break;
            case "Get":
                await inner.Received(1).GetAsync(_subscription.Id, Ct);
                break;
            case "Add":
                await inner.Received(1).AddAsync(_subscription, Ct);
                break;
            case "Update":
                await inner.Received(1).UpdateAsync(_subscription with { Revision = 2 }, 1, Ct);
                break;
            default:
                await inner.Received(1).DeleteAsync(_subscription.Id, Ct);
                break;
        }
    }

    private static (object? Value, Error? Error) Of<T>(ErrorOr<T> result) => result.IsError ? (null, result.Error) : (result.Value, null);

    private static ServiceProvider BuildWith(ISubscriptionStore inner, bool enableLogging = true)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging).UseSubscriptionStore((_, _) => inner);
        return services.Build();
    }
}
