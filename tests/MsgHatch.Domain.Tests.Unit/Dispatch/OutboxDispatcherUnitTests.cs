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
        Assert.Equal(DateTime.Parse(expected, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal), (await _doubles.NextRecordAsync()).NextTryAtUtc);
    }

    [Fact]
    public async Task DispatchPendingAsync_ClockMovesOneSecondOnEachRead_RecordsAStartAndAFinishOneSecondApart()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage()));
        _doubles.Time.AutoAdvanceAmount = TimeSpan.FromSeconds(1); // the logging wrappers are off, so only the dispatcher reads the clock
        await using var provider = _doubles.Build(transports: TestTransports.For(TestChannel.Id));

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var publishTry = (await _doubles.NextRecordAsync()).Try;
        Assert.Equal(TimeSpan.FromSeconds(1), publishTry.FinishedAtUtc - publishTry.StartedAtUtc);
    }

    [Theory]
    [InlineData(8, 503)] // a transient error on the last allowed try
    [InlineData(1, 403)] // a permanent error
    public async Task DispatchPendingAsync_TransientErrorOnTheLastTryOrPermanentError_RecordsNoNextTryTime(int tryNumber, int errorCode)
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage(tryCount: tryNumber - 1, maxTryCount: 8)));
        var error = errorCode == 503 ? new Error(Errors.Unavailable) : new Error(Errors.Rejected);
        await using var provider = _doubles.Build(transports: TestTransports.For(TestChannel.Id, error));

        // Act
        var summary = await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var record = await _doubles.NextRecordAsync();
        Assert.Equal(((DateTime?)null, (int?)errorCode, 1), (record.NextTryAtUtc, record.Try.ErrorCode, summary.Value.Failed));
    }

    [Fact]
    public async Task DispatchPendingAsync_MessageWithAnExpiryOfFiveMinutes_SendsItWhileTheTimeDoesNotMove()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage(expiresAtUtc: _doubles.Now.AddMinutes(5))));
        var transport = TestTransports.For(TestChannel.Id);
        await using var provider = _doubles.Build(transports: transport);

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Equal(PublishOutcome.Succeeded, (await _doubles.NextRecordAsync()).Try.Outcome);
        await transport.ReceivedWithAnyArgs(1).SendAsync(default!, default, Ct);
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
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage()));
        _doubles.Store.RecordTryAsync(default!, default!, default, Ct).ReturnsForAnyArgs(
            ValueTask.FromResult<Error?>(new Error(Errors.Unavailable)), ValueTask.FromResult<Error?>(null));
        await using var provider = _doubles.Build(transports: TestTransports.For(TestChannel.Id));

        // Act
        await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        var tries = RecordedTries(_doubles.Store);
        Assert.Equal(2, tries.Length);
        Assert.Same(tries[0], tries[1]);
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
    }

    [Fact]
    public async Task DispatchPendingAsync_DispatcherOfTheRootProviderWithScopeValidation_SendsWithNoDiScope()
    {
        // Arrange
        _doubles.ClaimsGive(TestChannel.Id, Claim(AMessage()));
        await using var provider = _doubles.Build(transports: TestTransports.For(TestChannel.Id));
        var dispatcher = provider.GetRequiredService<IOutboxDispatcher>();

        // Act
        var summary = await dispatcher.DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Equal(new DispatchSummary(1, 1, 0, 0), summary.Value);
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
    }

    [Fact]
    public async Task DispatchPendingAsync_ClaimError_ReturnsTheError()
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        _doubles.ClaimsGive(TestChannel.Id, error);
        await using var provider = _doubles.Build(transports: TestTransports.For(TestChannel.Id));

        // Act
        var summary = await provider.GetRequiredService<IOutboxDispatcher>().DispatchPendingAsync(10, Ct);

        // Assert
        Assert.Same(error, summary.Error);
    }

    private static PublishTry[] RecordedTries(IOutboxDispatchStore store) =>
        [.. store.ReceivedCalls()
            .Where(static call => call.GetMethodInfo().Name == nameof(IOutboxDispatchStore.RecordTryAsync))
            .Select(static call => (PublishTry)call.GetArguments()[1]!)];
}
