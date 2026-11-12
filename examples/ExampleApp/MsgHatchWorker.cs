using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Outbox;

namespace ExampleApp;

/// <summary>
/// Runs the dispatch loops of this process (D101, D102): the inbox loop for the role <see cref="MsgHatchRoles.Consumer"/>,
/// and the outbox loops for the role <see cref="MsgHatchRoles.Publisher"/>. One process with both roles runs both.
/// </summary>
/// <param name="inbox">The inbox processor.</param>
/// <param name="outbox">The outbox dispatcher.</param>
/// <param name="options">The options, with the roles of the process.</param>
public sealed class MsgHatchWorker(IInboxProcessor inbox, IOutboxDispatcher outbox, IOptions<MsgHatchOptions> options) : BackgroundService
{
    /// <inheritdoc/>
    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var roles = options.Value.Roles;
        return Task.WhenAll(
            roles.HasFlag(MsgHatchRoles.Consumer) ? inbox.RunAsync(stoppingToken).AsTask() : Task.CompletedTask,
            roles.HasFlag(MsgHatchRoles.Publisher) ? outbox.RunAsync(stoppingToken).AsTask() : Task.CompletedTask);
    }
}
