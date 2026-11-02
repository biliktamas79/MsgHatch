using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;
using static MsgHatch.Domain.Tests.Unit.TestTypes.OutboxDispatchDoubles;

namespace MsgHatch.Domain.Tests.Unit.Dispatch;

/// <summary>The shared down state of a channel in the outbox loops (D86, D106), with an error threshold of 3 and a check interval of 30 seconds.</summary>
public sealed partial class OutboxDispatcherUnitTests
{
    public static TheoryData<bool, int> SetChannelDownResultRows => new()
    {
        { true, 1 },
        { false, 0 },
    };

    [Fact]
    public async Task DispatchPendingAsync_ThreeTransientErrorsInARow_SetsTheChannelDownOneTimeWithTheLastErrorAndANextCheck()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage(), AMessage(), AMessage()));
        var transport = TestTransports.For(TestChannel.Id);
        transport.SendAsync(default!, default, Ct).ReturnsForAnyArgs(
            ValueTask.FromResult<ErrorOr<PublishReceipt>>(new Error(Errors.Unavailable)),
            ValueTask.FromResult<ErrorOr<PublishReceipt>>(new Error(Errors.Unavailable)),
            ValueTask.FromResult<ErrorOr<PublishReceipt>>(new Error(Errors.RateLimited)));
        await using var provider = BuildWithTheDownOptions(transport);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var down = Assert.Single(SetDownStates());
        Assert.Equal(
            (TestChannel.Id, ChannelDownReason.Errors, (int?)429, "RateLimited", (DateTime?)_doubles.Now.AddSeconds(30)),
            (down.Channel, down.Reason, down.ErrorCode, down.ErrorName, down.NextCheckAtUtc));
    }

    [Fact]
    public async Task DispatchPendingAsync_TwoTransientErrorsAndThenASuccess_DoesNotSetTheChannelDown()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage(), AMessage(), AMessage(), AMessage(), AMessage()));
        var transport = TestTransports.For(TestChannel.Id);
        transport.SendAsync(default!, default, Ct).ReturnsForAnyArgs(
            ValueTask.FromResult<ErrorOr<PublishReceipt>>(new Error(Errors.Unavailable)),
            ValueTask.FromResult<ErrorOr<PublishReceipt>>(new Error(Errors.Unavailable)),
            ValueTask.FromResult<ErrorOr<PublishReceipt>>(new PublishReceipt("provider-1")),
            ValueTask.FromResult<ErrorOr<PublishReceipt>>(new Error(Errors.Unavailable)),
            ValueTask.FromResult<ErrorOr<PublishReceipt>>(new Error(Errors.Unavailable)));
        await using var provider = BuildWithTheDownOptions(transport);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Empty(SetDownStates());
    }

    [Fact]
    public async Task DispatchPendingAsync_PermanentErrorBetweenTransientErrors_SetsTheCountToZero()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage(), AMessage(), AMessage(), AMessage(), AMessage()));
        var transport = TestTransports.For(TestChannel.Id);
        transport.SendAsync(default!, default, Ct).ReturnsForAnyArgs(
            ValueTask.FromResult<ErrorOr<PublishReceipt>>(new Error(Errors.Unavailable)),
            ValueTask.FromResult<ErrorOr<PublishReceipt>>(new Error(Errors.Unavailable)),
            ValueTask.FromResult<ErrorOr<PublishReceipt>>(new Error(Errors.Rejected)),
            ValueTask.FromResult<ErrorOr<PublishReceipt>>(new Error(Errors.Unavailable)),
            ValueTask.FromResult<ErrorOr<PublishReceipt>>(new Error(Errors.Unavailable)));
        await using var provider = BuildWithTheDownOptions(transport);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Empty(SetDownStates());
    }

    [Fact]
    public async Task DispatchPendingAsync_ExpiredTryBetweenTransientErrors_DoesNotChangeTheCount()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage(), AMessage(), AMessage(expiresAtUtc: _doubles.Now), AMessage()));
        await using var provider = BuildWithTheDownOptions(TestTransports.For(TestChannel.Id, new Error(Errors.Unavailable)));

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Equal(503, Assert.Single(SetDownStates()).ErrorCode); // the fourth try, not the expired third try, reaches the threshold
    }

    [Theory]
    [MemberData(nameof(SetChannelDownResultRows))]
    public async Task DispatchPendingAsync_SetChannelDownResult_LogsAnErrorOnlyWhenTheCallWroteTheState(bool wrote, int expectedRecords)
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage(), AMessage(), AMessage()));
        _doubles.Store.SetChannelDownAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<bool>.ForValue(wrote)));
        await using var provider = BuildWithTheDownOptions(TestTransports.For(TestChannel.Id, new Error(Errors.Unavailable)));

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Equal(expectedRecords, provider.LogRecords().Count(static r => r.Id.Id == 1402 && r.Level == LogLevel.Error));
    }

    [Fact]
    public async Task DispatchPendingAsync_MessageInTheBatchWhileItsChannelGoesDown_GetsNoSendAndNoTryAndTheSecondChannelStillSends()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage(), AMessage(), AMessage(), AMessage()));
        var ofTheSecondChannel = AMessage(TestChannel.SecondId);
        _doubles.ClaimsGive(TestChannel.SecondId, Claim(ofTheSecondChannel));
        var transport = TestTransports.For(TestChannel.Id, new Error(Errors.Unavailable));
        await using var provider = BuildWithTheDownOptions(transport, TestTransports.For(TestChannel.SecondId));

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        await transport.ReceivedWithAnyArgs(3).SendAsync(default!, default, Ct);
        RecordCall[] records = [await _doubles.NextRecordAsync(), await _doubles.NextRecordAsync(), await _doubles.NextRecordAsync(), await _doubles.NextRecordAsync()];
        Assert.Equal([TestChannel.Id, TestChannel.Id, TestChannel.Id, TestChannel.SecondId], records.Select(static r => r.Message.Channel));
        Assert.False(_doubles.HasMoreRecords);
    }

    [Fact]
    public async Task DispatchPendingAsync_DueCheckOfADownChannel_RunsTheCheckBeforeItsClaim()
    {
        // Arrange
        _doubles.Store.GetDownChannelsAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelDown>>.ForValue([ADownState(_doubles.Now)])));
        _doubles.Store.TryStartChannelCheckAsync(default, default, default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<bool>.ForValue(true)));
        await using var provider = BuildWithTheDownOptions(TestTransports.For(TestChannel.Id));

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Received.InOrder(() =>
        {
            _doubles.Store.TryStartChannelCheckAsync(TestChannel.Id, _doubles.Now, _doubles.Now.AddSeconds(30), Arg.Any<CancellationToken>());
            _doubles.Store.SetChannelUpAsync(TestChannel.Id, ChannelDownReason.Errors, Arg.Any<CancellationToken>());
            _doubles.Store.ClaimDueAsync(TestChannel.Id, Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
        });
    }

    [Fact]
    public async Task DispatchPendingAsync_CheckStartThatFails_DoesNotCheckTheHealth()
    {
        // Arrange
        _doubles.Store.GetDownChannelsAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelDown>>.ForValue([ADownState(_doubles.Now)])));
        _doubles.Store.TryStartChannelCheckAsync(default, default, default, Ct).ReturnsForAnyArgs(ValueTask.FromResult<ErrorOr<bool>>(new Error(Errors.Unavailable)));
        var transport = TestTransports.For(TestChannel.Id);
        await using var provider = BuildWithTheDownOptions(transport);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        await _doubles.Store.Received(1).TryStartChannelCheckAsync(TestChannel.Id, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await transport.DidNotReceive().CheckHealthAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_SetChannelUpThatFails_LogsNoChannelUp()
    {
        // Arrange
        _doubles.Store.GetDownChannelsAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelDown>>.ForValue([ADownState(_doubles.Now)])));
        _doubles.Store.TryStartChannelCheckAsync(default, default, default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<bool>.ForValue(true)));
        _doubles.Store.SetChannelUpAsync(default, default, Ct).ReturnsForAnyArgs(ValueTask.FromResult<ErrorOr<bool>>(new Error(Errors.Unavailable)));
        await using var provider = BuildWithTheDownOptions(TestTransports.For(TestChannel.Id));

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        await _doubles.Store.Received(1).SetChannelUpAsync(TestChannel.Id, ChannelDownReason.Errors, Arg.Any<CancellationToken>());
        Assert.DoesNotContain(provider.LogRecords(), static r => r.Id.Id == 1403);
    }

    [Fact]
    public async Task RunAsync_DownStateOfTheReasonErrors_StartsTheCheckWhenTheTimeReachesTheNextCheckAndThenMarksTheChannelUp()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, ClaimDown(ADownState(_doubles.Now.AddSeconds(30))));
        var checkStarted = new TaskCompletionSource();
        _doubles.Store.TryStartChannelCheckAsync(default, default, default, Ct).ReturnsForAnyArgs(_ =>
        {
            checkStarted.TrySetResult();
            return ValueTask.FromResult(ErrorOr<bool>.ForValue(true));
        });
        var transport = TestTransports.For(TestChannel.Id);
        await using var provider = BuildWithTheDownOptions(transport);
        await using var running = new RunningLoop(provider.GetRequiredService<IOutboxDispatcher>().RunAsync);
        await _doubles.NextClaimAsync();
        var wait = await _doubles.Time.NextWaitAsync();
        Assert.False(checkStarted.Task.IsCompleted);

        // Act
        _doubles.Time.Advance(wait);

        // Assert
        await _doubles.NextClaimAsync();
        Assert.Equal(TimeSpan.FromSeconds(30), wait);
        await transport.Received(1).CheckHealthAsync(Arg.Any<CancellationToken>());
        await _doubles.Store.Received(1).SetChannelUpAsync(TestChannel.Id, ChannelDownReason.Errors, Arg.Any<CancellationToken>());
        Assert.Equal(LogLevel.Information, Assert.Single(provider.LogRecords(), static r => r.Id.Id == 1403).Level);
    }

    [Fact]
    public async Task RunAsync_CheckThatAnotherInstanceRuns_DoesNotCheckTheHealth()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, ClaimDown(ADownState(_doubles.Now.AddSeconds(30))));
        var transport = TestTransports.For(TestChannel.Id);
        await using var provider = BuildWithTheDownOptions(transport);
        await using var running = new RunningLoop(provider.GetRequiredService<IOutboxDispatcher>().RunAsync);
        await _doubles.NextClaimAsync();

        // Act
        _doubles.Time.Advance(await _doubles.Time.NextWaitAsync());

        // Assert
        await _doubles.NextClaimAsync();
        await _doubles.Store.Received(1).TryStartChannelCheckAsync(TestChannel.Id, Arg.Any<DateTime>(), Arg.Any<DateTime>(), Arg.Any<CancellationToken>());
        await transport.DidNotReceive().CheckHealthAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_DownStateOfTheReasonOperator_ChecksNothingWhileAnHourPassesAndClaimsAtOnceOnASignal()
    {
        // Arrange
        var paused = new ChannelDown(TestChannel.Id, ChannelDownReason.Operator, _doubles.Now, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, "maintenance");
        _doubles.ClaimsGive(TestChannel.Id, ClaimDown(paused));
        await using var provider = BuildWithTheDownOptions(TestTransports.For(TestChannel.Id));
        await using var running = new RunningLoop(provider.GetRequiredService<IOutboxDispatcher>().RunAsync);
        await _doubles.NextClaimAsync();
        _doubles.Time.Advance(TimeSpan.FromHours(1));
        Assert.False(_doubles.HasMoreClaims || _doubles.Time.HasMoreWaits);

        // Act
        await provider.GetRequiredService<IDispatchSignal>().NotifyAsync(DispatchTopic.ForChannel(TestChannel.Id), Ct);

        // Assert
        Assert.Equal(_doubles.Now, (await _doubles.NextClaimAsync()).NowUtc);
        await _doubles.Store.DidNotReceiveWithAnyArgs().TryStartChannelCheckAsync(default, default, default, Ct);
    }

    [Fact]
    public async Task RunAsync_ChannelUpAgainAfterADownState_SetsItDownAgainAfterOneTransientError()
    {
        // Arrange
        _doubles.ClaimsGive(
            TestChannel.Id, ClaimDown(ADownState(_doubles.Now.AddSeconds(30))), Claim(AMessage()));
        var setDown = new TaskCompletionSource<ChannelDown>(TaskCreationOptions.RunContinuationsAsynchronously);
        _doubles.Store.SetChannelDownAsync(default!, Ct).ReturnsForAnyArgs(call =>
        {
            setDown.TrySetResult(call.ArgAt<ChannelDown>(0));
            return ValueTask.FromResult(ErrorOr<bool>.ForValue(true));
        });
        await using var provider = BuildWithTheDownOptions(TestTransports.For(TestChannel.Id, new Error(Errors.Unavailable)));
        await using var running = new RunningLoop(provider.GetRequiredService<IOutboxDispatcher>().RunAsync);
        await _doubles.NextClaimAsync();

        // Act
        await provider.GetRequiredService<IDispatchSignal>().NotifyAsync(DispatchTopic.ForChannel(TestChannel.Id), Ct);

        // Assert
        var down = await setDown.Task.WaitAsync(TestHost.HangGuard, TimeProvider.System, Ct);
        Assert.Equal((TestChannel.Id, ChannelDownReason.Errors), (down.Channel, down.Reason));
        Assert.Single(SetDownStates());
    }

    [Fact]
    public async Task DispatchPendingAsync_ExpiryOfFiveMinutesWhileTheChannelIsDownForTwoHours_RecordsAnExpiredTryAfterTheChannelIsUpAndSendsNothing()
    {
        // Arrange
        var store = new InMemoryOutboxStore();
        var message = TestMessages.AnOutgoingMessage(delivery: TestMessages.ADeliveryState() with { ExpiresAtUtc = _doubles.Now.AddMinutes(5) });
        await store.For(TenantKey.None).AddAsync([message], Ct);
        await store.SetChannelDownAsync(
            new ChannelDown(TestChannel.Id, ChannelDownReason.Operator, _doubles.Now, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, Note: null), Ct);
        var transport = TestTransports.For(TestChannel.Id);
        var services = TestHost.CreateServices(_doubles.Time);
        services.AddMsgHatch().AddTestChannel().UseOutboxStore((_, key) => store.For(key)).UseOutboxDispatchStore(_ => store).UseTransport(_ => transport);
        await using var provider = services.Build();
        var dispatcher = provider.GetRequiredService<IOutboxDispatcher>();
        _doubles.Time.Advance(TimeSpan.FromHours(2));
        var whileDown = await dispatcher.DispatchPendingAsync(10, Ct);
        await store.SetChannelUpAsync(TestChannel.Id, reason: null, Ct);

        // Act
        await dispatcher.DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Equal(0, whileDown.Value.Claimed);
        Assert.Equal(PublishOutcome.Expired, Assert.Single(store.GetTries(message.Delivery.Id)).Outcome);
        await transport.DidNotReceiveWithAnyArgs().SendAsync(default!, default, Ct);
    }

    private ChannelDown ADownState(DateTime nextCheckAtUtc) =>
        new(TestChannel.Id, ChannelDownReason.Errors, _doubles.Now, nextCheckAtUtc, 503, "Unavailable", Note: null);

    private ChannelDown[] SetDownStates() =>
        [.. _doubles.Store.ReceivedCalls()
            .Where(static call => call.GetMethodInfo().Name == nameof(IOutboxDispatchStore.SetChannelDownAsync))
            .Select(static call => (ChannelDown)call.GetArguments()[0]!)];

    private Microsoft.Extensions.DependencyInjection.ServiceProvider BuildWithTheDownOptions(params ITransport[] transports) =>
        _doubles.Build(
            static o => (o.EnablePolling, o.MaxDegreeOfParallelism) = (false, 1),
            static b => b
                .ConfigureChannelDown(TestChannel.Id, static o => (o.ErrorThreshold, o.CheckInterval) = (3, TimeSpan.FromSeconds(30)))
                .ConfigureChannelDown(TestChannel.SecondId, static o => (o.ErrorThreshold, o.CheckInterval) = (3, TimeSpan.FromSeconds(30))),
            transports);
}
