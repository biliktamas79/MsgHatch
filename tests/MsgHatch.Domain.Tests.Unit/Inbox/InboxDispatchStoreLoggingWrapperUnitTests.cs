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
    };

    public static TheoryData<string, int> SucceededEventRows => new()
    {
        { nameof(IInboxDispatchStore.ClaimDueAsync), 1202 },
        { nameof(IInboxDispatchStore.CompleteAsync), 1204 },
        { nameof(IInboxDispatchStore.FailAsync), 1204 },
        { nameof(IInboxDispatchStore.DeleteTestDataAsync), 1206 },
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(FailedEventRows))]
    public async Task Member_TransientError_ReturnsTheSameErrorAndLogsAWarningWithItsEventId(string member, int eventId)
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        await using var provider = BuildWith(AStoreOf(expected));
        var store = provider.GetRequiredService<IInboxDispatchStore>();

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
        var store = provider.GetRequiredService<IInboxDispatchStore>();

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
        var store = provider.GetRequiredService<IInboxDispatchStore>();

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
    }

    [Fact]
    public async Task CompleteAsync_TestMessageOfATenant_LogsTheTenantAndTheTestFlagAndNoEvent()
    {
        // Arrange
        await using var provider = BuildWith(AStoreOf(error: null));
        var store = provider.GetRequiredService<IInboxDispatchStore>();
        var message = TestMessages.AReceivedMessage(new TestEvent(_eventText), new TenantId("t1"), flags: MessageFlags.Test);

        // Act
        await store.CompleteAsync(message, TestMessages.CreatedAtUtc, Ct);

        // Assert
        var record = Assert.Single(provider.LogRecords());
        Assert.Equal(("t1", "Test"), (record.GetStructuredStateValue("Tenant"), record.GetStructuredStateValue("MessageFlags")));
        Assert.False(record.ContainsText(_eventText));
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

    private static async Task<Error?> CallAsync(IInboxDispatchStore store, string member)
    {
        var now = TestMessages.CreatedAtUtc;
        return member switch
        {
            nameof(IInboxDispatchStore.ClaimDueAsync) => (await store.ClaimDueAsync(1, now, TimeSpan.FromSeconds(30), Ct)).Error,
            nameof(IInboxDispatchStore.CompleteAsync) => await store.CompleteAsync(AMessage(), now, Ct),
            nameof(IInboxDispatchStore.FailAsync) => await store.FailAsync(AMessage(), "Unavailable", now, now.AddSeconds(5), Ct),
            _ => (await store.DeleteTestDataAsync(Ct)).Error,
        };
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
        return store;
    }

    private static ServiceProvider BuildWith(IInboxDispatchStore inner, bool enableLogging = true)
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch(o => o.EnableLogging = enableLogging).UseInboxDispatchStore(_ => inner);
        return services.Build();
    }
}
