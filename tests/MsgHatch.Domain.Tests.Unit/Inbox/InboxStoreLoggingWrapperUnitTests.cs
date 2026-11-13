using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Inbox;

/// <summary>The standard logging wrapper tests for <see cref="IInboxStore"/>. A received message has no channel address, so the row of the address checks the event.</summary>
public sealed class InboxStoreLoggingWrapperUnitTests
{
    private const string _eventText = "client1@example.com";
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AddAsync_InnerError_ReturnsTheSameError()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        await using var provider = BuildWith(AStore(expected));
        var store = provider.GetRequiredKeyedService<IInboxStore>(_t1);

        // Act
        var error = await store.AddAsync(AMessage(), Ct);

        // Assert
        Assert.Same(expected, error);
    }

    [Fact]
    public async Task AddAsync_TransientError_LogsAWarningWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AStore(new Error(Errors.Unavailable)));
        var store = provider.GetRequiredKeyedService<IInboxStore>(_t1);

        // Act
        await store.AddAsync(AMessage(), Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Warning, 1201), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task AddAsync_PermanentError_LogsAnErrorWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AStore(new Error(Errors.ValidationFailed)));
        var store = provider.GetRequiredKeyedService<IInboxStore>(_t1);

        // Act
        await store.AddAsync(AMessage(), Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, 1201), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task AddAsync_Success_LogsTheTenantOfTheKeyWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AStore(error: null));
        var store = provider.GetRequiredKeyedService<IInboxStore>(_t1);

        // Act
        var error = await store.AddAsync(AMessage(), Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(((Error?)null, LogLevel.Debug, 1200, "t1"), (error, record.Level, record.Id.Id, record.GetStructuredStateValue("Tenant")));
    }

    [Fact]
    public async Task AddAsync_TestMessage_NoLogRecordContainsTheEvent()
    {
        // Arrange
        await using var provider = BuildWith(AStore(error: null));
        var store = provider.GetRequiredKeyedService<IInboxStore>(_t1);

        // Act
        await store.AddAsync(TestMessages.AReceivedMessage(new TestEvent(_eventText), _t1.Tenant, flags: MessageFlags.Test), Ct);

        // Assert
        Assert.NotEmpty(provider.LogRecords());
        Assert.DoesNotContain(provider.LogRecords(), static record => record.ContainsText(_eventText));
    }

    [Fact]
    public async Task UseInboxStore_EnableLoggingFalse_RegistersNoLoggingWrapper()
    {
        // Arrange
        var inner = AStore(error: null);
        await using var provider = BuildWith(inner, enableLogging: false);

        // Act
        var store = provider.GetRequiredKeyedService<IInboxStore>(_t1);

        // Assert
        Assert.Same(inner, store);
    }

    public static TheoryData<string> ManagementMembers =>
    [
        nameof(IInboxStore.QueryAsync), nameof(IInboxStore.GetAsync), nameof(IInboxStore.CountAsync), nameof(IInboxStore.RetryAsync), nameof(IInboxStore.CancelAsync),
    ];

    [Theory]
    [MemberData(nameof(ManagementMembers))]
    public async Task ManagementMember_TransientError_ReturnsTheSameErrorAndLogsAWarningWithItsEventId(string member)
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        await using var provider = BuildWith(AManagedStore(expected));
        var store = provider.GetRequiredKeyedService<IInboxStore>(_t1);

        // Act
        var error = await CallAsync(store, member);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((expected, LogLevel.Warning, 1215), (error, record.Level, record.Id.Id));
    }

    [Theory]
    [MemberData(nameof(ManagementMembers))]
    public async Task ManagementMember_PermanentError_LogsAnErrorWithItsEventId(string member)
    {
        // Arrange
        await using var provider = BuildWith(AManagedStore(new Error(Errors.ValidationFailed)));
        var store = provider.GetRequiredKeyedService<IInboxStore>(_t1);

        // Act
        await CallAsync(store, member);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, 1215), (record.Level, record.Id.Id));
    }

    [Theory]
    [MemberData(nameof(ManagementMembers))]
    public async Task ManagementMember_Success_LogsTheTenantOfTheKeyWithItsEventId(string member)
    {
        // Arrange
        await using var provider = BuildWith(AManagedStore(error: null));
        var store = provider.GetRequiredKeyedService<IInboxStore>(_t1);

        // Act
        var error = await CallAsync(store, member);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(((Error?)null, LogLevel.Debug, 1214, "t1"), (error, record.Level, record.Id.Id, record.GetStructuredStateValue("Tenant")));
    }

    [Fact]
    public async Task RetryAsync_InnerCount_ReturnsTheCountUnchanged()
    {
        // Arrange
        var inner = AManagedStore(error: null);
        inner.RetryAsync(default!, default, default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<int>.ForValue(4)));
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredKeyedService<IInboxStore>(_t1);

        // Act
        var changed = await store.RetryAsync([Guid.NewGuid()], TestMessages.CreatedAtUtc, 3, Ct);

        // Assert
        Assert.Equal(4, changed.Value);
    }

    [Fact]
    public async Task GetAsync_InnerResult_ReturnsTheSameResult()
    {
        // Arrange
        var expected = (ReceivedMessage)AMessage();
        var inner = AManagedStore(error: null);
        inner.GetAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<ReceivedMessage?>.ForValue(expected)));
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredKeyedService<IInboxStore>(_t1);

        // Act
        var result = await store.GetAsync(Guid.NewGuid(), Ct);

        // Assert
        Assert.Same(expected, result.Value);
    }

    private static async Task<Error?> CallAsync(IInboxStore store, string member) => member switch
    {
        nameof(IInboxStore.QueryAsync) => (await store.QueryAsync(new InboxQuery(), Ct)).Error,
        nameof(IInboxStore.GetAsync) => (await store.GetAsync(Guid.NewGuid(), Ct)).Error,
        nameof(IInboxStore.CountAsync) => (await store.CountAsync(new TrafficQuery(), Ct)).Error,
        nameof(IInboxStore.RetryAsync) => (await store.RetryAsync([Guid.NewGuid()], TestMessages.CreatedAtUtc, 3, Ct)).Error,
        _ => (await store.CancelAsync([Guid.NewGuid()], TestMessages.CreatedAtUtc, Ct)).Error,
    };

    private static IInboxStore AManagedStore(Error? error)
    {
        var store = Substitute.For<IInboxStore>();
        store.QueryAsync(default!, default).ReturnsForAnyArgs(ValueTask.FromResult(error is null
            ? ErrorOr<MessagePage<ReceivedMessageSummary>>.ForValue(new MessagePage<ReceivedMessageSummary>([], Next: null))
            : ErrorOr<MessagePage<ReceivedMessageSummary>>.ForError(error)));
        store.GetAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult(
            error is null ? ErrorOr<ReceivedMessage?>.ForValue(null) : ErrorOr<ReceivedMessage?>.ForError(error)));
        store.CountAsync(default!, default).ReturnsForAnyArgs(ValueTask.FromResult(error is null
            ? ErrorOr<IReadOnlyList<MessageCount>>.ForValue([])
            : ErrorOr<IReadOnlyList<MessageCount>>.ForError(error)));
        var changed = ValueTask.FromResult(error is null ? ErrorOr<int>.ForValue(0) : ErrorOr<int>.ForError(error));
        store.RetryAsync(default!, default, default, default).ReturnsForAnyArgs(changed);
        store.CancelAsync(default!, default, default).ReturnsForAnyArgs(changed);
        return store;
    }

    private static IInboxStore AStore(Error? error)
    {
        var store = Substitute.For<IInboxStore>();
        store.AddAsync(default!, default).ReturnsForAnyArgs(ValueTask.FromResult(error));
        return store;
    }

    private static ReceivedMessage<TestEvent> AMessage() => TestMessages.AReceivedMessage(new TestEvent("text"), _t1.Tenant);

    private static ServiceProvider BuildWith(IInboxStore inner, bool enableLogging = true)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging).UseInboxStore((_, _) => inner);
        return services.Build();
    }
}
