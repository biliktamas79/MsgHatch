using Microsoft.Extensions.Logging;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;
using static MsgHatch.Domain.Tests.Unit.TestTypes.TestRoutes;

namespace MsgHatch.Domain.Tests.Unit.Pipeline;

public sealed partial class NotificationServiceUnitTests
{
    [Fact]
    public async Task ProcessAsync_StoredMessagesOnTwoChannels_NotifiesTheTopicOfEachChannelOneTime()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1"), ARoute("u2"), ARoute("u3", channel: TestChannel.SecondId, settings: new OtherTestChannelSettings())];
        var signal = Substitute.For<IDispatchSignal>();
        await using var provider = _doubles.Build(_time, more: b => b.UseDispatchSignal(_ => signal));

        // Act
        await ProcessAsync(provider, AContext());

        // Assert
        Assert.Equal([DispatchTopic.ForChannel(TestChannel.Id), DispatchTopic.ForChannel(TestChannel.SecondId)], NotifiedTopics(signal));
    }

    [Fact]
    public async Task ProcessAsync_NoStoredMessage_NotifiesNoTopic()
    {
        // Arrange
        _doubles.Routes = [];
        var signal = Substitute.For<IDispatchSignal>();
        await using var provider = _doubles.Build(_time, more: b => b.UseDispatchSignal(_ => signal));

        // Act
        await ProcessAsync(provider, AContext());

        // Assert
        Assert.Empty(NotifiedTopics(signal));
    }

    [Fact]
    public async Task ProcessAsync_OutboxStoreError_NotifiesNoTopic()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1")];
        _doubles.Store(TenantKey.None).AddAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(new Error(Errors.Unavailable)));
        var signal = Substitute.For<IDispatchSignal>();
        await using var provider = _doubles.Build(_time, more: b => b.UseDispatchSignal(_ => signal));

        // Act
        await ProcessAsync(provider, AContext());

        // Assert
        Assert.Empty(NotifiedTopics(signal));
    }

    [Fact]
    public async Task ProcessAsync_NotifyError_StoresTheMessageAndLogsAWarning()
    {
        // Arrange
        _doubles.Routes = [ARoute("u1")];
        var signal = Substitute.For<IDispatchSignal>();
        signal.NotifyAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(new Error(Errors.Unavailable)));
        await using var provider = _doubles.Build(_time, more: b => b.UseDispatchSignal(_ => signal));

        // Act
        var result = await ProcessAsync(provider, AContext());

        // Assert
        Assert.Equal(1, result.Value.Stored);
        Assert.Equal(LogLevel.Warning, Assert.Single(provider.LogRecords(), static r => r.Id.Id == 1010).Level);
    }

    [Fact]
    public async Task AcceptAsync_StoredEvent_NotifiesTheInboxTopic()
    {
        // Arrange
        var signal = Substitute.For<IDispatchSignal>();
        await using var provider = _doubles.Build(_time, more: b => b.UseDispatchSignal(_ => signal));

        // Act
        await AcceptAsync(provider, new TestEvent("text"), AContext());

        // Assert
        Assert.Equal([DispatchTopic.Inbox], NotifiedTopics(signal));
    }

    [Fact]
    public async Task AcceptAsync_InboxStoreError_NotifiesNoTopic()
    {
        // Arrange
        _doubles.Inbox(TenantKey.None).AddAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(new Error(Errors.Unavailable)));
        var signal = Substitute.For<IDispatchSignal>();
        await using var provider = _doubles.Build(_time, more: b => b.UseDispatchSignal(_ => signal));

        // Act
        await AcceptAsync(provider, new TestEvent("text"), AContext());

        // Assert
        Assert.Empty(NotifiedTopics(signal));
    }

    private static DispatchTopic[] NotifiedTopics(IDispatchSignal signal) =>
        [.. signal.ReceivedCalls()
            .Where(static call => call.GetMethodInfo().Name == nameof(IDispatchSignal.NotifyAsync))
            .Select(static call => (DispatchTopic)call.GetArguments()[0]!)];
}
