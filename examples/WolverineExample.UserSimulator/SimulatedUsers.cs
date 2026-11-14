using System.Net.Http;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WolverineExample.Contracts;

namespace WolverineExample.UserSimulator;

/// <summary>
/// Runs one SignalR client for each simulated user, with the token of that user. The hub of the consumer adds each
/// connection to the user name and to the group of the company of the user, so a client gets its user messages, its
/// company messages, and the messages of its tenant, which the consumer sends to each company group. After each connect
/// and reconnect, a client asks the hub for the notifications that it possibly missed, with the id of its last
/// notification. Each client logs each notification once, live or from the catch-up. A client that cannot connect, or
/// that lost its connection, tries again every 2 seconds with no end, so the simulator can start before the consumer and
/// survives a restart of the consumer.
/// </summary>
/// <param name="options">The settings, with the hub URL and the users.</param>
/// <param name="tokens">The token source.</param>
/// <param name="time">The clock of the delays.</param>
/// <param name="logger">The logger.</param>
public sealed class SimulatedUsers(IOptions<UserSimulatorOptions> options, UserTokens tokens, TimeProvider time, ILogger<SimulatedUsers> logger)
    : BackgroundService
{
    private static readonly TimeSpan _retryDelay = TimeSpan.FromSeconds(2);

    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var settings = options.Value;
        Client[] clients = [.. settings.Users.Select(user => ClientOf(settings.HubUrl!, user, stoppingToken))];
        try
        {
            await Task.WhenAll(clients.Select(client => StartAsync(client, stoppingToken)));
            await Task.Delay(Timeout.InfiniteTimeSpan, time, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
        finally
        {
            foreach (var client in clients)
            {
                await client.Connection.DisposeAsync();
            }
        }
    }

    private Client ClientOf(Uri hub, SimulatedUser user, CancellationToken ct)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(hub, o => o.AccessTokenProvider = () => Task.FromResult<string?>(tokens.Create(user)))
            .WithAutomaticReconnect(new RetryForever())
            .Build();
        var client = new Client(user, connection, new SeenNotifications());
        foreach (var messageType in DomainEvents.Names)
        {
            connection.On<ReceivedNotification>(messageType, notification =>
            {
                if (client.Seen.TryAdd(notification.Id))
                {
                    logger.NotificationReceived(user.Tenant, user.UserId, messageType, notification.Text);
                }
            });
        }

        connection.Reconnected += async _ =>
        {
            logger.UserReconnected(user.Tenant, user.UserId);
            await CatchUpAsync(client, ct);
        };
        connection.Closed += _ =>
        {
            logger.UserDisconnected(user.Tenant, user.UserId);
            return Task.CompletedTask;
        };
        return client;
    }

    private async Task StartAsync(Client client, CancellationToken ct)
    {
        while (true)
        {
            try
            {
                await client.Connection.StartAsync(ct);
                logger.UserConnected(client.User.Tenant, client.User.UserId);
                await CatchUpAsync(client, ct);
                return;
            }
            catch (HttpRequestException exception)
            {
                logger.ConnectFailed(client.User.Tenant, client.User.UserId, (int?)exception.StatusCode, exception.HttpRequestError, _retryDelay);
                await Task.Delay(_retryDelay, time, ct);
            }
        }
    }

    private async Task CatchUpAsync(Client client, CancellationToken ct)
    {
        try
        {
            var missed = await client.Connection.InvokeAsync<IReadOnlyList<MissedNotification>>(
                HubMethods.GetMissedNotifications, client.Seen.LastId, ct);
            foreach (var notification in missed)
            {
                if (client.Seen.TryAdd(notification.Id))
                {
                    logger.NotificationCaughtUp(client.User.Tenant, client.User.UserId, notification.MessageType, notification.Text);
                }
            }
        }
        catch (HubException exception)
        {
            logger.CatchUpFailed(client.User.Tenant, client.User.UserId, exception);
        }
    }

    private sealed record Client(SimulatedUser User, HubConnection Connection, SeenNotifications Seen);

    /// <summary>
    /// Reconnects at once, and then every 2 seconds with no end. The default policy of SignalR stops after 4 tries in
    /// about 42 seconds, and a closed connection stays closed, so a restart of the consumer that took longer lost the user.
    /// </summary>
    private sealed class RetryForever : IRetryPolicy
    {
        public TimeSpan? NextRetryDelay(RetryContext retryContext) => retryContext.PreviousRetryCount == 0 ? TimeSpan.Zero : _retryDelay;
    }
}
