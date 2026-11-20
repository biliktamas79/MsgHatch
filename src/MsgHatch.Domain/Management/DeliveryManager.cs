using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Time;

namespace MsgHatch.Domain.Management;

/// <summary>
/// The in-process delivery queries and commands of one tenant key (section 4.19, D289): the message members use the tenant
/// stores of the key, and the channel members, the test data delete, and the purge use the dispatch stores of all tenants.
/// The inbox store is optional. The content and the route trace are JSON text of the content type registry (D290).
/// </summary>
// ponytail: a bulk action runs inside the call, a background job if a UI needs very large actions
internal sealed class DeliveryManager(
    TenantKey key,
    IOutboxStore outbox,
    IInboxStore? inbox,
    IOutboxDispatchStore outboxDispatch,
    IInboxDispatchStore? inboxDispatch,
    IMessageRegistry registry,
    IDispatchSignal signal,
    IOptionsMonitor<RetryOptions> retryOptions,
    MsgHatchJson json,
    TimeProvider time) : IDeliveryQueries, IDeliveryCommands
{
    private const int _bulkPageLimit = 500;

    public static DeliveryManager Create(IServiceProvider tenantServices, TenantKey key) =>
        new(
            key,
            tenantServices.GetRequiredService<IOutboxStore>(),
            tenantServices.GetService<IInboxStore>(),
            tenantServices.GetRequiredService<IOutboxDispatchStore>(),
            tenantServices.GetService<IInboxDispatchStore>(),
            tenantServices.GetRequiredService<IMessageRegistry>(),
            tenantServices.GetRequiredService<IDispatchSignal>(),
            tenantServices.GetRequiredService<IOptionsMonitor<RetryOptions>>(),
            tenantServices.GetRequiredService<MsgHatchJson>(),
            tenantServices.GetRequiredService<TimeProvider>());

    public async ValueTask<ErrorOr<IReadOnlyList<ChannelStatus>>> GetChannelsAsync(CancellationToken ct)
    {
        var downs = await outboxDispatch.GetDownChannelsAsync(ct);
        if (downs.IsError)
        {
            return downs.Error!;
        }

        var counts = await outboxDispatch.CountAsync(new TrafficQuery(Status: DeliveryStatus.Pending, IncludeTest: true), ct);
        return counts.IsError
            ? counts.Error!
            : ErrorOr<IReadOnlyList<ChannelStatus>>.ForValue([.. registry.Channels.Select(descriptor => StatusOf(descriptor, downs.Value, counts.Value))]);
    }

    public async ValueTask<Error?> PauseChannelAsync(ChannelId channel, string? note, CancellationToken ct)
    {
        if ((DeliveryRules.CheckPause(key, note) ?? UnknownChannelError(channel)) is { } error)
        {
            return error;
        }

        var pause = new ChannelDown(channel, ChannelDownReason.Operator, time.GetUtcNowDateTime(), NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, note);
        return (await outboxDispatch.SetChannelDownAsync(pause, ct)).Error;
    }

    public async ValueTask<Error?> ResumeChannelAsync(ChannelId channel, CancellationToken ct)
    {
        if ((DeliveryRules.CheckGlobalAction(key) ?? UnknownChannelError(channel)) is { } error)
        {
            return error;
        }

        var resumed = await outboxDispatch.SetChannelUpAsync(channel, reason: null, time.GetUtcNowDateTime(), ct);
        if (resumed.IsError)
        {
            return resumed.Error;
        }

        await NotifyIgnoringErrorsAsync(DispatchTopic.ForChannel(channel), ct);
        return null;
    }

    public async ValueTask<ErrorOr<TrafficReport>> GetTrafficAsync(TrafficQuery query, CancellationToken ct)
    {
        if (DeliveryRules.Check(query) is { } error)
        {
            return error;
        }

        var inboxCounts = inbox is null ? ErrorOr<IReadOnlyList<MessageCount>>.ForValue([]) : await inbox.CountAsync(query, ct);
        if (inboxCounts.IsError)
        {
            return inboxCounts.Error!;
        }

        var outboxCounts = await outbox.CountAsync(query, ct);
        return outboxCounts.IsError ? outboxCounts.Error! : new TrafficReport(inboxCounts.Value, outboxCounts.Value);
    }

    public async ValueTask<ErrorOr<MessagePage<ReceivedMessageSummary>>> GetReceivedMessagesAsync(InboxQuery query, CancellationToken ct)
    {
        return DeliveryRules.Check(query) is { } error ? error
            : inbox is null ? new MessagePage<ReceivedMessageSummary>([], Next: null)
            : await inbox.QueryAsync(query, ct);
    }

    public async ValueTask<ErrorOr<MessagePage<OutgoingMessageSummary>>> GetOutgoingMessagesAsync(OutboxQuery query, CancellationToken ct)
    {
        return DeliveryRules.Check(query) is { } error ? error : await outbox.QueryAsync(query, ct);
    }

    public async ValueTask<ErrorOr<OutgoingMessageDetail>> GetOutgoingMessageAsync(Guid id, CancellationToken ct)
    {
        var history = await outbox.GetAsync(id, ct);
        if (history.IsError)
        {
            return history.Error!;
        }

        if (history.Value is not { } found)
        {
            return NotFound(id);
        }

        var trace = TraceTextOf(found.Message.Trace);
        return trace.IsError ? trace.Error! : new OutgoingMessageDetail(OutgoingMessageSummary.Of(found.Message), trace.Value, found.Tries);
    }

    public async ValueTask<ErrorOr<MessageContent>> GetReceivedContentAsync(Guid id, CancellationToken ct)
    {
        if (inbox is null)
        {
            return NotFound(id);
        }

        var message = await inbox.GetAsync(id, ct);
        return message.IsError ? message.Error! : message.Value is { } found ? ContentOf(id, json.Serialize(found)) : NotFound(id);
    }

    public async ValueTask<ErrorOr<MessageContent>> GetOutgoingContentAsync(Guid id, CancellationToken ct)
    {
        var history = await outbox.GetAsync(id, ct);
        return history.IsError ? history.Error! : history.Value is { } found ? ContentOf(id, json.Serialize(found.Message)) : NotFound(id);
    }

    public async ValueTask<ErrorOr<DeliveryActionResult>> RetryReceivedAsync(InboxQuery query, CancellationToken ct)
    {
        if (DeliveryRules.CheckBulkAction(query) is { } error)
        {
            return error;
        }

        if (inbox is null)
        {
            return new DeliveryActionResult(0, 0);
        }

        var addedTries = retryOptions.Get(OptionNames.Inbox).MaxTryCount;
        var result = await RunOnPagesAsync(
            after => inbox.QueryAsync(query with { Limit = _bulkPageLimit, After = after }, ct),
            async page => await inbox.RetryAsync(IdsOf(page), time.GetUtcNowDateTime(), addedTries, ct));
        if (result is { IsSuccess: true, Value.Changed: > 0 })
        {
            await NotifyIgnoringErrorsAsync(DispatchTopic.Inbox, ct);
        }

        return result;
    }

    public async ValueTask<ErrorOr<DeliveryActionResult>> CancelReceivedAsync(InboxQuery query, CancellationToken ct)
    {
        return DeliveryRules.CheckBulkAction(query) is { } error ? error
            : inbox is null ? new DeliveryActionResult(0, 0)
            : await RunOnPagesAsync(
                after => inbox.QueryAsync(query with { Limit = _bulkPageLimit, After = after }, ct),
                async page => await inbox.CancelAsync(IdsOf(page), time.GetUtcNowDateTime(), ct));
    }

    public async ValueTask<ErrorOr<DeliveryActionResult>> RetryOutgoingAsync(OutboxQuery query, CancellationToken ct)
    {
        if (DeliveryRules.CheckBulkAction(query) is { } error)
        {
            return error;
        }

        HashSet<ChannelId> changedChannels = [];
        var result = await RunOnPagesAsync(
            after => outbox.QueryAsync(query with { Limit = _bulkPageLimit, After = after }, ct),
            page => RetryEachContentTypeAsync(page, changedChannels, ct));
        foreach (var channel in changedChannels)
        {
            await NotifyIgnoringErrorsAsync(DispatchTopic.ForChannel(channel), ct);
        }

        return result;
    }

    public async ValueTask<ErrorOr<DeliveryActionResult>> CancelOutgoingAsync(OutboxQuery query, CancellationToken ct)
    {
        return DeliveryRules.CheckBulkAction(query) is { } error ? error
            : await RunOnPagesAsync(
                after => outbox.QueryAsync(query with { Limit = _bulkPageLimit, After = after }, ct),
                async page => await outbox.CancelAsync(IdsOf(page), time.GetUtcNowDateTime(), ct));
    }

    public async ValueTask<ErrorOr<DeletedMessages>> DeleteTestDataAsync(CancellationToken ct)
    {
        if (DeliveryRules.CheckGlobalAction(key) is { } error)
        {
            return error;
        }

        var inboxDeleted = inboxDispatch is null ? 0 : await inboxDispatch.DeleteTestDataAsync(ct);
        if (inboxDeleted.IsError)
        {
            return inboxDeleted.Error!;
        }

        var outboxDeleted = await outboxDispatch.DeleteTestDataAsync(ct);
        return outboxDeleted.IsError ? outboxDeleted.Error! : new DeletedMessages(inboxDeleted.Value, outboxDeleted.Value);
    }

    public async ValueTask<ErrorOr<DeletedMessages>> PurgeAsync(DateTime finishedBeforeUtc, int maxCount, CancellationToken ct)
    {
        if ((DeliveryRules.CheckPurge(key, maxCount) ?? FinishTimeError(finishedBeforeUtc)) is { } error)
        {
            return error;
        }

        var inboxPurged = inboxDispatch is null ? 0 : await inboxDispatch.PurgeAsync(finishedBeforeUtc, maxCount, ct);
        if (inboxPurged.IsError)
        {
            return inboxPurged.Error!;
        }

        var outboxPurged = await outboxDispatch.PurgeAsync(finishedBeforeUtc, maxCount, ct);
        return outboxPurged.IsError ? outboxPurged.Error! : new DeletedMessages(inboxPurged.Value, outboxPurged.Value);
    }

    /// <summary>
    /// Runs an action on each page of a query, from the first page to the page with no next cursor, and sums the matched
    /// and the changed messages. The cursor stays correct while the action changes the messages (D112).
    /// </summary>
    private static async Task<ErrorOr<DeliveryActionResult>> RunOnPagesAsync<T>(
        Func<MessageCursor?, ValueTask<ErrorOr<MessagePage<T>>>> query, Func<MessagePage<T>, Task<ErrorOr<int>>> action)
    {
        var (matched, changed) = (0, 0);
        MessageCursor? after = null;
        do
        {
            var page = await query(after);
            if (page.IsError)
            {
                return page.Error!;
            }

            var ids = await action(page.Value);
            if (ids.IsError)
            {
                return ids.Error!;
            }

            matched += page.Value.Items.Count;
            changed += ids.Value;
            after = page.Value.Next;
        }
        while (after is not null);

        return new DeliveryActionResult(matched, changed);
    }

    /// <summary>Retries the messages of a page with one store call for each content type, with the retry options of that content type (D87).</summary>
    private async Task<ErrorOr<int>> RetryEachContentTypeAsync(
        MessagePage<OutgoingMessageSummary> page, HashSet<ChannelId> changedChannels, CancellationToken ct)
    {
        var changed = 0;
        foreach (var group in page.Items.GroupBy(static summary => summary.ContentType, StringComparer.Ordinal))
        {
            var retried = await outbox.RetryAsync(
                [.. group.Select(static summary => summary.Delivery.Id)], time.GetUtcNowDateTime(), retryOptions.Get(group.Key).MaxTryCount, ct);
            if (retried.IsError)
            {
                return retried.Error!;
            }

            if (retried.Value > 0)
            {
                changedChannels.UnionWith(group.Select(static summary => summary.Channel));
            }

            changed += retried.Value;
        }

        return changed;
    }

    /// <summary>
    /// Notifies the loops of a topic after a change. A failed notification does not fail the change, which the store
    /// already holds: a loop claims the messages at its next poll.
    /// </summary>
    private async Task NotifyIgnoringErrorsAsync(DispatchTopic topic, CancellationToken ct) => _ = await signal.NotifyAsync(topic, ct);

    private static ChannelStatus StatusOf(ChannelDescriptor descriptor, IReadOnlyList<ChannelDown> downs, IReadOnlyList<MessageCount> counts)
    {
        MessageCount[] pending = [.. counts.Where(count => count.Channel == descriptor.Id)];
        return new ChannelStatus(
            descriptor,
            downs.FirstOrDefault(down => down.Channel == descriptor.Id),
            pending.Where(static count => !count.Test).Sum(static count => count.Count),
            pending.Where(static count => count.Test).Sum(static count => count.Count),
            pending.Min(static count => count.OldestDueAtUtc));
    }

    private ErrorOr<string?> TraceTextOf(RouteTrace? trace) =>
        trace is null ? ErrorOr<string?>.ForValue(null) : json.SerializeTrace(trace).ThenIfSuccess(static text => ErrorOr<string?>.ForValue(text));

    private static ErrorOr<MessageContent> ContentOf(Guid id, ErrorOr<byte[]> utf8Json) =>
        utf8Json.IsError ? utf8Json.Error : new MessageContent(id, Encoding.UTF8.GetString(utf8Json.Value));

    private static Guid[] IdsOf(MessagePage<ReceivedMessageSummary> page) => [.. page.Items.Select(static summary => summary.Delivery.Id)];

    private static Guid[] IdsOf(MessagePage<OutgoingMessageSummary> page) => [.. page.Items.Select(static summary => summary.Delivery.Id)];

    private Error? UnknownChannelError(ChannelId channel) =>
        registry.FindChannel(channel) is null ? new Error(Errors.NotFound, $"The channel {channel.Name} is not registered.") : null;

    private Error? FinishTimeError(DateTime finishedBeforeUtc) =>
        finishedBeforeUtc > time.GetUtcNowDateTime() ? new Error(Errors.ValidationFailed, "finishedBefore is after the current time.") : null;

    private static Error NotFound(Guid id) => new(Errors.NotFound, $"The message {id:D} is not stored for this tenant.");
}
