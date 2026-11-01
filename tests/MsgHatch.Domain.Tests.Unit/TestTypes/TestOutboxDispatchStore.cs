using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>An outbox dispatch store for the tests of the generic setup method. It claims, records, and changes nothing.</summary>
public sealed class TestOutboxDispatchStore : IOutboxDispatchStore
{
    public ValueTask<ErrorOr<ClaimResult<OutgoingMessage>>> ClaimDueAsync(
        ChannelId channel, int maxCount, DateTime nowUtc, TimeSpan lease, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<ClaimResult<OutgoingMessage>>.ForValue(new ClaimResult<OutgoingMessage>([], null)));

    public ValueTask<Error?> RecordTryAsync(
        OutgoingMessage message, PublishTry publishTry, DateTime? nextTryAtUtc, CancellationToken ct) =>
        ValueTask.FromResult<Error?>(null);

    public ValueTask<ErrorOr<int>> DeleteTestDataAsync(CancellationToken ct) => ValueTask.FromResult(ErrorOr<int>.ForValue(0));

    public ValueTask<ErrorOr<IReadOnlyList<ChannelDown>>> GetDownChannelsAsync(CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelDown>>.ForValue([]));

    public ValueTask<ErrorOr<bool>> SetChannelDownAsync(ChannelDown down, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<bool>.ForValue(false));

    public ValueTask<ErrorOr<bool>> SetChannelUpAsync(ChannelId channel, ChannelDownReason? reason, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<bool>.ForValue(false));

    public ValueTask<ErrorOr<bool>> TryStartChannelCheckAsync(
        ChannelId channel, DateTime nowUtc, DateTime nextCheckAtUtc, CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<bool>.ForValue(false));
}
