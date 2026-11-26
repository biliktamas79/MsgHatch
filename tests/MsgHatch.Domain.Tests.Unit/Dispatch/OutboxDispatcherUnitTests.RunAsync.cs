using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;
using static MsgHatch.Domain.Tests.Unit.TestTypes.OutboxDispatchDoubles;
using static MsgHatch.Domain.Tests.Unit.TestTypes.TestRoutes;

namespace MsgHatch.Domain.Tests.Unit.Dispatch;

/// <summary>
/// The dispatch loops of the outbox dispatcher (section 4.9), through <see cref="IOutboxDispatcher.RunAsync"/>. Each test
/// waits for an observable call: a claim, a recorded try, a timer of the fake clock, or a send.
/// </summary>
public sealed partial class OutboxDispatcherUnitTests
{
    [Fact]
    public async Task RunAsync_PollingAndAnEmptyStore_ClaimsAgainAfterAPollDelayThatDoublesToThirtySeconds()
    {
        // Arrange
        await using var provider = _doubles.Build(transports: TestTransports.For(TestChannel.Id));
        await using var running = new RunningLoop(provider.GetRequiredService<IOutboxDispatcher>().RunAsync);
        await _doubles.NextClaimAsync();

        // Act
        TimeSpan[] delays =
        [
            await PollAgainAsync(), await PollAgainAsync(), await PollAgainAsync(), await PollAgainAsync(),
            await PollAgainAsync(), await PollAgainAsync(), await PollAgainAsync(),
        ];

        // Assert
        Assert.Equal([1, 2, 4, 8, 16, 30, 30], delays.Select(static delay => delay.TotalSeconds));
        _ = _doubles.Store.Received(8).ClaimDueAsync(TestChannel.Id, 16, Arg.Any<DateTime>(), TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_NoPollingAndAnEmptyStore_ClaimsNoMoreWhileAnHourPassesAndClaimsAtOnceOnASignal()
    {
        // Arrange
        await using var provider = _doubles.Build(static o => o.EnablePolling = false, transports: TestTransports.For(TestChannel.Id));
        await using var running = new RunningLoop(provider.GetRequiredService<IOutboxDispatcher>().RunAsync);
        await _doubles.NextClaimAsync();
        _doubles.Time.Advance(TimeSpan.FromHours(1));
        Assert.False(_doubles.HasMoreClaims || _doubles.Time.HasMoreWaits);

        // Act
        await provider.GetRequiredService<IDispatchSignal>().NotifyAsync(DispatchTopic.ForChannel(TestChannel.Id), Ct);

        // Assert
        Assert.Equal(_doubles.Now, (await _doubles.NextClaimAsync()).NowUtc);
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.Id, 16, TestHost.Start.UtcDateTime, TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.Id, 16, _doubles.Now, TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_NoPollingAndANextDueTimeTenSecondsLater_ClaimsAgainWhenTheTimeMovesTenSeconds()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, ClaimWithNextDue(_doubles.Now.AddSeconds(10)));
        await using var provider = _doubles.Build(static o => o.EnablePolling = false, transports: TestTransports.For(TestChannel.Id));
        await using var running = new RunningLoop(provider.GetRequiredService<IOutboxDispatcher>().RunAsync);
        await _doubles.NextClaimAsync();
        var wait = await _doubles.Time.NextWaitAsync();

        // Act
        _doubles.Time.Advance(wait);

        // Assert
        Assert.Equal((TimeSpan.FromSeconds(10), TestHost.Start.UtcDateTime.AddSeconds(10)), (wait, (await _doubles.NextClaimAsync()).NowUtc));
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.Id, 16, TestHost.Start.UtcDateTime, TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).ClaimDueAsync(
            TestChannel.Id, 16, TestHost.Start.UtcDateTime.AddSeconds(10), TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_NoPollingAndATransientError_ClaimsAgainAtTheNextTryTimeWithNoSignal()
    {
        // Arrange
        var message = AMessage();
        _doubles.ClaimsGive(TestChannel.Id, Claim(message));
        var transport = TestTransports.For(TestChannel.Id, new Error(Errors.RateLimited));
        await using var provider = _doubles.Build(static o => o.EnablePolling = false, transports: transport);
        await using var running = new RunningLoop(provider.GetRequiredService<IOutboxDispatcher>().RunAsync);
        await _doubles.NextClaimAsync();
        var nextTryAtUtc = (await _doubles.NextRecordAsync()).NextTryAtUtc!.Value;
        var wait = await _doubles.Time.NextWaitAsync();

        // Act
        _doubles.Time.Advance(wait);

        // Assert
        Assert.Equal(nextTryAtUtc, (await _doubles.NextClaimAsync()).NowUtc);
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.Id, 16, TestHost.Start.UtcDateTime, TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.Id, 16, nextTryAtUtc, TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
        _ = transport.Received(1).SendAsync(message, null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).RecordTryAsync(
            message, Arg.Is<PublishTry>(t => t.Outcome == PublishOutcome.TransientError), nextTryAtUtc, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_SignalOfProcessAsync_ClaimsInTheLoopOfEachChannelOfTheStoredMessagesOnly()
    {
        // Arrange
        MessageRoute[] routes = [ARoute("u1"), ARoute("u2", channel: TestChannel.SecondId, settings: new OtherTestChannelSettings())];
        await using var provider = _doubles.Build(
            static o => o.EnablePolling = false,
            b => b.AddMessage("TestEvent", 2, TestJsonContext.Default.TestEvent, m => m
                .WithRouter((_, _) => PipelineDoubles.RouterGiving<TestEvent>(() => routes))
                .AsChannel(TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope,
                    static (_, _) => PipelineDoubles.EchoComposer<TestEvent, TestEnvelope>(static r => new TestEnvelope(r.Target.Id)))
                .AsChannel(TestChannel.SecondId, "OtherEnvelope", 1, TestJsonContext.Default.OtherTestEnvelope,
                    static (_, _) => PipelineDoubles.EchoComposer<TestEvent, OtherTestEnvelope>(static r => new OtherTestEnvelope(r.Target.Id)))),
            TestTransports.For(TestChannel.Id), TestTransports.For(TestChannel.SecondId), TestTransports.For(TestChannel.ThirdId));
        await using var running = new RunningLoop(provider.GetRequiredService<IOutboxDispatcher>().RunAsync);
        ClaimCall[] firstClaims = [await _doubles.NextClaimAsync(), await _doubles.NextClaimAsync(), await _doubles.NextClaimAsync()];
        await using var scope = provider.CreateAsyncScope();

        // Act
        await scope.ServiceProvider.GetRequiredService<INotificationService>()
            .ProcessAsync(new TestEvent("text"), new MessageContext(Guid.NewGuid()), Ct);

        // Assert
        ClaimCall[] claims = [await _doubles.NextClaimAsync(), await _doubles.NextClaimAsync()];
        Assert.Equal([TestChannel.Id, TestChannel.SecondId], claims.Select(static c => c.Channel).OrderBy(static c => c.Name, StringComparer.Ordinal).Reverse());
        Assert.Equal((false, 1), (_doubles.HasMoreClaims, claims.Select(static c => c.NowUtc).Concat(firstClaims.Select(static c => c.NowUtc)).Distinct().Count()));
        _ = _doubles.Store.Received(2).ClaimDueAsync(TestChannel.Id, 16, _doubles.Now, TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(2).ClaimDueAsync(TestChannel.SecondId, 16, _doubles.Now, TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.ThirdId, 16, _doubles.Now, TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_ClaimThatFillsTheFreeCapacity_ClaimsAgainAtOnce()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage(), AMessage()));
        await using var provider = _doubles.Build(
            static o => (o.EnablePolling, o.MaxDegreeOfParallelism, o.QueueCapacity) = (false, 2, 2), transports: TestTransports.For(TestChannel.Id));

        // Act
        await using var running = new RunningLoop(provider.GetRequiredService<IOutboxDispatcher>().RunAsync);

        // Assert
        ClaimCall[] claims = [await _doubles.NextClaimAsync(), await _doubles.NextClaimAsync()];
        Assert.Equal((2, claims[0].NowUtc), (claims[0].MaxCount, claims[1].NowUtc));
        _ = _doubles.Store.Received(2).ClaimDueAsync(
            TestChannel.Id, Arg.Any<int>(), TestHost.Start.UtcDateTime, TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_AllWorkersBusyInASendThatDoesNotComplete_ClaimsAtMostTheFreeCapacity()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage(), AMessage()), Claim(AMessage()), Claim(AMessage()));
        var sendStarted = new TaskCompletionSource();
        var transport = TestTransports.NeverCompleting(TestChannel.Id, sendStarted);
        await using var provider = _doubles.Build(
            static o => (o.EnablePolling, o.MaxDegreeOfParallelism, o.QueueCapacity) = (false, 1, 2), transports: transport);

        // Act
        await using var running = new RunningLoop(provider.GetRequiredService<IOutboxDispatcher>().RunAsync);

        // Assert
        ClaimCall[] claims = [await _doubles.NextClaimAsync(), await _doubles.NextClaimAsync()];
        await sendStarted.Task.WaitAsync(TestHost.HangGuard, TimeProvider.System, Ct);
        Assert.Equal([2, 1], claims.Select(static c => c.MaxCount));
        Assert.False(_doubles.HasMoreClaims);
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.Id, 2, TestHost.Start.UtcDateTime, TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.Id, 1, TestHost.Start.UtcDateTime, TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
        _ = transport.Received(1).SendAsync(Arg.Any<OutgoingMessage>(), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_LeaseThatEndsWithinTheSafetyMargin_SendsNothingAndClaimsAgainWhenTheLeaseEnds()
    {
        // Arrange
        var leaseEndUtc = _doubles.Now.AddSeconds(5);
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage(leaseUntilUtc: leaseEndUtc)));
        var transport = TestTransports.For(TestChannel.Id);
        await using var provider = _doubles.Build(static o => o.EnablePolling = false, transports: transport);
        await using var running = new RunningLoop(provider.GetRequiredService<IOutboxDispatcher>().RunAsync);
        await _doubles.NextClaimAsync();
        var wait = await _doubles.Time.NextWaitAsync();

        // Act
        _doubles.Time.Advance(wait);

        // Assert
        Assert.Equal(leaseEndUtc, (await _doubles.NextClaimAsync()).NowUtc);
        await transport.DidNotReceiveWithAnyArgs().SendAsync(default!, default, Ct);
        Assert.False(_doubles.HasMoreRecords);
    }

    [Fact]
    public async Task RunAsync_NoPollingAndAClaimError_ClaimsAgainAfterThePollDelay()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, new Error(Errors.Unavailable));
        await using var provider = _doubles.Build(static o => o.EnablePolling = false, transports: TestTransports.For(TestChannel.Id));
        await using var running = new RunningLoop(provider.GetRequiredService<IOutboxDispatcher>().RunAsync);
        await _doubles.NextClaimAsync();
        var wait = await _doubles.Time.NextWaitAsync();

        // Act
        _doubles.Time.Advance(wait);

        // Assert
        Assert.Equal((TimeSpan.FromSeconds(1), TestHost.Start.UtcDateTime.AddSeconds(1)), (wait, (await _doubles.NextClaimAsync()).NowUtc));
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.Id, 16, TestHost.Start.UtcDateTime, TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).ClaimDueAsync(
            TestChannel.Id, 16, TestHost.Start.UtcDateTime.AddSeconds(1), TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_NoPollingAndANextDueTimeThatPassed_ClaimsAgainAtOnceWithNoWait()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, ClaimWithNextDue(_doubles.Now.AddSeconds(-1)));
        await using var provider = _doubles.Build(static o => o.EnablePolling = false, transports: TestTransports.For(TestChannel.Id));

        // Act
        await using var running = new RunningLoop(provider.GetRequiredService<IOutboxDispatcher>().RunAsync);

        // Assert
        ClaimCall[] claims = [await _doubles.NextClaimAsync(), await _doubles.NextClaimAsync()];
        Assert.Equal((claims[0].NowUtc, false), (claims[1].NowUtc, _doubles.Time.HasMoreWaits));
        _ = _doubles.Store.Received(2).ClaimDueAsync(TestChannel.Id, 16, TestHost.Start.UtcDateTime, TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_TimeGivenWhileTheLoopStartsItsWait_WaitsUntilTheGivenTime()
    {
        // Arrange
        OutgoingMessage[] messages = [AMessage(), AMessage()];
        _doubles.ClaimsGive(TestChannel.Id, ClaimWithNextDue(_doubles.Now.AddHours(1), messages));
        var waitStarts = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var secondSend = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = TestTransports.For(TestChannel.Id);
        transport.SendAsync(default!, default, Ct).ReturnsForAnyArgs(
            call => RateLimitedAfterAsync(waitStarts.Task, call.ArgAt<CancellationToken>(2)),
            _ =>
            {
                secondSend.TrySetResult();
                return ValueTask.FromResult<ErrorOr<PublishReceipt>>(new PublishReceipt("provider-2"));
            });
        _doubles.Time.HoldNextTimer(waitStarts, secondSend.Task);
        await using var provider = _doubles.Build(static o => (o.EnablePolling, o.MaxDegreeOfParallelism) = (false, 1), transports: transport);

        // Act
        await using var running = new RunningLoop(provider.GetRequiredService<IOutboxDispatcher>().RunAsync);

        // Assert
        var nextTryAtUtc = (await _doubles.NextRecordAsync()).NextTryAtUtc!.Value;
        TimeSpan[] waits = [await _doubles.Time.NextWaitAsync(), await _doubles.Time.NextWaitAsync()];
        Assert.Equal([TimeSpan.FromHours(1), nextTryAtUtc - _doubles.Now], waits);
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.Id, 16, TestHost.Start.UtcDateTime, TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
        _ = transport.Received(1).SendAsync(messages[0], null, Arg.Any<CancellationToken>());
        _ = transport.Received(1).SendAsync(messages[1], null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).RecordTryAsync(
            messages[0], Arg.Is<PublishTry>(t => t.Outcome == PublishOutcome.TransientError), nextTryAtUtc, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(true, 2)]
    [InlineData(false, 1)]
    public async Task RunAsync_SignalThatDoesNotWork_WaitsOnePollIntervalAndClaimsAgain(bool enablePolling, int timers)
    {
        // Arrange
        var signal = Substitute.For<IDispatchSignal>();
        signal.WaitAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(new Error(Errors.Unavailable)));
        await using var provider = _doubles.Build(
            o => o.EnablePolling = enablePolling, b => b.UseDispatchSignal(_ => signal), TestTransports.For(TestChannel.Id));
        await using var running = new RunningLoop(provider.GetRequiredService<IOutboxDispatcher>().RunAsync);
        await _doubles.NextClaimAsync();
        await Task.WhenAll(Enumerable.Range(0, timers).Select(_ => _doubles.Time.NextWaitAsync()));

        // Act
        _doubles.Time.Advance(TimeSpan.FromSeconds(1));

        // Assert
        Assert.Equal(TestHost.Start.UtcDateTime.AddSeconds(1), (await _doubles.NextClaimAsync()).NowUtc);
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.Id, 16, TestHost.Start.UtcDateTime, TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).ClaimDueAsync(
            TestChannel.Id, 16, TestHost.Start.UtcDateTime.AddSeconds(1), TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
        _ = signal.Received().WaitAsync(DispatchTopic.ForChannel(TestChannel.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_SignalThatDoesNotWorkAndATimeGivenDuringTheFallbackWait_ClaimsAgainAtTheGivenTime()
    {
        // Arrange
        var signal = Substitute.For<IDispatchSignal>();
        signal.WaitAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(new Error(Errors.Unavailable)));
        var message = AMessage();
        _doubles.ClaimsGive(TestChannel.Id, ClaimWithNextDue(_doubles.Now.AddSeconds(60), message));
        var fallbackWaitStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var transport = TestTransports.For(TestChannel.Id);
        transport.SendAsync(default!, default, Ct).ReturnsForAnyArgs(call => RateLimitedAfterAsync(fallbackWaitStarted.Task, call.ArgAt<CancellationToken>(2)));
        await using var provider = _doubles.Build(
            static o => (o.EnablePolling, o.MaxDegreeOfParallelism) = (false, 1), b => b.UseDispatchSignal(_ => signal), transport);
        await using var running = new RunningLoop(provider.GetRequiredService<IOutboxDispatcher>().RunAsync);
        await _doubles.NextClaimAsync();
        TimeSpan[] fallbackWait = [await _doubles.Time.NextWaitAsync(), await _doubles.Time.NextWaitAsync()];
        fallbackWaitStarted.SetResult();
        var nextTryAtUtc = (await _doubles.NextRecordAsync()).NextTryAtUtc!.Value;
        var waitForTheGivenTime = await _doubles.Time.NextWaitAsync();

        // Act
        _doubles.Time.Advance(waitForTheGivenTime);

        // Assert
        Assert.Equal([TimeSpan.FromSeconds(60), TimeSpan.FromSeconds(60)], fallbackWait);
        Assert.Equal(
            (nextTryAtUtc - TestHost.Start.UtcDateTime, nextTryAtUtc),
            (waitForTheGivenTime, (await _doubles.NextClaimAsync()).NowUtc));
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.Id, 16, TestHost.Start.UtcDateTime, TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.Id, 16, nextTryAtUtc, TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
        _ = transport.Received(1).SendAsync(message, null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).RecordTryAsync(
            message, Arg.Is<PublishTry>(t => t.Outcome == PublishOutcome.TransientError), nextTryAtUtc, Arg.Any<CancellationToken>());
        _ = signal.Received().WaitAsync(DispatchTopic.ForChannel(TestChannel.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_SignalThatFailsAfterTheDueTime_ClaimsAgainAtOnce()
    {
        // Arrange
        var signal = Substitute.For<IDispatchSignal>();
        signal.WaitAsync(default, Ct).ReturnsForAnyArgs(
            _ =>
            {
                _doubles.Time.Advance(TimeSpan.FromSeconds(2));
                return ValueTask.FromResult<Error?>(new Error(Errors.Unavailable));
            },
            _ => ValueTask.FromResult<Error?>(new Error(Errors.Unavailable)));
        await using var provider = _doubles.Build(more: b => b.UseDispatchSignal(_ => signal), transports: TestTransports.For(TestChannel.Id));

        // Act
        await using var running = new RunningLoop(provider.GetRequiredService<IOutboxDispatcher>().RunAsync);

        // Assert
        ClaimCall[] claims = [await _doubles.NextClaimAsync(), await _doubles.NextClaimAsync()];
        Assert.Equal(TestHost.Start.UtcDateTime.AddSeconds(2), claims[1].NowUtc);
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.Id, 16, TestHost.Start.UtcDateTime, TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).ClaimDueAsync(
            TestChannel.Id, 16, TestHost.Start.UtcDateTime.AddSeconds(2), TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
        _ = signal.Received().WaitAsync(DispatchTopic.ForChannel(TestChannel.Id), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_Cancelled_ReturnsAndSendsNoMessageThatIsStillInTheWorkQueue()
    {
        // Arrange
        OutgoingMessage[] messages = [AMessage(), AMessage(), AMessage()];
        _doubles.ClaimsGive(TestChannel.Id, Claim(messages));
        var sendStarted = new TaskCompletionSource();
        var transport = TestTransports.NeverCompleting(TestChannel.Id, sendStarted);
        await using var provider = _doubles.Build(
            static o => (o.EnablePolling, o.MaxDegreeOfParallelism, o.QueueCapacity) = (false, 1, 4), transports: transport);
        var running = new RunningLoop(provider.GetRequiredService<IOutboxDispatcher>().RunAsync);
        await sendStarted.Task.WaitAsync(TestHost.HangGuard, TimeProvider.System, Ct);

        // Act
        await running.DisposeAsync();

        // Assert
        Assert.True(running.Run.IsCompletedSuccessfully);
        await transport.Received(1).SendAsync(messages[0], null, Arg.Any<CancellationToken>());
        await transport.DidNotReceive().SendAsync(Arg.Is<OutgoingMessage>(m => m != messages[0]), Arg.Any<string?>(), Arg.Any<CancellationToken>());
        await _doubles.Store.DidNotReceiveWithAnyArgs().RecordTryAsync(default!, default!, default, Ct);
        Assert.False(_doubles.HasMoreRecords);
    }

    [Fact]
    public async Task RunAsync_SameClaimedMessages_RecordsTheSameTriesAsDispatchPendingAsync()
    {
        // Arrange
        OutgoingMessage[] messages = [AMessage(id: _fixedId), AMessage(tryCount: 7)];
        var pending = new OutboxDispatchDoubles();
        pending.ClaimsGive(TestChannel.Id, Claim(messages));
        _doubles.ClaimsGive(TestChannel.Id, Claim(messages));
        await using var pendingProvider = pending.Build(
            static o => o.MaxDegreeOfParallelism = 1, transports: TestTransports.For(TestChannel.Id, new Error(Errors.RateLimited)));
        await using var provider = _doubles.Build(
            static o => (o.EnablePolling, o.MaxDegreeOfParallelism) = (false, 1),
            transports: TestTransports.For(TestChannel.Id, new Error(Errors.RateLimited)));
        await pendingProvider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Act
        await using var running = new RunningLoop(provider.GetRequiredService<IOutboxDispatcher>().RunAsync);

        // Assert
        Assert.Equal(
            [await pending.NextRecordAsync(), await pending.NextRecordAsync()],
            [await _doubles.NextRecordAsync(), await _doubles.NextRecordAsync()]);
        _ = _doubles.Store.Received(1).RecordTryAsync(
            messages[0], Arg.Is<PublishTry>(t => t.TryNumber == 1 && t.Outcome == PublishOutcome.TransientError), Arg.Is<DateTime?>(d => d != null),
            Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).RecordTryAsync(
            messages[1], Arg.Is<PublishTry>(t => t.TryNumber == 8 && t.Outcome == PublishOutcome.TransientError), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_TransportsForTwoChannels_ClaimsEachChannelInItsOwnLoop()
    {
        // Arrange
        await using var provider = _doubles.Build(transports: [TestTransports.For(TestChannel.Id), TestTransports.For(TestChannel.SecondId)]);

        // Act
        await using var running = new RunningLoop(provider.GetRequiredService<IOutboxDispatcher>().RunAsync);

        // Assert
        ClaimCall[] claims = [await _doubles.NextClaimAsync(), await _doubles.NextClaimAsync()];
        Assert.Equal([TestChannel.Id, TestChannel.SecondId], claims.Select(static c => c.Channel).OrderByDescending(static c => c.Name, StringComparer.Ordinal));
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.Id, 16, TestHost.Start.UtcDateTime, TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.SecondId, 16, TestHost.Start.UtcDateTime, TimeSpan.FromSeconds(60), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task RunAsync_SendOfTheFirstChannelThatDoesNotComplete_DoesNotDelayTheSendOfTheSecondChannel()
    {
        // Arrange
        var (ofTheFirstChannel, ofTheSecondChannel) = (AMessage(), AMessage(TestChannel.SecondId));
        _doubles.ClaimsGive(TestChannel.Id, Claim(ofTheFirstChannel));
        _doubles.ClaimsGive(TestChannel.SecondId, Claim(ofTheSecondChannel));
        var sendStarted = new TaskCompletionSource();
        var (first, second) = (TestTransports.NeverCompleting(TestChannel.Id, sendStarted), TestTransports.For(TestChannel.SecondId));
        await using var provider = _doubles.Build(transports: [first, second]);

        // Act
        await using var running = new RunningLoop(provider.GetRequiredService<IOutboxDispatcher>().RunAsync);

        // Assert
        await sendStarted.Task.WaitAsync(TestHost.HangGuard, TimeProvider.System, Ct);
        Assert.Equal(ofTheSecondChannel, (await _doubles.NextRecordAsync()).Message);
        _ = first.Received(1).SendAsync(ofTheFirstChannel, null, Arg.Any<CancellationToken>());
        _ = second.Received(1).SendAsync(ofTheSecondChannel, null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).RecordTryAsync(
            ofTheSecondChannel, Arg.Is<PublishTry>(t => t.Outcome == PublishOutcome.Succeeded), null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.DidNotReceive().RecordTryAsync(
            ofTheFirstChannel, Arg.Any<PublishTry>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
    }

    private static async ValueTask<ErrorOr<PublishReceipt>> RateLimitedAfterAsync(Task waitStarts, CancellationToken ct)
    {
        await waitStarts.WaitAsync(ct);
        return new Error(Errors.RateLimited);
    }

    private async Task<TimeSpan> PollAgainAsync()
    {
        var wait = await _doubles.Time.NextWaitAsync();
        _doubles.Time.Advance(wait);
        await _doubles.NextClaimAsync();
        return wait;
    }
}
