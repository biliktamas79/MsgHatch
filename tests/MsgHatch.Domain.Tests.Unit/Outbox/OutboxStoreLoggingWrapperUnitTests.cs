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
        await using var provider = BuildWith(AStore(error: null));
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Act
        var error = await store.AddAsync([AMessage()], TestContext.Current.CancellationToken);

        // Assert
        Assert.Null(error);
    }

    [Fact]
    public async Task AddAsync_InnerError_ReturnsTheSameError()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        await using var provider = BuildWith(AStore(expected));
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Act
        var error = await store.AddAsync([AMessage()], TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(expected, error);
    }

    [Fact]
    public async Task AddAsync_TransientError_LogsAWarningWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AStore(new Error(Errors.Unavailable)));
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Act
        await store.AddAsync([AMessage()], TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Warning, 1301), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task AddAsync_PermanentError_LogsAnErrorWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AStore(new Error(Errors.ValidationFailed)));
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Act
        await store.AddAsync([AMessage()], TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, 1301), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task AddAsync_CancelledError_LogsInformationWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AStore(new Error(Errors.OperationCancelled)));
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Act
        await store.AddAsync([AMessage()], TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Information, 1301), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task AddAsync_EmptyList_LogsADebugRecordWithACountOfZero()
    {
        // Arrange
        await using var provider = BuildWith(AStore(error: null));
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Act
        await store.AddAsync([], TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Debug, "0"), (record.Level, record.GetStructuredStateValue("Count")));
    }

    [Fact]
    public async Task AddAsync_Success_LogsADebugRecordWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AStore(error: null));
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Act
        await store.AddAsync([AMessage()], TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Debug, 1300), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task AddAsync_TenantStoreOfAKey_LogsTheTenantOfTheKey()
    {
        // Arrange
        await using var provider = BuildWith(AStore(error: null));
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Act
        await store.AddAsync([AMessage()], TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal("t1", Assert.Single(provider.LogRecords()).GetStructuredStateValue("Tenant"));
    }

    [Fact]
    public async Task AddAsync_TestMessage_NoLogRecordContainsTheChannelAddress()
    {
        // Arrange
        await using var provider = BuildWith(AStore(error: null));
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);
        var message = TestMessages.AnOutgoingMessage(new TestEnvelope(_address), tenant: _t1.Tenant, flags: MessageFlags.Test);

        // Act
        await store.AddAsync([message], TestContext.Current.CancellationToken);

        // Assert
        Assert.NotEmpty(provider.LogRecords());
        Assert.DoesNotContain(provider.LogRecords(), static record => record.ContainsText(_address));
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
        await using var provider = BuildWith(AManagedStore(expected));
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Act
        var error = await CallAsync(store, member);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((expected, LogLevel.Warning, 1319), (error, record.Level, record.Id.Id));
    }

    [Theory]
    [MemberData(nameof(ManagementMembers))]
    public async Task ManagementMember_PermanentError_LogsAnErrorWithItsEventId(string member)
    {
        // Arrange
        await using var provider = BuildWith(AManagedStore(new Error(Errors.ValidationFailed)));
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Act
        await CallAsync(store, member);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, 1319), (record.Level, record.Id.Id));
    }

    [Theory]
    [MemberData(nameof(ManagementMembers))]
    public async Task ManagementMember_Success_LogsTheTenantOfTheKeyWithItsEventId(string member)
    {
        // Arrange
        await using var provider = BuildWith(AManagedStore(error: null));
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Act
        var error = await CallAsync(store, member);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(((Error?)null, LogLevel.Debug, 1318, "t1"), (error, record.Level, record.Id.Id, record.GetStructuredStateValue("Tenant")));
    }

    [Fact]
    public async Task RetryAsync_InnerCount_ReturnsTheCountUnchanged()
    {
        // Arrange
        var inner = AManagedStore(error: null);
        inner.RetryAsync(default!, default, default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<int>.ForValue(4)));
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredKeyedService<IOutboxStore>(_t1);

        // Act
        var changed = await store.RetryAsync([Guid.NewGuid()], TestMessages.CreatedAtUtc, 3, Ct);

        // Assert
        Assert.Equal(4, changed.Value);
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

        // Act
        var result = await store.GetAsync(Guid.NewGuid(), Ct);

        // Assert
        Assert.Same(expected, result.Value);
    }

    private static async Task<Error?> CallAsync(IOutboxStore store, string member) => member switch
    {
        nameof(IOutboxStore.QueryAsync) => (await store.QueryAsync(new OutboxQuery(), Ct)).Error,
        nameof(IOutboxStore.GetAsync) => (await store.GetAsync(Guid.NewGuid(), Ct)).Error,
        nameof(IOutboxStore.CountAsync) => (await store.CountAsync(new TrafficQuery(), Ct)).Error,
        nameof(IOutboxStore.RetryAsync) => (await store.RetryAsync([Guid.NewGuid()], TestMessages.CreatedAtUtc, 3, Ct)).Error,
        _ => (await store.CancelAsync([Guid.NewGuid()], TestMessages.CreatedAtUtc, Ct)).Error,
    };

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
