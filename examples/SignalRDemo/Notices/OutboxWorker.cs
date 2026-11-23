using MsgHatch.Domain.Outbox;

namespace SignalRDemo.Notices;

/// <summary>
/// Runs the outbox loop, which sends each stored notification through the SignalR transport. The publish endpoint
/// routes each notification into the outbox with no inbox, so the demo runs no inbox loop.
/// </summary>
/// <param name="outbox">The outbox dispatcher.</param>
public sealed class OutboxWorker(IOutboxDispatcher outbox) : BackgroundService
{
    /// <inheritdoc/>
    protected override Task ExecuteAsync(CancellationToken stoppingToken) => outbox.RunAsync(stoppingToken).AsTask();
}
