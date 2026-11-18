using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Management;

namespace MsgHatch.Domain.Tests.Performance;

/// <summary>
/// The outbox of the dispatch benchmarks, in both parts. A claim gives a batch of messages that the store made before
/// the measurement, and the store records nothing, so the store allocates nothing and keeps no message. With
/// <see cref="ClaimOnce"/>, only the next claim gives the batch, and the claims after it give no message.
/// </summary>
public sealed class PerfOutboxStore : IOutboxStore, IOutboxDispatchStore
{
    private static readonly ValueTask<Error?> _none = ValueTask.FromResult<Error?>(null);
    private static readonly ValueTask<ErrorOr<ClaimResult<OutgoingMessage>>> _empty =
        ValueTask.FromResult(ErrorOr<ClaimResult<OutgoingMessage>>.ForValue(new ClaimResult<OutgoingMessage>([], NextDueAtUtc: null)));

    private static readonly ValueTask<ErrorOr<IReadOnlyList<ChannelDown>>> _noDownChannels =
        ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelDown>>.ForValue([]));

    private readonly ValueTask<ErrorOr<ClaimResult<OutgoingMessage>>> _batch;
    private readonly bool _always;
    private int _armed;

    private PerfOutboxStore(int batchSize, bool always)
    {
        _always = always;
        var leaseUntilUtc = new DateTime(2100, 1, 1, 0, 0, 0, DateTimeKind.Utc);
        OutgoingMessage[] messages = [.. Enumerable.Range(1, batchSize).Select(user => MessageOf(user, leaseUntilUtc))];
        _batch = ValueTask.FromResult(ErrorOr<ClaimResult<OutgoingMessage>>.ForValue(new ClaimResult<OutgoingMessage>(messages, NextDueAtUtc: null)));
    }

    /// <summary>A store whose each claim gives the batch.</summary>
    public static PerfOutboxStore EachClaim(int batchSize) => new(batchSize, always: true);

    /// <summary>A store whose claim gives the batch only after <see cref="ClaimOnce"/>.</summary>
    public static PerfOutboxStore AfterArm(int batchSize) => new(batchSize, always: false);

    /// <summary>Lets the next claim give the batch.</summary>
    public void ClaimOnce() => Volatile.Write(ref _armed, 1);

    public ValueTask<Error?> AddAsync(IReadOnlyList<OutgoingMessage> messages, CancellationToken ct) => _none;

    public ValueTask<ErrorOr<ClaimResult<OutgoingMessage>>> ClaimDueAsync(
        ChannelId channel, int maxCount, DateTime nowUtc, TimeSpan lease, CancellationToken ct) =>
        _always || Interlocked.Exchange(ref _armed, 0) == 1 ? _batch : _empty;

    public ValueTask<Error?> RecordTryAsync(OutgoingMessage message, PublishTry publishTry, DateTime? nextTryAtUtc, CancellationToken ct) => _none;

    public ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct) => ValueTask.FromResult(ErrorOr<int>.ForValue(0));

    public ValueTask<ErrorOr<MessagePage<OutgoingMessageSummary>>> QueryAsync(OutboxQuery query, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<MessagePage<OutgoingMessageSummary>>.ForValue(new MessagePage<OutgoingMessageSummary>([], Next: null)));

    public ValueTask<ErrorOr<OutgoingMessageHistory?>> GetAsync(Guid id, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<OutgoingMessageHistory?>.ForValue(null));

    public ValueTask<ErrorOr<int>> RetryAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, int addedTries, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<int>.ForValue(0));

    public ValueTask<ErrorOr<int>> CancelAsync(IReadOnlyList<Guid> ids, DateTime nowUtc, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<int>.ForValue(0));

    public ValueTask<ErrorOr<IReadOnlyList<MessageCount>>> CountAsync(TrafficQuery query, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<MessageCount>>.ForValue([]));

    public ValueTask<ErrorOr<int>> PurgeAsync(DateTime finishedBeforeUtc, int maxCount, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<int>.ForValue(0));

    public ValueTask<ErrorOr<IReadOnlyList<ChannelDown>>> GetDownChannelsAsync(CancellationToken ct) => _noDownChannels;

    public ValueTask<ErrorOr<bool>> SetChannelDownAsync(ChannelDown down, CancellationToken ct) => ValueTask.FromResult(ErrorOr<bool>.ForValue(false));

    public ValueTask<ErrorOr<bool>> SetChannelUpAsync(ChannelId channel, ChannelDownReason? reason, DateTime upAtUtc, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<bool>.ForValue(false));

    public ValueTask<ErrorOr<IReadOnlyList<ChannelState>>> GetChannelStatesAsync(DateTime fromUtc, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelState>>.ForValue([]));

    public ValueTask<ErrorOr<bool>> TryStartChannelCheckAsync(ChannelId channel, DateTime nowUtc, DateTime nextCheckAtUtc, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<bool>.ForValue(false));

    private static OutgoingMessage<PerfEnvelope> MessageOf(int user, DateTime leaseUntilUtc) =>
        new(
            SchemaVersion: 1,
            new DeliveryState(
                Guid.NewGuid(), DeliveryStatus.Pending, TryCount: 0, MaxTryCount: 8, DateTime.UnixEpoch, LastTryAtUtc: null,
                NextTryAtUtc: leaseUntilUtc, leaseUntilUtc, ExpiresAtUtc: null, LastError: null),
            SourceMessageId: Guid.NewGuid(), Tenant: null, PerfChannel.Type, PerfChannel.Id, new DeliveryTarget(DeliveryTargetKind.User, $"u{user}"),
            "Perf:PerfEnvelope", ContentVersion: 1, MessageFlags.None, TraceParent: null, Trace: null, new PerfEnvelope("Your order is on its way."));
}
