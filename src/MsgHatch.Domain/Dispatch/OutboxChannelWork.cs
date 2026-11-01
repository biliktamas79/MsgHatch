using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Time;

namespace MsgHatch.Domain.Dispatch;

/// <summary>
/// The work of the outbox loop of one channel: it claims the messages of the channel, and publishes each claimed
/// message with the transport of the channel (section 4.8, publishing). It creates no DI scope (D130).
/// </summary>
internal sealed class OutboxChannelWork(
    ITransport transport, IOutboxDispatchStore store, DispatchOptions options, IOptionsMonitor<RetryOptions> retryOptions,
    TimeProvider time, ILogger logger) : IDispatchWork<OutgoingMessage>
{
    public DispatchTopic Topic { get; } = DispatchTopic.ForChannel(transport.Channel);

    public ValueTask BeforeClaimAsync(CancellationToken ct) => ValueTask.CompletedTask;

    public ValueTask<ErrorOr<ClaimResult<OutgoingMessage>>> ClaimAsync(int maxCount, DateTime nowUtc, CancellationToken ct) =>
        store.ClaimDueAsync(transport.Channel, maxCount, nowUtc, options.Lease, ct);

    public DateTime? NextWakeAtUtc(ClaimResult<OutgoingMessage> result) => result.NextDueAtUtc;

    public DateTime? LeaseUntilUtcOf(OutgoingMessage message) => message.Delivery.LeaseUntilUtc;

    public async ValueTask<ItemResult> ProcessAsync(OutgoingMessage message, CancellationToken ct)
    {
        try
        {
            return await PublishAsync(message, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ItemResult.None;
        }
        catch (Exception exception)
        {
            Log.OutgoingMessageFailed(
                logger, message.Delivery.Id, message.Channel.Name, message.Tenant?.Value, message.Flags, Errors.Exception.Code,
                Errors.Exception.Name, exception);
            return ItemResult.None;
        }
    }

    private async ValueTask<ItemResult> PublishAsync(OutgoingMessage message, CancellationToken ct)
    {
        var tryNumber = message.Delivery.TryCount + 1;
        var startedAtUtc = time.GetUtcNowDateTime();
        if (message.Delivery.ExpiresAtUtc <= startedAtUtc)
        {
            var expired = new PublishTry(
                message.Delivery.Id, tryNumber, startedAtUtc, startedAtUtc, PublishOutcome.Expired, ProviderMessageId: null,
                Errors.Expired.Code, Errors.Expired.Name, ErrorMessage: null);
            return await RecordAsync(message, expired, nextTryAtUtc: null, ct);
        }

        var sent = await transport.SendAsync(message, redirectTo: null, ct);
        var finishedAtUtc = time.GetUtcNowDateTime();
        return IsCancelledSend(sent)
            ? ItemResult.None
            : await RecordSendAsync(message, tryNumber, startedAtUtc, finishedAtUtc, sent, ct);
    }

    private ValueTask<ItemResult> RecordSendAsync(
        OutgoingMessage message, int tryNumber, DateTime startedAtUtc, DateTime finishedAtUtc, ErrorOr<PublishReceipt> sent,
        CancellationToken ct)
    {
        if (sent.IsError)
        {
            var outcome = sent.Error.Type.IsTransient ? PublishOutcome.TransientError : PublishOutcome.PermanentError;
            var failed = new PublishTry(
                message.Delivery.Id, tryNumber, startedAtUtc, finishedAtUtc, outcome, ProviderMessageId: null, sent.Error.Type.Code,
                sent.Error.Type.Name, sent.Error.Message);
            return RecordAsync(message, failed, NextTryAtUtc(message, failed), ct);
        }

        var succeeded = new PublishTry(
            message.Delivery.Id, tryNumber, startedAtUtc, finishedAtUtc, PublishOutcome.Succeeded, sent.Value.ProviderMessageId,
            ErrorCode: null, ErrorName: null, ErrorMessage: null);
        return RecordAsync(message, succeeded, nextTryAtUtc: null, ct);
    }

    private DateTime? NextTryAtUtc(OutgoingMessage message, PublishTry failed) =>
        failed.Outcome == PublishOutcome.TransientError && failed.TryNumber < message.Delivery.MaxTryCount
            ? RetrySchedule.NextTryAtUtc(message.Delivery.Id, failed.TryNumber, failed.FinishedAtUtc, retryOptions.Get(message.ContentType))
            : null;

    private async ValueTask<ItemResult> RecordAsync(
        OutgoingMessage message, PublishTry publishTry, DateTime? nextTryAtUtc, CancellationToken ct)
    {
        var error = await store.RecordTryAsync(message, publishTry, nextTryAtUtc, ct);
        var repeated = error is null ? null : await store.RecordTryAsync(message, publishTry, nextTryAtUtc, ct);
        return error is not null && repeated is not null
            ? ItemResult.None
            : new ItemResult(OutcomeOf(publishTry.Outcome, nextTryAtUtc), nextTryAtUtc);
    }

    private static bool IsCancelledSend(ErrorOr<PublishReceipt> sent) => sent.IsError && sent.Error.Type == Errors.OperationCancelled;

    private static ItemOutcome OutcomeOf(PublishOutcome outcome, DateTime? nextTryAtUtc) => outcome switch
    {
        PublishOutcome.Succeeded or PublishOutcome.Suppressed => ItemOutcome.Succeeded,
        PublishOutcome.TransientError when nextTryAtUtc is not null => ItemOutcome.Retried,
        _ => ItemOutcome.Failed,
    };
}
