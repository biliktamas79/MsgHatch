using System.Collections.Concurrent;
using System.Globalization;
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
using NSubstitute.ExceptionExtensions;
using static MsgHatch.Domain.Tests.Unit.TestTypes.OutboxDispatchDoubles;

namespace MsgHatch.Domain.Tests.Unit.Dispatch;

/// <summary>The outbox dispatcher through <see cref="IOutboxDispatcher"/>, with an outbox dispatch store substitute (section 4.8, publishing).</summary>
[Collection(SequentialTelemetry.Name)]
public sealed partial class OutboxDispatcherUnitTests
{
    private static readonly Guid _fixedId = Guid.Parse("11223344-5566-7788-99aa-bbccddeeff00");

    private readonly OutboxDispatchDoubles _doubles = new();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<int, int, string> NextTryRows => new()
    {
        { 1, 5, "2030-01-01T00:00:05.1178373Z" },
        { 2, 5, "2030-01-01T00:00:08.7078094Z" },
        { 8, 5, "2030-01-01T00:12:16.6015441Z" }, // 640 seconds and the factor 1.151, at most MaxDelay multiplied by 1.2
        { 1, 1, "2030-01-01T00:00:01.0235674Z" },
    };

    public static TheoryData<string, int, int?, string?> RetryAfterRows => new()
    {
        { "no wait of the provider: the schedule", 429, null, "2030-01-01T00:00:05.1178373Z" },
        { "a wait shorter than the schedule: the schedule", 429, 1, "2030-01-01T00:00:05.1178373Z" },
        { "a wait longer than the schedule: the wait", 429, 60, "2030-01-01T00:01:00Z" },
        { "a wait longer than MaxDelay: MaxDelay", 429, 3600, "2030-01-01T00:15:00Z" },
        { "a wait with another transient error: the wait", 503, 60, "2030-01-01T00:01:00Z" },
        { "a wait with a permanent error: no next try", 403, 60, null },
    };

