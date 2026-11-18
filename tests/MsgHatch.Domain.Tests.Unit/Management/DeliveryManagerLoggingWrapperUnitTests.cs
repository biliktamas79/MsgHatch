using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Domain.Tests.Unit.Management;

/// <summary>
/// The standard logging wrapper tests for <see cref="IDeliveryManager"/>. The wrapper wraps the library manager, so the
/// stores are substitutes, and each test reads only the records of the delivery management, with the event ids 1800 to 1899.
/// </summary>
public sealed class DeliveryManagerLoggingWrapperUnitTests : IAsyncDisposable
{
    private const string _address = "client1@example.com";
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));
    private readonly IOutboxStore _outbox = Substitute.For<IOutboxStore>();
    private readonly IInboxStore _inbox = Substitute.For<IInboxStore>();
    private readonly IOutboxDispatchStore _outboxDispatch = Substitute.For<IOutboxDispatchStore>();
    private readonly IInboxDispatchStore _inboxDispatch = Substitute.For<IInboxDispatchStore>();
    private readonly ServiceProvider _provider;

    public DeliveryManagerLoggingWrapperUnitTests()
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch()
            .AddTestChannel()
            .UseOutboxStore((_, _) => _outbox)
            .UseOutboxDispatchStore(_ => _outboxDispatch)
            .UseInboxStore((_, _) => _inbox)
            .UseInboxDispatchStore(_ => _inboxDispatch)
            .UseDispatchSignal(static _ => Substitute.For<IDispatchSignal>());
        _provider = services.Build();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private static DateTime Now => TestHost.Start.UtcDateTime;

    public ValueTask DisposeAsync() => _provider.DisposeAsync();

    [Fact]
    public async Task Read_Success_LogsADebugRecordWithTheTenantOfTheKey()
    {
        // Arrange
        _outbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(
            ErrorOr<MessagePage<OutgoingMessageSummary>>.ForValue(new MessagePage<OutgoingMessageSummary>([], Next: null))));

        // Act
        await Manager(_t1).GetOutgoingMessagesAsync(new OutboxQuery(), Ct);

        // Assert
        var record = Assert.Single(ManagementRecords());
        Assert.Equal((LogLevel.Debug, 1800, "t1"), (record.Level, record.Id.Id, record.GetStructuredStateValue("Tenant")));
    }

    [Fact]
    public async Task Read_TransientError_ReturnsTheSameErrorAndLogsAWarningWithItsEventId()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        _outbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<MessagePage<OutgoingMessageSummary>>.ForError(expected)));

        // Act
        var page = await Manager().GetOutgoingMessagesAsync(new OutboxQuery(), Ct);

        // Assert
        var record = Assert.Single(ManagementRecords());
        Assert.Equal((expected, LogLevel.Warning, 1801), (page.Error, record.Level, record.Id.Id));
    }

    [Fact]
    public async Task Request_PermanentError_LogsAnErrorWithItsEventId()
    {
        // Act
        await Manager().GetOutgoingMessagesAsync(new OutboxQuery(Limit: 0), Ct);

        // Assert
        var record = Assert.Single(ManagementRecords());
        Assert.Equal((LogLevel.Error, 1801), (record.Level, record.Id.Id));
    }

    public static TheoryData<string, int> ChangeRows => new()
    {
        { "pause", 1802 },
        { "resume", 1802 },
        { "retry", 1803 },
        { "cancel", 1803 },
        { "delete test data", 1804 },
        { "purge", 1804 },
    };

    [Theory]
    [MemberData(nameof(ChangeRows))]
    public async Task Change_Success_LogsAnInformationRecordWithItsEventId(string change, int eventId)
    {
        // Arrange
        ConfigureSuccess();

        // Act
        await ChangeAsync(change);

        // Assert
        var record = Assert.Single(ManagementRecords());
        Assert.Equal((LogLevel.Information, eventId), (record.Level, record.Id.Id));
    }

    [Fact]
    public async Task RetryOutgoingAsync_Filters_LogsTheFiltersAndTheCountsAndNoTargetId()
    {
        // Arrange
        ConfigureSuccess();
        var query = new OutboxQuery(Status: DeliveryStatus.Failed, Channel: TestChannel.Id, Target: new(DeliveryTargetKind.User, "user-42"));

        // Act
        await Manager().RetryOutgoingAsync(query, Ct);

        // Assert
        var record = Assert.Single(ManagementRecords());
        Assert.Equal(
            ("status: Failed, channel: Test, target: User", "1", "1"),
            (record.GetStructuredStateValue("Filters"), record.GetStructuredStateValue("Matched"), record.GetStructuredStateValue("Changed")));
        Assert.False(record.ContainsText("user-42"));
    }

    [Fact]
    public async Task GetOutgoingContentAsync_TestEnvelope_NoLogRecordContainsTheAddress()
    {
        // Arrange
        var message = TestMessages.AnOutgoingMessage(new TestEnvelope(_address), flags: MessageFlags.Test);
        _outbox.GetAsync(default, Ct).ReturnsForAnyArgs(
            ValueTask.FromResult(ErrorOr<OutgoingMessageHistory?>.ForValue(new OutgoingMessageHistory(message, []))));

        // Act
        await Manager().GetOutgoingContentAsync(message.Delivery.Id, Ct);

        // Assert
        Assert.NotEmpty(_provider.LogRecords());
        Assert.DoesNotContain(_provider.LogRecords(), static record => record.ContainsText(_address));
    }

    [Fact]
    public async Task PauseChannelAsync_Note_NoLogRecordContainsTheNote()
    {
        // Arrange
        const string note = "call client1@example.com";
        ConfigureSuccess();

        // Act
        await Manager().PauseChannelAsync(TestChannel.Id, note, Ct);

        // Assert
        Assert.DoesNotContain(_provider.LogRecords(), static record => record.ContainsText(note));
    }

    [Fact]
    public async Task AddMsgHatch_EnableLoggingFalse_LogsNoRecordOfTheDeliveryManagement()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.EnableLogging = false)
            .AddTestChannel()
            .UseOutboxStore((_, _) => _outbox)
            .UseOutboxDispatchStore(_ => _outboxDispatch)
            .UseDispatchSignal(static _ => Substitute.For<IDispatchSignal>());
        await using var provider = services.Build();

        // Act
        await provider.GetRequiredService<IDeliveryManager>().GetOutgoingMessagesAsync(new OutboxQuery(Limit: 0), Ct);

        // Assert
        Assert.Empty(provider.LogRecords());
    }

    private IDeliveryManager Manager(TenantKey key = default) => _provider.GetRequiredKeyedService<IDeliveryManager>(key);

    private IEnumerable<FakeLogRecord> ManagementRecords() => _provider.LogRecords().Where(static record => record.Id.Id is >= 1800 and < 1900);

    private void ConfigureSuccess()
    {
        var one = ValueTask.FromResult(ErrorOr<int>.ForValue(1));
        var changed = ValueTask.FromResult(ErrorOr<bool>.ForValue(true));
        _outboxDispatch.SetChannelDownAsync(default!, Ct).ReturnsForAnyArgs(changed);
        _outboxDispatch.SetChannelUpAsync(default, default, default, Ct).ReturnsForAnyArgs(changed);
        _outboxDispatch.DeleteTestDataAsync(Ct).ReturnsForAnyArgs(one);
        _outboxDispatch.PurgeAsync(default, default, Ct).ReturnsForAnyArgs(one);
        _inboxDispatch.DeleteTestDataAsync(Ct).ReturnsForAnyArgs(one);
        _inboxDispatch.PurgeAsync(default, default, Ct).ReturnsForAnyArgs(one);
        _outbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<MessagePage<OutgoingMessageSummary>>.ForValue(
            new MessagePage<OutgoingMessageSummary>([OutgoingMessageSummary.Of(TestMessages.AnOutgoingMessage())], Next: null))));
        _outbox.RetryAsync(default!, default, default, Ct).ReturnsForAnyArgs(one);
        _outbox.CancelAsync(default!, default, Ct).ReturnsForAnyArgs(one);
    }

    private async Task ChangeAsync(string change)
    {
        var manager = Manager();
        var failed = new OutboxQuery(Status: DeliveryStatus.Failed);
        _ = change switch
        {
            "pause" => await manager.PauseChannelAsync(TestChannel.Id, note: null, Ct),
            "resume" => await manager.ResumeChannelAsync(TestChannel.Id, Ct),
            "retry" => (await manager.RetryOutgoingAsync(failed, Ct)).Error,
            "cancel" => (await manager.CancelOutgoingAsync(failed, Ct)).Error,
            "delete test data" => (await manager.DeleteTestDataAsync(Ct)).Error,
            _ => (await manager.PurgeAsync(Now, 10, Ct)).Error,
        };
    }
}
