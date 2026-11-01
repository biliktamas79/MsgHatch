using System.Collections.Frozen;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Time;

namespace MsgHatch.Domain.Dispatch;

/// <summary>
/// The work of the inbox loop: it claims the received messages, and runs the steps 2 to 6 for each one in its own DI
/// scope, with the tenant of the message (section 4.8, D126, D130). It is the outer boundary of the consumer code of
/// the pipeline, so an exception of a router or a loader fails the message with <see cref="Errors.Exception"/>.
/// </summary>
internal sealed class InboxWork(
    IInboxDispatchStore store, IServiceScopeFactory scopes, FrozenDictionary<MessageType, IMessageHandler> handlers,
    DispatchOptions options, IOptionsMonitor<RetryOptions> retryOptions, TimeProvider time, ILogger logger)
    : IDispatchWork<ReceivedMessage>
{
    public DispatchTopic Topic => DispatchTopic.Inbox;

    public ValueTask BeforeClaimAsync(CancellationToken ct) => ValueTask.CompletedTask;

    public ValueTask<ErrorOr<ClaimResult<ReceivedMessage>>> ClaimAsync(int maxCount, DateTime nowUtc, CancellationToken ct) =>
        store.ClaimDueAsync(maxCount, nowUtc, options.Lease, ct);

    public DateTime? NextWakeAtUtc(ClaimResult<ReceivedMessage> result) => result.NextDueAtUtc;

    public DateTime? LeaseUntilUtcOf(ReceivedMessage message) => message.Delivery.LeaseUntilUtc;

    public async ValueTask<ItemResult> ProcessAsync(ReceivedMessage message, CancellationToken ct)
    {
        try
        {
            var processed = await RunPipelineAsync(message, ct);
            return IsStoppedByTheHost(processed, ct) ? ItemResult.None : await RecordAsync(message, processed.Error, ct);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return ItemResult.None;
        }
        catch (Exception exception)
        {
            Log.ReceivedMessageFailed(
                logger, message.Delivery.Id, message.Type.Name, message.Tenant?.Value, message.Flags, Errors.Exception.Code,
                Errors.Exception.Name, exception);
            return ItemResult.None;
        }
    }

    private async ValueTask<ErrorOr<NotificationResult>> RunPipelineAsync(ReceivedMessage message, CancellationToken ct)
    {
        if (!handlers.TryGetValue(message.Type, out var handler))
        {
            return new Error(Errors.NotFound, $"The message type {message.Type.Name} is not registered.");
        }

        await using var scope = scopes.CreateAsyncScope();
        return await Boundary.RunAsync(() => handler.ProcessAsync(message, scope.ServiceProvider, ct), ct);
    }

    private async ValueTask<ItemResult> RecordAsync(ReceivedMessage message, Error? error, CancellationToken ct)
    {
        var triedAtUtc = time.GetUtcNowDateTime();
        if (error is null)
        {
            return await store.CompleteAsync(message, triedAtUtc, ct) is null
                ? new ItemResult(ItemOutcome.Succeeded, GiveAtUtc: null)
                : ItemResult.None;
        }

        var nextTryAtUtc = NextTryAtUtc(message, error, triedAtUtc);
        return await store.FailAsync(message, TextOf(error), triedAtUtc, nextTryAtUtc, ct) is null
            ? new ItemResult(nextTryAtUtc is null ? ItemOutcome.Failed : ItemOutcome.Retried, nextTryAtUtc)
            : ItemResult.None;
    }

    private DateTime? NextTryAtUtc(ReceivedMessage message, Error error, DateTime triedAtUtc)
    {
        var tryNumber = message.Delivery.TryCount + 1;
        return error.Type.IsTransient && tryNumber < message.Delivery.MaxTryCount
            ? RetrySchedule.NextTryAtUtc(message.Delivery.Id, tryNumber, triedAtUtc, retryOptions.Get(OptionNames.Inbox))
            : null;
    }

    private static bool IsStoppedByTheHost(ErrorOr<NotificationResult> processed, CancellationToken ct) =>
        processed.IsError && processed.Error.Type == Errors.OperationCancelled && ct.IsCancellationRequested;

    private static string TextOf(Error error) => error.Message is null ? error.Type.Name : $"{error.Type.Name}: {error.Message}";
}
