using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Inbox;

/// <summary>The standard logging wrapper tests for <see cref="IInboxDispatchStore"/>. It is not a tenant service, so it logs the tenant of a message.</summary>
public sealed class InboxDispatchStoreLoggingWrapperUnitTests
{
    private const string _eventText = "client1@example.com";

    public static TheoryData<string, int> FailedEventRows => new()
    {
        { nameof(IInboxDispatchStore.ClaimDueAsync), 1203 },
        { nameof(IInboxDispatchStore.CompleteAsync), 1205 },
        { nameof(IInboxDispatchStore.FailAsync), 1205 },
        { nameof(IInboxDispatchStore.DeleteTestDataAsync), 1207 },
        { nameof(IInboxDispatchStore.PurgeAsync), 1213 },
    };

    public static TheoryData<string, int> SucceededEventRows => new()
    {
        { nameof(IInboxDispatchStore.ClaimDueAsync), 1202 },
        { nameof(IInboxDispatchStore.CompleteAsync), 1204 },
        { nameof(IInboxDispatchStore.FailAsync), 1204 },
        { nameof(IInboxDispatchStore.DeleteTestDataAsync), 1206 },
        { nameof(IInboxDispatchStore.PurgeAsync), 1212 },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(FailedEventRows))]
    public async Task Member_TransientError_ReturnsTheSameErrorAndLogsAWarningWithItsEventId(string member, int eventId)
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        var inner = AStoreOf(expected);
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredService<IInboxDispatchStore>();
        var message = AMessage();

        // Act
        var error = await CallAsync(store, member, message);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((expected, LogLevel.Warning, eventId), (error, record.Level, record.Id.Id));
        AssertForwardedOnce(inner, member, message);
    }

    [Theory]
    [MemberData(nameof(FailedEventRows))]
    public async Task Member_PermanentError_LogsAnErrorWithItsEventId(string member, int eventId)
    {
        // Arrange
        var inner = AStoreOf(new Error(Errors.ValidationFailed));
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredService<IInboxDispatchStore>();
        var message = AMessage();

        // Act
        await CallAsync(store, member, message);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal((LogLevel.Error, eventId), (record.Level, record.Id.Id));
        AssertForwardedOnce(inner, member, message);
    }

    [Theory]
    [MemberData(nameof(SucceededEventRows))]
    public async Task Member_Success_LogsADebugRecordWithItsEventId(string member, int eventId)
    {
        // Arrange
        var inner = AStoreOf(error: null);
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredService<IInboxDispatchStore>();
        var message = AMessage();

        // Act
        var error = await CallAsync(store, member, message);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(((Error?)null, LogLevel.Debug, eventId), (error, record.Level, record.Id.Id));
        AssertForwardedOnce(inner, member, message);
    }

    [Fact]
    public async Task ClaimDueAsync_InnerResult_ReturnsTheResultUnchanged()
    {
        // Arrange
        var claimed = new ClaimResult<ReceivedMessage>([AMessage()], TestMessages.CreatedAtUtc);
        var inner = AStoreOf(error: null);
        inner.ClaimDueAsync(default, default, default, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<ClaimResult<ReceivedMessage>>.ForValue(claimed)));
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredService<IInboxDispatchStore>();

        // Act
        var result = await store.ClaimDueAsync(1, TestMessages.CreatedAtUtc, TimeSpan.FromSeconds(30), Ct);

        // Assert
        Assert.Same(claimed, result.Value);
        _ = inner.Received(1).ClaimDueAsync(1, TestMessages.CreatedAtUtc, TimeSpan.FromSeconds(30), Ct);
    }

    [Fact]
    public async Task PurgeAsync_InnerCount_ReturnsTheCountUnchanged()
    {
        // Arrange
        var inner = AStoreOf(error: null);
        inner.PurgeAsync(default, default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<int>.ForValue(7)));
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredService<IInboxDispatchStore>();

        // Act
        var purged = await store.PurgeAsync(TestMessages.CreatedAtUtc, 10, Ct);

        // Assert
        Assert.Equal(7, purged.Value);
        _ = inner.Received(1).PurgeAsync(TestMessages.CreatedAtUtc, 10, Ct);
    }

    [Fact]
    public async Task CompleteAsync_TestMessageOfATenant_LogsTheTenantAndTheTestFlagAndNoEvent()
    {
        // Arrange
        var inner = AStoreOf(error: null);
        await using var provider = BuildWith(inner);
        var store = provider.GetRequiredService<IInboxDispatchStore>();
        var message = TestMessages.AReceivedMessage(new TestEvent(_eventText), new TenantId("t1"), flags: MessageFlags.Test);

        // Act
        await store.CompleteAsync(message, TestMessages.CreatedAtUtc, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(("t1", "Test"), (record.GetStructuredStateValue("Tenant"), record.GetStructuredStateValue("MessageFlags")));
        Assert.False(record.ContainsText(_eventText));
        _ = inner.Received(1).CompleteAsync(message, TestMessages.CreatedAtUtc, Ct);
    }

    [Fact]
    public async Task UseInboxDispatchStore_EnableLoggingFalse_RegistersNoLoggingWrapper()
    {
        // Arrange
        var inner = AStoreOf(error: null);
        await using var provider = BuildWith(inner, enableLogging: false);

        // Act
        var store = provider.GetRequiredService<IInboxDispatchStore>();

        // Assert
        Assert.Same(inner, store);
    }

    private static ReceivedMessage<TestEvent> AMessage() => TestMessages.AReceivedMessage(new TestEvent("text"));

    private static async Task<Error?> CallAsync(IInboxDispatchStore store, string member, ReceivedMessage message)
    {
        var now = TestMessages.CreatedAtUtc;
        return member switch
        {
            nameof(IInboxDispatchStore.ClaimDueAsync) => (await store.ClaimDueAsync(1, now, TimeSpan.FromSeconds(30), Ct)).Error,
            nameof(IInboxDispatchStore.CompleteAsync) => await store.CompleteAsync(message, now, Ct),
            nameof(IInboxDispatchStore.FailAsync) => await store.FailAsync(message, "Unavailable", now, now.AddSeconds(5), Ct),
            nameof(IInboxDispatchStore.DeleteTestDataAsync) => (await store.DeleteTestDataAsync(Ct)).Error,
            _ => (await store.PurgeAsync(now, 10, Ct)).Error,
        };
    }

    /// <summary>Asserts that the wrapper called the member of <see cref="CallAsync"/> on the inner store one time with the same arguments, and no other member.</summary>
    private static void AssertForwardedOnce(IInboxDispatchStore inner, string member, ReceivedMessage message)
    {
        var now = TestMessages.CreatedAtUtc;
        Assert.Single(inner.ReceivedCalls());
        switch (member)
        {
            case nameof(IInboxDispatchStore.ClaimDueAsync):
                _ = inner.Received(1).ClaimDueAsync(1, now, TimeSpan.FromSeconds(30), Ct);
                break;
            case nameof(IInboxDispatchStore.CompleteAsync):
                _ = inner.Received(1).CompleteAsync(message, now, Ct);
                break;
            case nameof(IInboxDispatchStore.FailAsync):
                _ = inner.Received(1).FailAsync(message, "Unavailable", now, now.AddSeconds(5), Ct);
                break;
            case nameof(IInboxDispatchStore.DeleteTestDataAsync):
                _ = inner.Received(1).DeleteTestDataAsync(Ct);
                break;
            default:
                _ = inner.Received(1).PurgeAsync(now, 10, Ct);
                break;
        }
    }

    private static IInboxDispatchStore AStoreOf(Error? error)
    {
        var store = Substitute.For<IInboxDispatchStore>();
        store.ClaimDueAsync(default, default, default, default).ReturnsForAnyArgs(ValueTask.FromResult(error is null
            ? ErrorOr<ClaimResult<ReceivedMessage>>.ForValue(new ClaimResult<ReceivedMessage>([], null))
            : ErrorOr<ClaimResult<ReceivedMessage>>.ForError(error)));
        store.CompleteAsync(default!, default, default).ReturnsForAnyArgs(ValueTask.FromResult(error));
        store.FailAsync(default!, default!, default, default, default).ReturnsForAnyArgs(ValueTask.FromResult(error));
        store.DeleteTestDataAsync(default).ReturnsForAnyArgs(ValueTask.FromResult(
            error is null ? ErrorOr<int>.ForValue(0) : ErrorOr<int>.ForError(error)));
        store.PurgeAsync(default, default, default).ReturnsForAnyArgs(ValueTask.FromResult(
            error is null ? ErrorOr<int>.ForValue(0) : ErrorOr<int>.ForError(error)));
        return store;
    }

    private static ServiceProvider BuildWith(IInboxDispatchStore inner, bool enableLogging = true)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging).UseInboxDispatchStore(_ => inner);
        return services.Build();
    }
}
