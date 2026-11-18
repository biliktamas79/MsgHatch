using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Outbox;

/// <summary>
/// The standard logging wrapper tests for <see cref="IOutboxDispatchStore"/>. It is not a tenant service, so it logs
/// the tenant of a message, where a member gets one.
/// </summary>
public sealed class OutboxDispatchStoreLoggingWrapperUnitTests
{
    [Fact]
    public async Task DeleteTestDataAsync_InnerCount_ReturnsTheCountUnchanged()
    {
        // Arrange
        const int count = 3;
        await using var provider = BuildWith(AStore(count));
        var store = provider.GetRequiredService<IOutboxDispatchStore>();

        // Act
        var deleted = await store.DeleteTestDataAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(count, deleted.Value);
    }

    [Fact]
    public async Task DeleteTestDataAsync_InnerError_ReturnsTheSameError()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        await using var provider = BuildWith(AStore(expected));
        var store = provider.GetRequiredService<IOutboxDispatchStore>();

        // Act
        var deleted = await store.DeleteTestDataAsync(TestContext.Current.CancellationToken);

        // Assert
        Assert.Same(expected, deleted.Error);
    }

    [Fact]
    public async Task DeleteTestDataAsync_TransientError_LogsAWarningWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AStore(new Error(Errors.Unavailable)));
        var store = provider.GetRequiredService<IOutboxDispatchStore>();

        // Act
        await store.DeleteTestDataAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Warning, 1303), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task DeleteTestDataAsync_PermanentError_LogsAnErrorWithItsEventId()
    {
        // Arrange
        await using var provider = BuildWith(AStore(new Error(Errors.ValidationFailed)));
        var store = provider.GetRequiredService<IOutboxDispatchStore>();

        // Act
        await store.DeleteTestDataAsync(TestContext.Current.CancellationToken);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, 1303), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task UseOutboxDispatchStore_EnableLoggingFalse_RegistersNoLoggingWrapper()
    {
        // Arrange
        var inner = AStore(0);
        await using var provider = BuildWith(inner, enableLogging: false);

        // Act
        var store = provider.GetRequiredService<IOutboxDispatchStore>();

        // Assert
        Assert.Same(inner, store);
    }

    public static TheoryData<string, int> FailedEventRows => new()
    {
        { nameof(IOutboxDispatchStore.ClaimDueAsync), 1305 },
        { nameof(IOutboxDispatchStore.RecordTryAsync), 1307 },
        { nameof(IOutboxDispatchStore.GetDownChannelsAsync), 1309 },
        { nameof(IOutboxDispatchStore.SetChannelDownAsync), 1311 },
        { nameof(IOutboxDispatchStore.SetChannelUpAsync), 1311 },
        { nameof(IOutboxDispatchStore.TryStartChannelCheckAsync), 1311 },
        { nameof(IOutboxDispatchStore.GetChannelStatesAsync), 1321 },
        { nameof(IOutboxDispatchStore.CountAsync), 1317 },
        { nameof(IOutboxDispatchStore.PurgeAsync), 1317 },
    };

    public static TheoryData<string, int> SucceededEventRows => new()
    {
        { nameof(IOutboxDispatchStore.ClaimDueAsync), 1304 },
        { nameof(IOutboxDispatchStore.RecordTryAsync), 1306 },
        { nameof(IOutboxDispatchStore.GetDownChannelsAsync), 1308 },
        { nameof(IOutboxDispatchStore.SetChannelDownAsync), 1310 },
        { nameof(IOutboxDispatchStore.SetChannelUpAsync), 1310 },
        { nameof(IOutboxDispatchStore.TryStartChannelCheckAsync), 1310 },
        { nameof(IOutboxDispatchStore.GetChannelStatesAsync), 1320 },
        { nameof(IOutboxDispatchStore.CountAsync), 1316 },
        { nameof(IOutboxDispatchStore.PurgeAsync), 1316 },
    };

    [Theory]
    [MemberData(nameof(FailedEventRows))]
    public async Task Member_TransientError_ReturnsTheSameErrorAndLogsAWarningWithItsEventId(string member, int eventId)
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        await using var provider = BuildWith(AStoreOf(expected));
        var store = provider.GetRequiredService<IOutboxDispatchStore>();

        // Act
        var error = await CallAsync(store, member);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((expected, LogLevel.Warning, eventId), (error, record.Level, record.Id.Id));
    }

    [Theory]
    [MemberData(nameof(FailedEventRows))]
    public async Task Member_PermanentError_LogsAnErrorWithItsEventId(string member, int eventId)
    {
        // Arrange
        await using var provider = BuildWith(AStoreOf(new Error(Errors.ValidationFailed)));
        var store = provider.GetRequiredService<IOutboxDispatchStore>();

        // Act
        await CallAsync(store, member);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, eventId), (record.Level, record.Id.Id));
    }

    [Theory]
    [MemberData(nameof(SucceededEventRows))]
    public async Task Member_Success_LogsADebugRecordWithItsEventId(string member, int eventId)
    {
        // Arrange
        await using var provider = BuildWith(AStoreOf(error: null));
        var store = provider.GetRequiredService<IOutboxDispatchStore>();

        // Act
        var error = await CallAsync(store, member);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(((Error?)null, LogLevel.Debug, eventId), (error, record.Level, record.Id.Id));
    }

    [Fact]
    public async Task ClaimDueAsync_InnerResult_ReturnsTheResultUnchanged()
    {
        // Arrange
        var claimed = new ClaimResult<OutgoingMessage>([TestMessages.AnOutgoingMessage()], TestMessages.CreatedAtUtc);
        var inner = AStoreOf(error: null);
        inner.ClaimDueAsync(default, default, default, default, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<ClaimResult<OutgoingMessage>>.ForValue(claimed)));
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredService<IOutboxDispatchStore>();

        // Act
        var result = await store.ClaimDueAsync(TestChannel.Id, 1, TestMessages.CreatedAtUtc, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Same(claimed, result.Value);
    }

    [Fact]
    public async Task CountAsync_InnerRows_ReturnsTheRowsUnchanged()
    {
        // Arrange
        IReadOnlyList<MessageCount> rows =
        [
            new(new MessageType("TestEvent"), TestChannel.Id, DeliveryStatus.Pending, Test: false, 2, TestMessages.CreatedAtUtc),
            new(new MessageType("TestEvent"), TestChannel.Id, DeliveryStatus.Done, Test: true, 1, null),
        ];
        var inner = AStoreOf(error: null);
        inner.CountAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageCount>>.ForValue(rows)));
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredService<IOutboxDispatchStore>();

        // Act
        var counts = await store.CountAsync(new TrafficQuery(), Ct);

        // Assert
        Assert.Same(rows, counts.Value);
    }

    [Fact]
    public async Task PurgeAsync_InnerCount_ReturnsTheCountUnchanged()
    {
        // Arrange
        var inner = AStoreOf(error: null);
        inner.PurgeAsync(default, default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<int>.ForValue(7)));
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredService<IOutboxDispatchStore>();

        // Act
        var purged = await store.PurgeAsync(TestMessages.CreatedAtUtc, 10, Ct);

        // Assert
        Assert.Equal(7, purged.Value);
    }

    [Fact]
    public async Task RecordTryAsync_TestMessageOfATenant_LogsTheTenantAndTheTestFlagAndNoChannelAddress()
    {
        // Arrange
        await using var provider = BuildWith(AStoreOf(error: null));
        var store = provider.GetRequiredService<IOutboxDispatchStore>();
        var message = TestMessages.AnOutgoingMessage(new TestEnvelope(_address), tenant: new TenantId("t1"), flags: MessageFlags.Test);

        // Act
        await store.RecordTryAsync(message, ATry(message), nextTryAtUtc: null, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(("t1", "Test"), (record.GetStructuredStateValue("Tenant"), record.GetStructuredStateValue("MessageFlags")));
        Assert.False(record.ContainsText(_address));
    }

    [Fact]
    public async Task SetChannelDownAsync_PauseWithANote_NoLogRecordContainsTheNote()
    {
        // Arrange
        const string note = "call client1@example.com";
        await using var provider = BuildWith(AStoreOf(error: null));
        var store = provider.GetRequiredService<IOutboxDispatchStore>();

        // Act
        await store.SetChannelDownAsync(APause(note), Ct);

        // Assert
        Assert.False(Assert.Single(provider.LogRecords()).ContainsText(note));
    }

    private const string _address = "client1@example.com";

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static ChannelDown APause(string note) =>
        new(TestChannel.Id, ChannelDownReason.Operator, TestMessages.CreatedAtUtc, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, note);

    private static PublishTry ATry(OutgoingMessage message) =>
        new(message.Delivery.Id, 1, TestMessages.CreatedAtUtc, TestMessages.CreatedAtUtc, PublishOutcome.Succeeded, "provider-1", null, null, null);

    private static async Task<Error?> CallAsync(IOutboxDispatchStore store, string member)
    {
        var now = TestMessages.CreatedAtUtc;
        var message = TestMessages.AnOutgoingMessage();
        return member switch
        {
            nameof(IOutboxDispatchStore.ClaimDueAsync) => (await store.ClaimDueAsync(TestChannel.Id, 1, now, TimeSpan.FromSeconds(30), Ct)).Error,
            nameof(IOutboxDispatchStore.RecordTryAsync) => await store.RecordTryAsync(message, ATry(message), nextTryAtUtc: null, Ct),
            nameof(IOutboxDispatchStore.GetDownChannelsAsync) => (await store.GetDownChannelsAsync(Ct)).Error,
            nameof(IOutboxDispatchStore.SetChannelDownAsync) => (await store.SetChannelDownAsync(APause("note"), Ct)).Error,
            nameof(IOutboxDispatchStore.SetChannelUpAsync) => (await store.SetChannelUpAsync(TestChannel.Id, reason: null, now, Ct)).Error,
            nameof(IOutboxDispatchStore.GetChannelStatesAsync) => (await store.GetChannelStatesAsync(now, Ct)).Error,
            nameof(IOutboxDispatchStore.TryStartChannelCheckAsync) => (await store.TryStartChannelCheckAsync(TestChannel.Id, now, now.AddSeconds(30), Ct)).Error,
            nameof(IOutboxDispatchStore.CountAsync) => (await store.CountAsync(new TrafficQuery(), Ct)).Error,
            _ => (await store.PurgeAsync(now, 10, Ct)).Error,
        };
    }

    private static IOutboxDispatchStore AStoreOf(Error? error)
    {
        var store = Substitute.For<IOutboxDispatchStore>();
        store.ClaimDueAsync(default, default, default, default, default).ReturnsForAnyArgs(ValueTask.FromResult(error is null
            ? ErrorOr<ClaimResult<OutgoingMessage>>.ForValue(new ClaimResult<OutgoingMessage>([], null))
            : ErrorOr<ClaimResult<OutgoingMessage>>.ForError(error)));
        store.RecordTryAsync(default!, default!, default, default).ReturnsForAnyArgs(ValueTask.FromResult(error));
        store.GetDownChannelsAsync(default).ReturnsForAnyArgs(ValueTask.FromResult(error is null
            ? ErrorOr<IReadOnlyList<ChannelDown>>.ForValue([])
            : ErrorOr<IReadOnlyList<ChannelDown>>.ForError(error)));
        var changed = ValueTask.FromResult(error is null ? ErrorOr<bool>.ForValue(true) : ErrorOr<bool>.ForError(error));
        store.SetChannelDownAsync(default!, default).ReturnsForAnyArgs(changed);
        store.SetChannelUpAsync(default, default, default, default).ReturnsForAnyArgs(changed);
        store.GetChannelStatesAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult(error is null
            ? ErrorOr<IReadOnlyList<ChannelState>>.ForValue([])
            : ErrorOr<IReadOnlyList<ChannelState>>.ForError(error)));
        store.TryStartChannelCheckAsync(default, default, default, default).ReturnsForAnyArgs(changed);
        store.CountAsync(default!, default).ReturnsForAnyArgs(ValueTask.FromResult(error is null
            ? ErrorOr<IReadOnlyList<MessageCount>>.ForValue([])
            : ErrorOr<IReadOnlyList<MessageCount>>.ForError(error)));
        store.PurgeAsync(default, default, default).ReturnsForAnyArgs(ValueTask.FromResult(
            error is null ? ErrorOr<int>.ForValue(0) : ErrorOr<int>.ForError(error)));
        return store;
    }

    private static IOutboxDispatchStore AStore(ErrorOr<int> result)
    {
        var store = Substitute.For<IOutboxDispatchStore>();
        store.DeleteTestDataAsync(default).ReturnsForAnyArgs(ValueTask.FromResult(result));
        return store;
    }

    private static ServiceProvider BuildWith(IOutboxDispatchStore inner, bool enableLogging = true)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging).UseOutboxDispatchStore(_ => inner);
        return services.Build();
    }
}
