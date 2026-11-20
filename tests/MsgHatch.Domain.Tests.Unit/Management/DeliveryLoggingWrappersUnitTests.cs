using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;
using static MsgHatch.Domain.Tests.Unit.TestTypes.DeliveryTestServices;

namespace MsgHatch.Domain.Tests.Unit.Management;

/// <summary>
/// The standard logging wrapper tests for <see cref="IDeliveryQueries"/> and <see cref="IDeliveryCommands"/> (D289). The
/// wrappers wrap the library manager, so the stores are substitutes, and each test reads only the records of the delivery
/// management, with the event ids 1800 to 1899 of the delivery manager.
/// </summary>
public sealed class DeliveryLoggingWrappersUnitTests : IAsyncDisposable
{
    private const string _address = "client1@example.com";
    private readonly DeliveryTestServices _services = new();
    private readonly ServiceProvider _provider;
    private readonly AsyncServiceScope _scope;

    public DeliveryLoggingWrappersUnitTests()
    {
        var services = TestHost.CreateServices();
        _services.AddTo(services);
        _provider = services.Build();
        _scope = _provider.CreateAsyncScope();
    }

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    private DateTime Now => _services.Now;

    public static TheoryData<string, LogLevel, int> CallRows => new()
    {
        { nameof(IDeliveryQueries.GetChannelsAsync), LogLevel.Debug, 1800 },
        { nameof(IDeliveryQueries.GetTrafficAsync), LogLevel.Debug, 1800 },
        { nameof(IDeliveryQueries.GetReceivedMessagesAsync), LogLevel.Debug, 1800 },
        { nameof(IDeliveryQueries.GetOutgoingMessagesAsync), LogLevel.Debug, 1800 },
        { nameof(IDeliveryQueries.GetOutgoingMessageAsync), LogLevel.Debug, 1800 },
        { nameof(IDeliveryQueries.GetReceivedContentAsync), LogLevel.Debug, 1800 },
        { nameof(IDeliveryQueries.GetOutgoingContentAsync), LogLevel.Debug, 1800 },
        { nameof(IDeliveryCommands.PauseChannelAsync), LogLevel.Information, 1802 },
        { nameof(IDeliveryCommands.ResumeChannelAsync), LogLevel.Information, 1802 },
        { nameof(IDeliveryCommands.RetryReceivedAsync), LogLevel.Information, 1803 },
        { nameof(IDeliveryCommands.CancelReceivedAsync), LogLevel.Information, 1803 },
        { nameof(IDeliveryCommands.RetryOutgoingAsync), LogLevel.Information, 1803 },
        { nameof(IDeliveryCommands.CancelOutgoingAsync), LogLevel.Information, 1803 },
        { nameof(IDeliveryCommands.DeleteTestDataAsync), LogLevel.Information, 1804 },
        { nameof(IDeliveryCommands.PurgeAsync), LogLevel.Information, 1804 },
    };

    public async ValueTask DisposeAsync()
    {
        await _scope.DisposeAsync();
        await _provider.DisposeAsync();
    }

    [Theory]
    [MemberData(nameof(CallRows))]
    public async Task Call_Success_LogsOneRecordWithItsLevelAndItsEventId(string call, LogLevel level, int eventId)
    {
        // Arrange
        ConfigureSuccess();

        // Act
        var error = await CallAsync(call);

        // Assert
        var record = Assert.Single(ManagementRecords());
        Assert.Equal((call, (Error?)null, level, eventId), (call, error, record.Level, record.Id.Id));
    }

    [Fact]
    public async Task Read_Success_LogsTheTenantOfTheKey()
    {
        // Arrange
        ConfigureSuccess();

        // Act
        await Queries(T1).GetOutgoingMessagesAsync(new OutboxQuery(), Ct);

        // Assert
        Assert.Equal("t1", Assert.Single(ManagementRecords()).GetStructuredStateValue("Tenant"));
    }

