using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Time;

namespace MsgHatch.Domain.Dispatch;

/// <summary>
/// The work of the outbox loop of one channel: it claims the messages of the channel, and publishes each claimed
/// message with the transport of the channel (section 4.8, publishing). It creates no DI scope (D130). It counts the
/// transient errors in a row of its channel, marks the channel down for all publisher instances at the threshold, and
/// runs the health check of a down channel (D86, D106).
/// </summary>
// ponytail: fixed check interval, a growing interval if long outages need it
internal sealed class OutboxChannelWork(
    ITransport transport, IOutboxDispatchStore store, DispatchOptions options, ChannelDownOptions downOptions,
    TestDeliveryOptions testDelivery, IOptionsMonitor<RetryOptions> retryOptions, TimeProvider time, ILogger logger)
    : IDispatchWork<OutgoingMessage>
{
    private volatile ChannelDown? _down;
    private int _errorsInARow;

    public DispatchTopic Topic { get; } = DispatchTopic.ForChannel(transport.Channel);

    public async ValueTask BeforeClaimAsync(CancellationToken ct)
    {
        if (_down is { } down)
        {
            await CheckWhenDueAsync(down, ct);
        }
    }

    public async ValueTask BeforeBatchAsync(CancellationToken ct)
    {
        var downChannels = await store.GetDownChannelsAsync(ct);
        if (downChannels.IsSuccess && downChannels.Value.FirstOrDefault(down => down.Channel == transport.Channel) is { } down)
        {
            await CheckWhenDueAsync(down, ct);
        }
    }

    public ValueTask<ErrorOr<ClaimResult<OutgoingMessage>>> ClaimAsync(int maxCount, DateTime nowUtc, CancellationToken ct) =>
        store.ClaimDueAsync(transport.Channel, maxCount, nowUtc, options.Lease, ct);

    public DateTime? AfterClaim(ClaimResult<OutgoingMessage> result)
    {
        if (_down is not null && result.Down is null)
        {
            Volatile.Write(ref _errorsInARow, downOptions.ErrorThreshold - 1);
        }

        _down = result.Down;
        return result.Down switch
        {
            null => result.NextDueAtUtc,
            { Reason: ChannelDownReason.Errors } down => down.NextCheckAtUtc,
            _ => null,
        };
    }

    public DateTime? LeaseUntilUtcOf(OutgoingMessage message) => message.Delivery.LeaseUntilUtc;

    public async ValueTask<ItemResult> ProcessAsync(OutgoingMessage message, CancellationToken ct)
    {
        try
        {
            return _down is null ? await PublishAsync(message, ct) : ItemResult.None;
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

    private async ValueTask CheckWhenDueAsync(ChannelDown down, CancellationToken ct)
    {
        var nowUtc = time.GetUtcNowDateTime();
        if (down.Reason != ChannelDownReason.Errors || !(down.NextCheckAtUtc <= nowUtc))
        {
            return;
        }

        var started = await store.TryStartChannelCheckAsync(transport.Channel, nowUtc, nowUtc + downOptions.CheckInterval, ct);
        if (started is not { IsSuccess: true, Value: true } || await transport.CheckHealthAsync(ct) is not null)
        {
            return;
        }

        var up = await store.SetChannelUpAsync(transport.Channel, ChannelDownReason.Errors, ct);
        if (up is { IsSuccess: true, Value: true })
        {
            Log.ChannelUp(logger, transport.Channel.Name, (nowUtc - down.SinceUtc).TotalSeconds);
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

        var isTest = message.Flags.HasFlag(MessageFlags.Test);
        if (isTest && testDelivery.Mode == TestDeliveryMode.Suppress)
        {
            var suppressed = new PublishTry(
                message.Delivery.Id, tryNumber, startedAtUtc, startedAtUtc, PublishOutcome.Suppressed, ProviderMessageId: null,
                ErrorCode: null, ErrorName: null, ErrorMessage: null);
            return await RecordAsync(message, suppressed, nextTryAtUtc: null, ct);
        }

        var redirectTo = isTest && testDelivery.Mode == TestDeliveryMode.Redirect ? testDelivery.RedirectTo : null;
        var sent = await transport.SendAsync(message, redirectTo, ct);
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
        await CountErrorsInARowAsync(publishTry, ct);
        return error is not null && repeated is not null
            ? ItemResult.None
            : new ItemResult(OutcomeOf(publishTry.Outcome, nextTryAtUtc), nextTryAtUtc);
    }

    private async ValueTask CountErrorsInARowAsync(PublishTry publishTry, CancellationToken ct)
    {
        if (publishTry.Outcome is PublishOutcome.Succeeded or PublishOutcome.PermanentError)
        {
            Volatile.Write(ref _errorsInARow, 0);
        }
        else if (publishTry.Outcome == PublishOutcome.TransientError
            && Interlocked.Increment(ref _errorsInARow) >= downOptions.ErrorThreshold)
        {
            await MarkDownAsync(publishTry, ct);
        }
    }

    private async ValueTask MarkDownAsync(PublishTry lastTry, CancellationToken ct)
    {
        var down = new ChannelDown(
            transport.Channel, ChannelDownReason.Errors, lastTry.FinishedAtUtc, lastTry.FinishedAtUtc + downOptions.CheckInterval,
            lastTry.ErrorCode, lastTry.ErrorName, Note: null);
        var wrote = await store.SetChannelDownAsync(down, ct);
        if (wrote.IsError)
        {
            return;
        }

        _down = down;
        if (wrote.Value)
        {
            Log.ChannelDown(logger, transport.Channel.Name, lastTry.ErrorCode, lastTry.ErrorName);
        }
    }

    private static bool IsCancelledSend(ErrorOr<PublishReceipt> sent) => sent.IsError && sent.Error.Type == Errors.OperationCancelled;

    private static ItemOutcome OutcomeOf(PublishOutcome outcome, DateTime? nextTryAtUtc) => outcome switch
    {
        PublishOutcome.Succeeded or PublishOutcome.Suppressed => ItemOutcome.Succeeded,
        PublishOutcome.TransientError when nextTryAtUtc is not null => ItemOutcome.Retried,
        _ => ItemOutcome.Failed,
    };
}
