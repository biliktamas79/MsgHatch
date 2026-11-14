using System.Net.Http;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WolverineExample.Contracts;

namespace WolverineExample.UserSimulator;

/// <summary>
/// Runs one SignalR client for each simulated user, with the token of that user. The hub of the consumer adds each
/// connection to the user name and to the group of the company of the user, so a client gets its user messages, its
/// company messages, and the messages of its tenant, which the consumer sends to each company group. Each client logs
/// each notification. A client that cannot connect tries again every 2 seconds, so the simulator can start before the
/// consumer.
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
        (SimulatedUser User, HubConnection Connection)[] clients = [.. settings.Users.Select(user => (user, ConnectionOf(settings.HubUrl!, user)))];
        try
        {
            await Task.WhenAll(clients.Select(client => StartAsync(client.User, client.Connection, stoppingToken)));
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

    private HubConnection ConnectionOf(Uri hub, SimulatedUser user)
    {
        var connection = new HubConnectionBuilder()
            .WithUrl(hub, o => o.AccessTokenProvider = () => Task.FromResult<string?>(tokens.Create(user)))
            .WithAutomaticReconnect()
            .Build();
        foreach (var messageType in DomainEvents.Names)
        {
            connection.On<ReceivedNotification>(
                messageType, notification => logger.NotificationReceived(user.Tenant, user.UserId, messageType, notification.Text));
        }

        connection.Reconnected += _ =>
        {
            logger.UserReconnected(user.Tenant, user.UserId);
            return Task.CompletedTask;
        };
        connection.Closed += _ =>
        {
            logger.UserDisconnected(user.Tenant, user.UserId);
            return Task.CompletedTask;
        };
        return connection;
    }

    private async Task StartAsync(SimulatedUser user, HubConnection connection, CancellationToken ct)
    {
        while (true)
        {
            try
            {
                await connection.StartAsync(ct);
                logger.UserConnected(user.Tenant, user.UserId);
                return;
            }
            catch (HttpRequestException exception)
            {
                logger.ConnectFailed(user.Tenant, user.UserId, (int?)exception.StatusCode, exception.HttpRequestError, _retryDelay);
                await Task.Delay(_retryDelay, time, ct);
            }
        }
    }
}