    [Fact]
    public async Task Read_TransientError_ReturnsTheSameErrorAndLogsAWarningWithItsEventId()
    {
        // Arrange
        var expected = new Error(Errors.Unavailable);
        _services.Outbox.QueryAsync(default!, Ct)
            .ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<MessagePage<OutgoingMessageSummary>>.ForError(expected)));

        // Act
        var page = await Queries().GetOutgoingMessagesAsync(new OutboxQuery(), Ct);

        // Assert
        var record = Assert.Single(ManagementRecords());
        Assert.Equal((expected, LogLevel.Warning, 1801), (page.Error, record.Level, record.Id.Id));
    }

    [Theory]
    [InlineData("query")]
    [InlineData("command")]
    public async Task Request_PermanentError_LogsAnErrorWithItsEventId(string kind)
    {
        // Act
        _ = kind == "query"
            ? (await Queries().GetOutgoingMessagesAsync(new OutboxQuery(Limit: 0), Ct)).Error
            : (await Commands().RetryOutgoingAsync(new OutboxQuery(), Ct)).Error;

        // Assert
        var record = Assert.Single(ManagementRecords());
        Assert.Equal((kind, LogLevel.Error, 1801), (kind, record.Level, record.Id.Id));
    }

    [Fact]
    public async Task RetryOutgoingAsync_Filters_LogsTheFiltersAndTheCountsAndNoTargetId()
    {
        // Arrange
        ConfigureSuccess();
        var query = new OutboxQuery(Status: DeliveryStatus.Failed, Channel: TestChannel.Id, Target: new(DeliveryTargetKind.User, "user-42"));

        // Act
        await Commands().RetryOutgoingAsync(query, Ct);

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
        _services.Outbox.GetAsync(default, Ct).ReturnsForAnyArgs(
            ValueTask.FromResult(ErrorOr<OutgoingMessageHistory?>.ForValue(new OutgoingMessageHistory(message, []))));

        // Act
        var content = await Queries().GetOutgoingContentAsync(message.Delivery.Id, Ct);

        // Assert
        Assert.Contains(_address, content.Value.Json, StringComparison.Ordinal);
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
        await Commands().PauseChannelAsync(TestChannel.Id, note, Ct);

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
            .UseOutboxStore((_, _) => _services.Outbox)
            .UseOutboxDispatchStore(_ => _services.OutboxDispatch)
            .UseDispatchSignal(static _ => Substitute.For<IDispatchSignal>());
        await using var provider = services.Build();
        await using var scope = provider.CreateAsyncScope();

        // Act
        await scope.ServiceProvider.GetRequiredService<IDeliveryQueries>().GetOutgoingMessagesAsync(new OutboxQuery(Limit: 0), Ct);
        await scope.ServiceProvider.GetRequiredService<IDeliveryCommands>().RetryOutgoingAsync(new OutboxQuery(), Ct);

        // Assert
        Assert.Empty(provider.LogRecords());
    }

    private IDeliveryQueries Queries(TenantKey key = default) => _scope.ServiceProvider.GetRequiredKeyedService<IDeliveryQueries>(key);

    private IDeliveryCommands Commands(TenantKey key = default) => _scope.ServiceProvider.GetRequiredKeyedService<IDeliveryCommands>(key);

    private IEnumerable<FakeLogRecord> ManagementRecords() => _provider.LogRecords().Where(static record => record.Id.Id is >= 1800 and < 1900);

    private void ConfigureSuccess()
    {
        var one = Changed(1);
        var changed = ValueTask.FromResult(ErrorOr<bool>.ForValue(true));
        var message = TestMessages.AnOutgoingMessage();
        _services.OutboxDispatch.GetDownChannelsAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelDown>>.ForValue([])));
        _services.OutboxDispatch.CountAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageCount>>.ForValue([])));
        _services.OutboxDispatch.SetChannelDownAsync(default!, Ct).ReturnsForAnyArgs(changed);
        _services.OutboxDispatch.SetChannelUpAsync(default, default, default, Ct).ReturnsForAnyArgs(changed);
        _services.OutboxDispatch.DeleteTestDataAsync(Ct).ReturnsForAnyArgs(one);
        _services.OutboxDispatch.PurgeAsync(default, default, Ct).ReturnsForAnyArgs(one);
        _services.InboxDispatch.DeleteTestDataAsync(Ct).ReturnsForAnyArgs(one);
        _services.InboxDispatch.PurgeAsync(default, default, Ct).ReturnsForAnyArgs(one);
        _services.Inbox.CountAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageCount>>.ForValue([])));
        _services.Outbox.CountAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageCount>>.ForValue([])));
        _services.Inbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(Page(null, AReceivedSummary()));
        _services.Outbox.QueryAsync(default!, Ct).ReturnsForAnyArgs(Page(null, ASummary()));
        _services.Inbox.GetAsync(default, Ct).ReturnsForAnyArgs(
            ValueTask.FromResult(ErrorOr<ReceivedMessage?>.ForValue(TestMessages.AReceivedMessage(new TestEvent("text")))));
        _services.Outbox.GetAsync(default, Ct).ReturnsForAnyArgs(
            ValueTask.FromResult(ErrorOr<OutgoingMessageHistory?>.ForValue(new OutgoingMessageHistory(message, []))));
        _services.Inbox.RetryAsync(default!, default, default, Ct).ReturnsForAnyArgs(one);
        _services.Inbox.CancelAsync(default!, default, Ct).ReturnsForAnyArgs(one);
        _services.Outbox.RetryAsync(default!, default, default, Ct).ReturnsForAnyArgs(one);
        _services.Outbox.CancelAsync(default!, default, Ct).ReturnsForAnyArgs(one);
    }

    private async Task<Error?> CallAsync(string call)
    {
        var (queries, commands) = (Queries(), Commands());
        var id = Guid.NewGuid();
        return call switch
        {
            nameof(IDeliveryQueries.GetChannelsAsync) => (await queries.GetChannelsAsync(Ct)).Error,
            nameof(IDeliveryQueries.GetTrafficAsync) => (await queries.GetTrafficAsync(new TrafficQuery(), Ct)).Error,
            nameof(IDeliveryQueries.GetReceivedMessagesAsync) => (await queries.GetReceivedMessagesAsync(new InboxQuery(), Ct)).Error,
            nameof(IDeliveryQueries.GetOutgoingMessagesAsync) => (await queries.GetOutgoingMessagesAsync(new OutboxQuery(), Ct)).Error,
            nameof(IDeliveryQueries.GetOutgoingMessageAsync) => (await queries.GetOutgoingMessageAsync(id, Ct)).Error,
            nameof(IDeliveryQueries.GetReceivedContentAsync) => (await queries.GetReceivedContentAsync(id, Ct)).Error,
            nameof(IDeliveryQueries.GetOutgoingContentAsync) => (await queries.GetOutgoingContentAsync(id, Ct)).Error,
            nameof(IDeliveryCommands.PauseChannelAsync) => await commands.PauseChannelAsync(TestChannel.Id, note: null, Ct),
            nameof(IDeliveryCommands.ResumeChannelAsync) => await commands.ResumeChannelAsync(TestChannel.Id, Ct),
            nameof(IDeliveryCommands.RetryReceivedAsync) => (await commands.RetryReceivedAsync(new InboxQuery(Status: DeliveryStatus.Failed), Ct)).Error,
            nameof(IDeliveryCommands.CancelReceivedAsync) => (await commands.CancelReceivedAsync(new InboxQuery(Status: DeliveryStatus.Pending), Ct)).Error,
            nameof(IDeliveryCommands.RetryOutgoingAsync) => (await commands.RetryOutgoingAsync(new OutboxQuery(Status: DeliveryStatus.Failed), Ct)).Error,
            nameof(IDeliveryCommands.CancelOutgoingAsync) => (await commands.CancelOutgoingAsync(new OutboxQuery(Status: DeliveryStatus.Pending), Ct)).Error,
            nameof(IDeliveryCommands.DeleteTestDataAsync) => (await commands.DeleteTestDataAsync(Ct)).Error,
            nameof(IDeliveryCommands.PurgeAsync) => (await commands.PurgeAsync(Now, 10, Ct)).Error,
            _ => throw new ArgumentOutOfRangeException(nameof(call), call, "No delivery call has this name."),
        };
    }
}
