using System.Collections.Frozen;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Telemetry;
using MsgHatch.Domain.Time;

namespace MsgHatch.Domain.Dispatch;

/// <summary>
/// The work of the outbox loop of one channel: it claims the messages of the channel, and publishes each claimed
/// message with the transport of the channel (section 4.8, publishing). It upgrades a message of an old envelope version
/// before the send, and records the try of the claimed message (section 4.13). It creates no DI scope (D130). It counts
/// the transient errors in a row of its transport, marks the channel down for all publisher instances at the threshold,
/// and runs the health check of a down channel (D86, D106).
/// </summary>
// ponytail: fixed check interval, a growing interval if long outages need it
internal sealed class OutboxChannelWork(
    ITransport transport, IOutboxDispatchStore store, FrozenDictionary<string, IEnvelopeUpgrade> upgrades, DispatchOptions options,
    ChannelDownOptions downOptions, TestDeliveryOptions testDelivery, IOptionsMonitor<RetryOptions> retryOptions, TimeProvider time,
    MsgHatchMetrics metrics, ILogger logger)
    : IDispatchWork<OutgoingMessage>
{
    private volatile ChannelDown? _down;
    private volatile bool _lastClaimWasDown;
    private int _errorsInARow;

    public DispatchTopic Topic { get; } = DispatchTopic.ForChannel(transport.Channel);

    public ChannelId Channel => transport.Channel;

    /// <summary>True when the last claim of the loop gave a down state, for the gauge <c>msghatch.channel.down</c>.</summary>
    public bool LastClaimWasDown => _lastClaimWasDown;

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
        _lastClaimWasDown = result.Down is not null;
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
        if (IsCheckDue(down, nowUtc) && await TryStartCheckAsync(nowUtc, ct) && await transport.CheckHealthAsync(ct) is null)
        {
            await MarkUpAsync(down, nowUtc, ct);
        }
    }

    private static bool IsCheckDue(ChannelDown down, DateTime nowUtc) =>
        down.Reason == ChannelDownReason.Errors && down.NextCheckAtUtc <= nowUtc;

    private async ValueTask<bool> TryStartCheckAsync(DateTime nowUtc, CancellationToken ct) =>
        await store.TryStartChannelCheckAsync(transport.Channel, nowUtc, nowUtc + downOptions.CheckInterval, ct)
            is { IsSuccess: true, Value: true };

    private async ValueTask MarkUpAsync(ChannelDown down, DateTime nowUtc, CancellationToken ct)
    {
        var up = await store.SetChannelUpAsync(transport.Channel, ChannelDownReason.Errors, ct);
        if (up is { IsSuccess: true, Value: true })
        {
            Log.ChannelUp(logger, transport.Channel.Name, (nowUtc - down.SinceUtc).TotalSeconds);
        }
    }

    private async ValueTask<ItemResult> PublishAsync(OutgoingMessage message, CancellationToken ct)
    {
        var tryNumber = message.Delivery.TryCount + 1;
        var sendStarted = time.GetTimestamp();
        var startedAtUtc = time.GetUtcNowDateTime();
        if (TryWithNoSend(message, tryNumber, startedAtUtc) is { } noSend)
        {
            return await RecordAsync(message, noSend, nextTryAtUtc: null, ct);
        }

        var current = CurrentVersionOf(message);
        if (current.IsError)
        {
            var unsupported = TryOf(message, tryNumber, startedAtUtc, startedAtUtc, current.Error);
            return await RecordAsync(message, unsupported, NextTryAtUtc(message, unsupported), ct);
        }

        return await SendAsync(message, current.Value, tryNumber, sendStarted, startedAtUtc, ct);
    }

    private PublishTry? TryWithNoSend(OutgoingMessage message, int tryNumber, DateTime startedAtUtc) =>
        message.Delivery.ExpiresAtUtc <= startedAtUtc ? NoSendTry(message, tryNumber, startedAtUtc, PublishOutcome.Expired, Errors.Expired)
        : IsTest(message) && testDelivery.Mode == TestDeliveryMode.Suppress
            ? NoSendTry(message, tryNumber, startedAtUtc, PublishOutcome.Suppressed, error: null)
        : null;

    private static PublishTry NoSendTry(OutgoingMessage message, int tryNumber, DateTime atUtc, PublishOutcome outcome, ErrorType? error) =>
        new(message.Delivery.Id, tryNumber, atUtc, atUtc, outcome, ProviderMessageId: null, error?.Code, error?.Name, ErrorMessage: null);

    private ErrorOr<OutgoingMessage> CurrentVersionOf(OutgoingMessage message) =>
        upgrades.TryGetValue(message.ContentType, out var upgrade) ? upgrade.Upgrade(message) : message;

    private async ValueTask<ItemResult> SendAsync(
        OutgoingMessage message, OutgoingMessage current, int tryNumber, long sendStarted, DateTime startedAtUtc, CancellationToken ct)
    {
        using var span = SendSpan.Start(message, tryNumber);
        var sent = await transport.SendAsync(current, RedirectOf(message), ct);
        var finishedAtUtc = time.GetUtcNowDateTime();
        if (IsCancelledSend(sent))
        {
            return ItemResult.None;
        }

        var publishTry = TryOf(message, tryNumber, startedAtUtc, finishedAtUtc, sent);
        span.Finish(publishTry.Outcome, sent.Error);
        metrics.RecordSendDuration(transport.Channel, publishTry.Outcome, time.GetElapsedTime(sendStarted));
        var result = await RecordAsync(message, publishTry, NextTryAtUtc(message, publishTry), ct);
        await CountErrorsInARowAsync(publishTry, ct);
        return result;
    }

    private string? RedirectOf(OutgoingMessage message) =>
        IsTest(message) && testDelivery.Mode == TestDeliveryMode.Redirect ? testDelivery.RedirectTo : null;

    private static bool IsTest(OutgoingMessage message) => message.Flags.HasFlag(MessageFlags.Test);

    private static PublishTry TryOf(
        OutgoingMessage message, int tryNumber, DateTime startedAtUtc, DateTime finishedAtUtc, ErrorOr<PublishReceipt> sent)
    {
        if (sent.IsError)
        {
            var outcome = sent.Error.Type.IsTransient ? PublishOutcome.TransientError : PublishOutcome.PermanentError;
            return new PublishTry(
                message.Delivery.Id, tryNumber, startedAtUtc, finishedAtUtc, outcome, ProviderMessageId: null, sent.Error.Type.Code,
                sent.Error.Type.Name, sent.Error.Message);
        }

        return new PublishTry(
            message.Delivery.Id, tryNumber, startedAtUtc, finishedAtUtc, PublishOutcome.Succeeded, sent.Value.ProviderMessageId,
            ErrorCode: null, ErrorName: null, ErrorMessage: null);
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
        if (error is not null && repeated is not null)
        {
            return ItemResult.None;
        }

        var outcome = OutcomeOf(publishTry.Outcome, nextTryAtUtc);
        MeasureRecordedTry(message, publishTry, outcome);
        return new ItemResult(outcome, nextTryAtUtc);
    }

    private void MeasureRecordedTry(OutgoingMessage message, PublishTry publishTry, ItemOutcome outcome)
    {
        metrics.RecordTry(message, publishTry.Outcome);
        if (outcome == ItemOutcome.Failed)
        {
            metrics.RecordFailed(message, publishTry.ErrorName);
        }

        if (publishTry.Outcome == PublishOutcome.Succeeded)
        {
            metrics.RecordDeliveryDelay(message, publishTry.FinishedAtUtc - message.Delivery.CreatedAtUtc);
        }
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