    [Fact]
    public async Task DispatchPendingAsync_TwentyMessagesWithFourWorkers_SendsEachMessageOneTimeWithAtMostFourSendsAtTheSameTime()
    {
        // Arrange
        OutgoingMessage[] claimed = [.. Enumerable.Range(0, 20).Select(static _ => AMessage(id: Guid.NewGuid()))];
        _doubles.ClaimsGive(TestChannel.Id, Claim(claimed));
        var sends = new SendCounter();
        var transport = Substitute.For<ITransport>();
        transport.Channel.Returns(TestChannel.Id);
        transport.SendAsync(default!, default, Ct)
            .ReturnsForAnyArgs(call => new ValueTask<ErrorOr<PublishReceipt>>(sends.SendAsync(call.ArgAt<OutgoingMessage>(0))));
        await using var provider = _doubles.Build(static o => o.MaxDegreeOfParallelism = 4, transports: transport);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(20, Ct);

        // Assert
        Assert.Equal(
            (string.Join(",", claimed.Select(static m => m.Delivery.Id).Order()), true),
            (string.Join(",", sends.Sent.Order()), sends.MostAtTheSameTime <= 4));
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.Id, 20, _doubles.Now, TimeSpan.FromSeconds(60), Ct);
        _ = transport.Received(20).SendAsync(Arg.Any<OutgoingMessage>(), null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(20).RecordTryAsync(
            Arg.Is<OutgoingMessage>(m => Array.IndexOf(claimed, m) >= 0), Arg.Is<PublishTry>(t => t.Outcome == PublishOutcome.Succeeded), null,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_SendsTransientTransientAndSuccess_RecordsTriesOneTwoAndThreeWithTheClaimedMessages()
    {
        // Arrange
        OutgoingMessage[] claimed = [AMessage(id: _fixedId), AMessage(id: _fixedId, tryCount: 1), AMessage(id: _fixedId, tryCount: 2)];
        _doubles.ClaimsGive(TestChannel.Id, Claim(claimed[0]), Claim(claimed[1]), Claim(claimed[2]));
        var transport = TestTransports.For(TestChannel.Id);
        transport.SendAsync(default!, default, Ct).ReturnsForAnyArgs(
            ValueTask.FromResult<ErrorOr<PublishReceipt>>(new Error(Errors.RateLimited)),
            ValueTask.FromResult<ErrorOr<PublishReceipt>>(new Error(Errors.Unavailable)),
            ValueTask.FromResult<ErrorOr<PublishReceipt>>(new PublishReceipt("provider-9")));
        await using var provider = _doubles.Build(transports: transport);
        var dispatcher = provider.GetRequiredService<IOutboxDispatcher>();
        await dispatcher.DispatchPendingAsync(10, Ct);
        await dispatcher.DispatchPendingAsync(10, Ct);

        // Act
        await dispatcher.DispatchPendingAsync(10, Ct);

        // Assert
        RecordCall[] records = [await _doubles.NextRecordAsync(), await _doubles.NextRecordAsync(), await _doubles.NextRecordAsync()];
        Assert.Equal(
            [(claimed[0], 1, PublishOutcome.TransientError, (string?)null), (claimed[1], 2, PublishOutcome.TransientError, null),
             (claimed[2], 3, PublishOutcome.Succeeded, "provider-9")],
            records.Select(static r => (r.Message, r.Try.TryNumber, r.Try.Outcome, r.Try.ProviderMessageId)));
        Received.InOrder(() =>
        {
            _ = transport.SendAsync(claimed[0], null, Arg.Any<CancellationToken>());
            _ = _doubles.Store.RecordTryAsync(claimed[0], Arg.Is<PublishTry>(t => t.TryNumber == 1), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
            _ = transport.SendAsync(claimed[1], null, Arg.Any<CancellationToken>());
            _ = _doubles.Store.RecordTryAsync(claimed[1], Arg.Is<PublishTry>(t => t.TryNumber == 2), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>());
            _ = transport.SendAsync(claimed[2], null, Arg.Any<CancellationToken>());
            _ = _doubles.Store.RecordTryAsync(claimed[2], Arg.Is<PublishTry>(t => t.TryNumber == 3), null, Arg.Any<CancellationToken>());
        });
    }

    [Theory]
    [MemberData(nameof(NextTryRows))]
    public async Task DispatchPendingAsync_TransientError_RecordsTheExactNextTryTime(int tryNumber, int baseDelaySeconds, string expected)
    {
        // Arrange
        var message = AMessage(id: _fixedId, tryCount: tryNumber - 1, maxTryCount: 10);
        _doubles.ClaimsGive(TestChannel.Id, Claim(message));
        var transport = TestTransports.For(TestChannel.Id, new Error(Errors.RateLimited));
        await using var provider = _doubles.Build(transports: transport, more: b => b.AddMessage(
            "TestEvent", 2, TestJsonContext.Default.TestEvent, m => m
                .WithRouter<TwoRouteTestRouter>()
                .AsChannel(
                    TestChannel.Id, "TestEnvelope", 1, TestJsonContext.Default.TestEnvelope, TestChannel.AnyComposer<TestEvent>(),
                    configure: c => c.WithRetry(o => o.BaseDelay = TimeSpan.FromSeconds(baseDelaySeconds)))));

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var nextTryAtUtc = DateTime.Parse(expected, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
        Assert.Equal(nextTryAtUtc, (await _doubles.NextRecordAsync()).NextTryAtUtc);
        _ = transport.Received(1).SendAsync(message, null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).RecordTryAsync(
            message, Arg.Is<PublishTry>(t => t.TryNumber == tryNumber && t.Outcome == PublishOutcome.TransientError), nextTryAtUtc,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [MemberData(nameof(RetryAfterRows))]
    public async Task DispatchPendingAsync_ErrorWithAWaitOfTheProvider_RecordsTheNextTryTimeOfTheRow(
        string row, int errorCode, int? waitSeconds, string? expected)
    {
        // Arrange
        var message = AMessage(id: _fixedId, maxTryCount: 10);
        _doubles.ClaimsGive(TestChannel.Id, Claim(message));
        var error = new Error(Errors.Find(errorCode)!) { RetryAfter = waitSeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null };
        var transport = TestTransports.For(TestChannel.Id, error);
        await using var provider = _doubles.Build(transports: transport);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var nextTryAtUtc = expected is null ? (DateTime?)null : DateTime.Parse(expected, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
        Assert.Equal((row, nextTryAtUtc), (row, (await _doubles.NextRecordAsync()).NextTryAtUtc));
        _ = transport.Received(1).SendAsync(message, null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).RecordTryAsync(
            message, Arg.Is<PublishTry>(t => t.ErrorCode == errorCode), nextTryAtUtc, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_ClockMovesOneSecondOnEachRead_RecordsAStartAndAFinishOneSecondApart()
    {
        // Arrange
        var message = AMessage();
        _doubles.ClaimsGive(TestChannel.Id, Claim(message));
        _doubles.Time.AutoAdvanceAmount = TimeSpan.FromSeconds(1); // the logging wrappers are off, so only the dispatcher reads the clock
        var transport = TestTransports.For(TestChannel.Id);
        await using var provider = _doubles.Build(transports: transport);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var publishTry = (await _doubles.NextRecordAsync()).Try;
        Assert.Equal(TimeSpan.FromSeconds(1), publishTry.FinishedAtUtc - publishTry.StartedAtUtc);
        _ = transport.Received(1).SendAsync(message, null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).RecordTryAsync(message, publishTry, null, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(8, 503)] // a transient error on the last allowed try
    [InlineData(1, 403)] // a permanent error
    public async Task DispatchPendingAsync_TransientErrorOnTheLastTryOrPermanentError_RecordsNoNextTryTime(int tryNumber, int errorCode)
    {
        // Arrange
        var message = AMessage(tryCount: tryNumber - 1, maxTryCount: 8);
        _doubles.ClaimsGive(TestChannel.Id, Claim(message));
        var error = errorCode == 503 ? new Error(Errors.Unavailable) : new Error(Errors.Rejected);
        var transport = TestTransports.For(TestChannel.Id, error);
        await using var provider = _doubles.Build(transports: transport);

        // Act
        var summary = await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var record = await _doubles.NextRecordAsync();
        Assert.Equal(((DateTime?)null, (int?)errorCode, 1), (record.NextTryAtUtc, record.Try.ErrorCode, summary.Value.Failed));
        _ = transport.Received(1).SendAsync(message, null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).RecordTryAsync(
            message, Arg.Is<PublishTry>(t => t.TryNumber == tryNumber && t.ErrorCode == errorCode), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_MessageWithAnExpiryOfFiveMinutes_SendsItWhileTheTimeDoesNotMove()
    {
        // Arrange
        var message = AMessage(expiresAtUtc: _doubles.Now.AddMinutes(5));
        _doubles.ClaimsGive(TestChannel.Id, Claim(message));
        var transport = TestTransports.For(TestChannel.Id);
        await using var provider = _doubles.Build(transports: transport);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Equal(PublishOutcome.Succeeded, (await _doubles.NextRecordAsync()).Try.Outcome);
        await transport.Received(1).SendAsync(message, null, Arg.Any<CancellationToken>());
        await _doubles.Store.Received(1).RecordTryAsync(
            message, Arg.Is<PublishTry>(t => t.Outcome == PublishOutcome.Succeeded), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_SixMinutesAfterAnExpiryOfFiveMinutes_RecordsAnExpiredTryAndSendsNothing()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage(expiresAtUtc: _doubles.Now.AddMinutes(5))));
        var transport = TestTransports.For(TestChannel.Id);
        await using var provider = _doubles.Build(transports: transport);
        _doubles.Time.Advance(TimeSpan.FromMinutes(6));

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var publishTry = (await _doubles.NextRecordAsync()).Try;
        Assert.Equal((PublishOutcome.Expired, (int?)410), (publishTry.Outcome, publishTry.ErrorCode));
        await transport.DidNotReceiveWithAnyArgs().SendAsync(default!, default, Ct);
    }

    [Fact]
    public async Task DispatchPendingAsync_TokenCancelledDuringTheSend_ReturnsOperationCancelledAndRecordsNoTry()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage()));
        using var stop = CancellationTokenSource.CreateLinkedTokenSource(Ct);
        var transport = TestTransports.For(TestChannel.Id);
        transport.SendAsync(default!, default, Ct).ReturnsForAnyArgs(_ =>
        {
            stop.Cancel();
            return ValueTask.FromResult<ErrorOr<PublishReceipt>>(new Error(Errors.OperationCancelled));
        });
        await using var provider = _doubles.Build(transports: transport);

        // Act
        var summary = await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, stop.Token);

        // Assert
        Assert.Equal(Errors.OperationCancelled, summary.Error?.Type);
        await _doubles.Store.DidNotReceiveWithAnyArgs().RecordTryAsync(default!, default!, default, Ct);
    }

    [Fact]
    public async Task DispatchPendingAsync_FailedRecordTry_RepeatsItOneTimeWithTheSameTry()
    {
        // Arrange
        var message = AMessage();
        _doubles.ClaimsGive(TestChannel.Id, Claim(message));
        _doubles.Store.RecordTryAsync(default!, default!, default, Ct).ReturnsForAnyArgs(
            ValueTask.FromResult<Error?>(new Error(Errors.Unavailable)), ValueTask.FromResult<Error?>(null));
        var transport = TestTransports.For(TestChannel.Id);
        await using var provider = _doubles.Build(transports: transport);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var tries = RecordedTries(_doubles.Store);
        Assert.Equal(2, tries.Length);
        Assert.Same(tries[0], tries[1]);
        _ = transport.Received(1).SendAsync(message, null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(2).RecordTryAsync(message, tries[0], null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_StoreThatThrowsForTheFirstMessage_LogsExceptionAndSendsTheNextMessage()
    {
        // Arrange
        var (first, second) = (AMessage(), AMessage());
        _doubles.ClaimsGive(TestChannel.Id, Claim(first, second));
        _doubles.Store.RecordTryAsync(Arg.Is<OutgoingMessage>(m => m == first), Arg.Any<PublishTry>(), Arg.Any<DateTime?>(), Arg.Any<CancellationToken>())
            .Throws(new InvalidOperationException("defective store"));
        var transport = TestTransports.For(TestChannel.Id);
        await using var provider = _doubles.Build(dispatch: static o => o.MaxDegreeOfParallelism = 1, transports: transport);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Equal(second, (await _doubles.NextRecordAsync()).Message);
        var record = Assert.Single(provider.LogRecords(), static r => r.Id.Id == 1400);
        Assert.Equal((LogLevel.Error, "500"), (record.Level, record.GetStructuredStateValue("ErrorCode")));
        _ = transport.Received(1).SendAsync(first, null, Arg.Any<CancellationToken>());
        _ = transport.Received(1).SendAsync(second, null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).RecordTryAsync(
            first, Arg.Is<PublishTry>(t => t.Outcome == PublishOutcome.Succeeded), null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).RecordTryAsync(
            second, Arg.Is<PublishTry>(t => t.Outcome == PublishOutcome.Succeeded), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_TransportThatThrows_LogsTheExceptionAndRecordsATransientExceptionTryWithANextTryTime()
    {
        // Arrange
        var message = AMessage(tryCount: 0, maxTryCount: 8);
        _doubles.ClaimsGive(TestChannel.Id, Claim(message));
        var transport = AThrowingTransport();
        await using var provider = _doubles.Build(transports: transport);

        // Act
        var summary = await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var record = await _doubles.NextRecordAsync();
        Assert.Equal(
            (PublishOutcome.TransientError, (int?)500, "Exception", true, 1),
            (record.Try.Outcome, record.Try.ErrorCode, record.Try.ErrorName, record.NextTryAtUtc is not null, summary.Value.Retried));
        Assert.Equal(LogLevel.Error, Assert.Single(provider.LogRecords(), static r => r.Id.Id == 1400).Level);
        _ = transport.Received(1).SendAsync(message, null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).RecordTryAsync(
            message, Arg.Is<PublishTry>(t => t.ErrorCode == 500), Arg.Is<DateTime?>(d => d != null), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_TransportThatThrowsOnTheLastTry_RecordsNoNextTryTimeAndFailsTheMessage()
    {
        // Arrange
        var message = AMessage(tryCount: 7, maxTryCount: 8);
        _doubles.ClaimsGive(TestChannel.Id, Claim(message));
        var transport = AThrowingTransport();
        await using var provider = _doubles.Build(transports: transport);

        // Act
        var summary = await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var record = await _doubles.NextRecordAsync();
        Assert.Equal(((DateTime?)null, (int?)500, 1), (record.NextTryAtUtc, record.Try.ErrorCode, summary.Value.Failed));
        _ = transport.Received(1).SendAsync(message, null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).RecordTryAsync(
            message, Arg.Is<PublishTry>(t => t.TryNumber == 8 && t.ErrorCode == 500), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_ThrowBetweenTwoTransientErrors_DoesNotChangeTheErrorCountOfTheDownState()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage()), Claim(AMessage()), Claim(AMessage()));
        var transport = TestTransports.For(TestChannel.Id);
        transport.SendAsync(default!, default, Ct).ReturnsForAnyArgs(
            static _ => ValueTask.FromResult<ErrorOr<PublishReceipt>>(new Error(Errors.RateLimited)),
            static _ => throw new InvalidOperationException("defective transport"),
            static _ => ValueTask.FromResult<ErrorOr<PublishReceipt>>(new Error(Errors.RateLimited)));
        await using var provider = _doubles.Build(
            transports: transport, more: static b => b.ConfigureChannelDown(TestChannel.Id, static o => o.ErrorThreshold = 2));
        var dispatcher = provider.GetRequiredService<IOutboxDispatcher>();
        await dispatcher.DispatchPendingAsync(10, Ct);
        await dispatcher.DispatchPendingAsync(10, Ct);

        // Act
        await dispatcher.DispatchPendingAsync(10, Ct);

        // Assert
        var down = Assert.Single(SetDownStates());
        Assert.Equal((int?)429, down.ErrorCode);
        _ = transport.Received(3).SendAsync(Arg.Any<OutgoingMessage>(), null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).SetChannelDownAsync(
            Arg.Is<ChannelDown>(d => d.Channel == TestChannel.Id && d.Reason == ChannelDownReason.Errors && d.ErrorCode == 429),
            Arg.Any<CancellationToken>());
    }

    private static ITransport AThrowingTransport()
    {
        var transport = TestTransports.For(TestChannel.Id);
        transport.SendAsync(default!, default, Ct).ThrowsAsyncForAnyArgs(new InvalidOperationException("defective transport"));
        return transport;
    }

    [Fact]
    public async Task DispatchPendingAsync_FakeTime_GivesItToTheClaimAsNow()
    {
        // Arrange
        _doubles.Time.Advance(TimeSpan.FromMinutes(3));
        await using var provider = _doubles.Build(transports: TestTransports.For(TestChannel.Id));

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Equal(TestHost.Start.UtcDateTime.AddMinutes(3), (await _doubles.NextClaimAsync()).NowUtc);
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.Id, 10, TestHost.Start.UtcDateTime.AddMinutes(3), TimeSpan.FromSeconds(60), Ct);
    }

    [Fact]
    public async Task DispatchPendingAsync_DispatcherOfTheRootProviderWithScopeValidation_SendsWithNoDiScope()
    {
        // Arrange
        var message = AMessage();
        _doubles.ClaimsGive(TestChannel.Id, Claim(message));
        var transport = TestTransports.For(TestChannel.Id);
        await using var provider = _doubles.Build(transports: transport);
        var dispatcher = provider.GetRequiredService<IOutboxDispatcher>();

        // Act
        var summary = await dispatcher.DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Equal(new DispatchSummary(1, 1, 0, 0), summary.Value);
        _ = transport.Received(1).SendAsync(message, null, Arg.Any<CancellationToken>());
        _ = _doubles.Store.Received(1).RecordTryAsync(
            message, Arg.Is<PublishTry>(t => t.Outcome == PublishOutcome.Succeeded), null, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_BatchSizeZero_ReturnsValidationFailed()
    {
        // Arrange
        await using var provider = _doubles.Build(transports: TestTransports.For(TestChannel.Id));

        // Act
        var summary = await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(0, Ct);

        // Assert
        Assert.Equal(Errors.ValidationFailed, summary.Error?.Type);
        _ = _doubles.Store.DidNotReceiveWithAnyArgs().ClaimDueAsync(default, default, default, default, Ct);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task DispatchPendingAsync_BatchSize_ClaimsAtMostThatNumberForEachChannel(int batchSize)
    {
        // Arrange
        await using var provider = _doubles.Build(
            transports: [TestTransports.For(TestChannel.Id), TestTransports.For(TestChannel.SecondId)]);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(batchSize, Ct);

        // Assert
        Assert.Equal(
            [(TestChannel.Id, batchSize), (TestChannel.SecondId, batchSize)],
            [ToChannelAndMaxCount(await _doubles.NextClaimAsync()), ToChannelAndMaxCount(await _doubles.NextClaimAsync())]);
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.Id, batchSize, _doubles.Now, TimeSpan.FromSeconds(60), Ct);
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.SecondId, batchSize, _doubles.Now, TimeSpan.FromSeconds(60), Ct);
    }

    private static (ChannelId Channel, int MaxCount) ToChannelAndMaxCount(ClaimCall claim) => (claim.Channel, claim.MaxCount);

    [Fact]
    public async Task DispatchPendingAsync_TransportForOnlyTheFirstChannel_ClaimsOnlyTheFirstChannel()
    {
        // Arrange
        await using var provider = _doubles.Build(transports: TestTransports.For(TestChannel.Id));

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Equal(TestChannel.Id, (await _doubles.NextClaimAsync()).Channel);
        Assert.False(_doubles.HasMoreClaims);
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.Id, 10, _doubles.Now, TimeSpan.FromSeconds(60), Ct);
        _ = _doubles.Store.DidNotReceive().ClaimDueAsync(
            Arg.Is<ChannelId>(c => c != TestChannel.Id), Arg.Any<int>(), Arg.Any<DateTime>(), Arg.Any<TimeSpan>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DispatchPendingAsync_ClaimError_ReturnsTheError()
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        _doubles.ClaimsGive(TestChannel.Id, error);
        var transport = TestTransports.For(TestChannel.Id);
        await using var provider = _doubles.Build(transports: transport);

        // Act
        var summary = await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Same(error, summary.Error);
        _ = _doubles.Store.Received(1).ClaimDueAsync(TestChannel.Id, 10, _doubles.Now, TimeSpan.FromSeconds(60), Ct);
        _ = transport.DidNotReceiveWithAnyArgs().SendAsync(default!, default, Ct);
        _ = _doubles.Store.DidNotReceiveWithAnyArgs().RecordTryAsync(default!, default!, default, Ct);
    }

    private static PublishTry[] RecordedTries(IOutboxDispatchStore store) =>
        [.. store.ReceivedCalls()
            .Where(static call => call.GetMethodInfo().Name == nameof(IOutboxDispatchStore.RecordTryAsync))
            .Select(static call => (PublishTry)call.GetArguments()[1]!)];

    /// <summary>A transport send that takes 20 ms, and counts the sends that run at the same time.</summary>
    private sealed class SendCounter
    {
        private readonly Lock _lock = new();
        private int _running;

        public ConcurrentQueue<Guid> Sent { get; } = new();

        public int MostAtTheSameTime { get; private set; }

        public async Task<ErrorOr<PublishReceipt>> SendAsync(OutgoingMessage message)
        {
            lock (_lock)
            {
                _running++;
                MostAtTheSameTime = Math.Max(MostAtTheSameTime, _running);
            }

            await Task.Delay(TimeSpan.FromMilliseconds(20), TimeProvider.System);
            lock (_lock)
            {
                _running--;
            }

            Sent.Enqueue(message.Delivery.Id);
            return new PublishReceipt("provider-1");
        }
    }
}
