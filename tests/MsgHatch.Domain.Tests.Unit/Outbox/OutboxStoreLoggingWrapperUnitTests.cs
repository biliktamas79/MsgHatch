using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Outbox;

/// <summary>The standard logging wrapper tests for <see cref="IOutboxStore"/>.</summary>
public sealed class OutboxStoreLoggingWrapperUnitTests
{
    private const string _address = "client1@example.com";
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task AddAsync_InnerSuccess_ReturnsTheResultUnchanged()
    {
        // Arrange
        var inner = AStore(error: null);
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);
        OutgoingMessage[] messages = [AMessage()];

        // Act
        var error = await store.AddAsync(messages, TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(error);
        _ = inner.Received(1).AddAsync(messages, Ct);
    }

    [Fact]
    public async Task AddAsync_InnerError_ReturnsTheSameError()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        var inner = AStore(expected);
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);
        OutgoingMessage[] messages = [AMessage()];

        // Act
        var error = await store.AddAsync(messages, TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(expected, error);
        _ = inner.Received(1).AddAsync(messages, Ct);
    }

    [Fact]
    public async Task AddAsync_TransientError_LogsAWarningWithItsEventId()
    {
        // Arrange
        var inner = AStore(new Error(Errors.Unavailable));
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);
        OutgoingMessage[] messages = [AMessage()];

        // Act
        await store.AddAsync(messages, TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Warning, 1301), (record.Level, record.Id.Id));
        _ = inner.Received(1).AddAsync(messages, Ct);
    }

    [Fact]
    public async Task AddAsync_PermanentError_LogsAnErrorWithItsEventId()
    {
        // Arrange
        var inner = AStore(new Error(Errors.ValidationFailed));
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);
        OutgoingMessage[] messages = [AMessage()];

        // Act
        await store.AddAsync(messages, TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, 1301), (record.Level, record.Id.Id));
        _ = inner.Received(1).AddAsync(messages, Ct);
    }

    [Fact]
    public async Task AddAsync_CancelledError_LogsInformationWithItsEventId()
    {
        // Arrange
        var inner = AStore(new Error(Errors.OperationCancelled));
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);
        OutgoingMessage[] messages = [AMessage()];

        // Act
        await store.AddAsync(messages, TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Information, 1301), (record.Level, record.Id.Id));
        _ = inner.Received(1).AddAsync(messages, Ct);
    }

    [Fact]
    public async Task AddAsync_EmptyList_LogsADebugRecordWithACountOfZero()
    {
        // Arrange
        var inner = AStore(error: null);
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        OutgoingMessage[] messages = [];

        // Act
        await store.AddAsync(messages, TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Debug, "0"), (record.Level, record.GetStructuredStateValue("Count")));
        _ = inner.Received(1).AddAsync(messages, Ct);
    }

    [Fact]
    public async Task AddAsync_Success_LogsADebugRecordWithItsEventId()
    {
        // Arrange
        var inner = AStore(error: null);
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);
        OutgoingMessage[] messages = [AMessage()];

        // Act
        await store.AddAsync(messages, TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Debug, 1300), (record.Level, record.Id.Id));
        _ = inner.Received(1).AddAsync(messages, Ct);
    }

    [Fact]
    public async Task AddAsync_TenantStoreOfAKey_LogsTheTenantOfTheKey()
    {
        // Arrange
        var inner = AStore(error: null);
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);
        OutgoingMessage[] messages = [AMessage()];

        // Act
        await store.AddAsync(messages, TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("t1", Assert.Single(provider.LogRecords()).GetStructuredStateValue("Tenant"));
        _ = inner.Received(1).AddAsync(messages, Ct);
    }

    [Fact]
    public async Task AddAsync_TestMessage_NoLogRecordContainsTheChannelAddress()
    {
        // Arrange
        var inner = AStore(error: null);
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);
        var message = TestMessages.AnOutgoingMessage(new TestEnvelope(_address), tenant: _t1.Tenant, flags: MessageFlags.Test);

        OutgoingMessage[] messages = [message];

        // Act
        await store.AddAsync(messages, TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(provider.LogRecords());
        Assert.DoesNotContain(provider.LogRecords(), static record => record.ContainsText(_address));
        _ = inner.Received(1).AddAsync(messages, Ct);
    }

    [Fact]
    public async Task UseOutboxStore_EnableLoggingFalse_RegistersNoLoggingWrapper()
    {
        // Arrange
        var inner = AStore(error: null);
        await using var provider = BuildWith(inner, enableLogging: false);

        // Act
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Assert
        Assert.Same(inner, store);
    }

    public static TheoryData<string> ManagementMembers =>
    [
        nameof(IOutboxStore.QueryAsync), nameof(IOutboxStore.GetAsync), nameof(IOutboxStore.CountAsync), nameof(IOutboxStore.RetryAsync), nameof(IOutboxStore.CancelAsync),
    ];

    [Theory]
    [MemberData(nameof(ManagementMembers))]
    public async Task ManagementMember_TransientError_ReturnsTheSameErrorAndLogsAWarningWithItsEventId(string member)
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        var inner = AManagedStore(expected);
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);
        Guid[] ids = [Guid.NewGuid()];

        // Act
        var error = await CallAsync(store, member, ids);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((expected, LogLevel.Warning, 1319), (error, record.Level, record.Id.Id));
        AssertForwardedOnce(inner, member, ids);
    }

    [Theory]
    [MemberData(nameof(ManagementMembers))]
    public async Task ManagementMember_PermanentError_LogsAnErrorWithItsEventId(string member)
    {
        // Arrange
        var inner = AManagedStore(new Error(Errors.ValidationFailed));
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);
        Guid[] ids = [Guid.NewGuid()];

        // Act
        await CallAsync(store, member, ids);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, 1319), (record.Level, record.Id.Id));
        AssertForwardedOnce(inner, member, ids);
    }

    [Theory]
    [MemberData(nameof(ManagementMembers))]
    public async Task ManagementMember_Success_LogsTheTenantOfTheKeyWithItsEventId(string member)
    {
        // Arrange
        var inner = AManagedStore(error: null);
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);
        Guid[] ids = [Guid.NewGuid()];

        // Act
        var error = await CallAsync(store, member, ids);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(((Error?)null, LogLevel.Debug, 1318, "t1"), (error, record.Level, record.Id.Id, record.GetStructuredStateValue("Tenant")));
        AssertForwardedOnce(inner, member, ids);
    }

    [Fact]
    public async Task RetryAsync_InnerCount_ReturnsTheCountUnchanged()
    {
        // Arrange
        var inner = AManagedStore(error: null);
        inner.RetryAsync(default!, default, default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<int>.ForValue(4)));
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);
        Guid[] ids = [Guid.NewGuid()];

        // Act
        var changed = await store.RetryAsync(ids, TestMessages.CreatedAtUtc, 3, Ct);

        // Assert
        Assert.Equal(4, changed.Value);
        _ = inner.Received(1).RetryAsync(ids, TestMessages.CreatedAtUtc, 3, Ct);
    }

    [Fact]
    public async Task GetAsync_InnerResult_ReturnsTheSameResult()
    {
        // Arrange
        var expected = new OutgoingMessageHistory(AMessage(), []);
        var inner = AManagedStore(error: null);
        inner.GetAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<OutgoingMessageHistory?>.ForValue(expected)));
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);
        var id = Guid.NewGuid();

        // Act
        var result = await store.GetAsync(id, Ct);

        // Assert
        Assert.Same(expected, result.Value);
        _ = inner.Received(1).GetAsync(id, Ct);
    }

    private static async Task<Error?> CallAsync(IOutboxStore store, string member, Guid[] ids) => member switch
    {
        nameof(IOutboxStore.QueryAsync) => (await store.QueryAsync(new OutboxQuery(), Ct)).Error,
        nameof(IOutboxStore.GetAsync) => (await store.GetAsync(ids[0], Ct)).Error,
        nameof(IOutboxStore.CountAsync) => (await store.CountAsync(new TrafficQuery(), Ct)).Error,
        nameof(IOutboxStore.RetryAsync) => (await store.RetryAsync(ids, TestMessages.CreatedAtUtc, 3, Ct)).Error,
        _ => (await store.CancelAsync(ids, TestMessages.CreatedAtUtc, Ct)).Error,
    };

    /// <summary>Asserts that the wrapper called the member of <see cref="CallAsync"/> on the inner store one time with the same arguments, and no other member.</summary>
    private static void AssertForwardedOnce(IOutboxStore inner, string member, Guid[] ids)
    {
        Assert.Single(inner.ReceivedCalls());
        switch (member)
        {
            case nameof(IOutboxStore.QueryAsync):
                _ = inner.Received(1).QueryAsync(new OutboxQuery(), Ct);
                break;
            case nameof(IOutboxStore.GetAsync):
                _ = inner.Received(1).GetAsync(ids[0], Ct);
                break;
            case nameof(IOutboxStore.CountAsync):
                _ = inner.Received(1).CountAsync(new TrafficQuery(), Ct);
                break;
            case nameof(IOutboxStore.RetryAsync):
                _ = inner.Received(1).RetryAsync(ids, TestMessages.CreatedAtUtc, 3, Ct);
                break;
            default:
                _ = inner.Received(1).CancelAsync(ids, TestMessages.CreatedAtUtc, Ct);
                break;
        }
    }

    private static IOutboxStore AManagedStore(Error? error)
    {
        var store = Substitute.For<IOutboxStore>();
        store.QueryAsync(default!, default).ReturnsForAnyArgs(ValueTask.FromResult(error is null
            ? ErrorOr<MessagePage<OutgoingMessageSummary>>.ForValue(new MessagePage<OutgoingMessageSummary>([], Next: null))
            : ErrorOr<MessagePage<OutgoingMessageSummary>>.ForError(error)));
        store.GetAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult(
            error is null ? ErrorOr<OutgoingMessageHistory?>.ForValue(null) : ErrorOr<OutgoingMessageHistory?>.ForError(error)));
        store.CountAsync(default!, default).ReturnsForAnyArgs(ValueTask.FromResult(error is null
            ? ErrorOr<IReadOnlyList<MessageCount>>.ForValue([])
            : ErrorOr<IReadOnlyList<MessageCount>>.ForError(error)));
        var changed = ValueTask.FromResult(error is null ? ErrorOr<int>.ForValue(0) : ErrorOr<int>.ForError(error));
        store.RetryAsync(default!, default, default, default).ReturnsForAnyArgs(changed);
        store.CancelAsync(default!, default, default).ReturnsForAnyArgs(changed);
        return store;
    }

    private static IOutboxStore AStore(Error? error)
    {
        var store = Substitute.For<IOutboxStore>();
        store.AddAsync(default!, default).ReturnsForAnyArgs(ValueTask.FromResult(error));
        return store;
    }

    private static OutgoingMessage<TestEnvelope> AMessage() => TestMessages.AnOutgoingMessage(tenant: _t1.Tenant);

    private static ServiceProvider BuildWith(IOutboxStore inner, bool enableLogging = true)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging).UseOutboxStore((_, _) => inner);
        return services.Build();
    }
}
