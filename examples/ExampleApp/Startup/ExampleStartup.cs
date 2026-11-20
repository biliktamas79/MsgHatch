using ExampleApp.Channels;
using ExampleApp.Notices;
using ExampleApp.PasswordResets;
using ExampleApp.Recipients;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Email;
using MsgHatch.SignalR;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Verification;

namespace ExampleApp.Startup;

/// <summary>
/// The startup work of the example (D157): it writes the shared scenario only into an empty directory, and then accepts
/// the 7 example events, with new message ids at each start.
/// </summary>
public static class ExampleStartup
{
    /// <summary>Writes the scenario when the directory has no user, and accepts the example events.</summary>
    /// <param name="services">The services of the app.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when the events are in the inbox.</returns>
    public static async Task RunAsync(IServiceProvider services, CancellationToken ct)
    {
        await using var scope = services.CreateAsyncScope();
        var directory = ActivatorUtilities.CreateInstance<ExampleRecipientDirectory>(scope.ServiceProvider, TenantKey.None);
        if (!Succeeded(await directory.HasUsersAsync(ct)))
        {
            await SeedAsync(directory, scope.ServiceProvider.GetRequiredService<ISubscriptionCommands>(), ct);
        }

        await AcceptEventsAsync(scope.ServiceProvider.GetRequiredService<INotificationService>(), ct);
    }

    /// <summary>Writes the companies and the users into the directory, and the subscriptions through the commands.</summary>
    /// <param name="directory">The directory of one tenant.</param>
    /// <param name="commands">The subscription commands of the same tenant.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when the scenario is written.</returns>
    public static async Task SeedAsync(ExampleRecipientDirectory directory, ISubscriptionCommands commands, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(commands);
        foreach (var company in ExampleScenario.Companies)
        {
            ThrowIfFailed(await directory.SaveCompanyAsync(company, ct));
        }

        foreach (var user in ExampleScenario.Users)
        {
            ThrowIfFailed(await directory.SaveUserAsync(user, ct));
        }

        foreach (var draft in ExampleScenario.Subscriptions)
        {
            ThrowIfFailed((await commands.CreateAsync(draft, ct)).Error);
        }
    }

    /// <summary>
    /// Runs the routing verification with the test users Client 1 and Client 3 and the languages <c>en</c> and <c>hu</c>,
    /// and logs the report (section 4.18, D162). The app calls it at startup in the <c>Development</c> environment.
    /// </summary>
    /// <param name="services">The services of the app.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The report.</returns>
    public static async Task<RoutingVerificationReport> VerifyRoutingAsync(IServiceProvider services, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        await using var scope = services.CreateAsyncScope();
        var report = await scope.ServiceProvider.GetRequiredService<IRoutingVerifier>()
            .VerifyAsync(new RoutingVerificationRequest([UserId.From(1), UserId.From(3)], ["en", "hu"]), ct);
        ThrowIfFailed(report.Error);
        LogReport(services.GetRequiredService<ILoggerFactory>().CreateLogger(typeof(ExampleStartup).FullName!), report.Value);
        return report.Value;
    }

    /// <summary>
    /// Writes an example history of the channel states into the outbox dispatch store, so that the History dialog of the admin
    /// UI has data (D288). The times come before the current time of <see cref="TimeProvider"/>. Email gets four down states:
    /// one 40 days ago, before the 30-day window, a pause with the note "Example maintenance", and two down states for errors.
    /// SignalR gets one down state for errors, and Broker gets no row. Each down state ends with an up row, so each channel is
    /// up after the seed. A channel with a state row gets no seed, so a second call adds nothing. The app calls it at startup
    /// in the <c>Development</c> environment.
    /// </summary>
    /// <param name="services">The services of the app.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when the rows are in the store.</returns>
    public static async Task SeedChannelHistoryAsync(IServiceProvider services, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(services);
        await using var scope = services.CreateAsyncScope();
        var channels = await scope.ServiceProvider.GetRequiredKeyedService<IRegistryQueries>(TenantKey.None).GetChannelsAsync(ct);
        ThrowIfFailed(channels.Error);
        var store = services.GetRequiredService<IOutboxDispatchStore>();
        var nowUtc = services.GetRequiredService<TimeProvider>().GetUtcNow().UtcDateTime;
        foreach (var channel in channels.Value.Where(static channel => channel.Current is null))
        {
            foreach (var (down, duration) in ExampleHistoryOf(channel.Id, nowUtc))
            {
                ThrowIfFailed((await store.SetChannelDownAsync(down, ct)).Error);
                ThrowIfFailed((await store.SetChannelUpAsync(down.Channel, down.Reason, down.SinceUtc + duration, ct)).Error);
            }
        }
    }

    /// <summary>Logs the counts of a report, each failed row as a warning, and each skipped row (D162).</summary>
    /// <param name="logger">The logger.</param>
    /// <param name="report">The report.</param>
    public static void LogReport(ILogger logger, RoutingVerificationReport report)
    {
        ArgumentNullException.ThrowIfNull(report);
        logger.RoutingVerified(report.Passed, report.Failed, report.Skipped);
        foreach (var check in report.Checks)
        {
            if (check.Outcome == RoutingCheckOutcome.Failed)
            {
                logger.RoutingCheckFailed(
                    check.Type.Name, check.TestCase, check.Owner?.Level, check.Channel?.Name, check.Language, check.ErrorCode, check.ErrorName);
            }
            else if (check.Outcome == RoutingCheckOutcome.Skipped)
            {
                logger.RoutingCheckSkipped(check.Type.Name, check.TestCase, check.Owner?.Level, check.Channel?.Name, check.Language);
            }
        }
    }

    private static async Task AcceptEventsAsync(INotificationService notifications, CancellationToken ct)
    {
        int[] clients = [1, 2];
        Accepted(await notifications.AcceptAsync(new EventA(clients, null, "Your report is ready."), NewContext(), ct));
        Accepted(await notifications.AcceptAsync(new EventB(clients, null, "A new invoice is open."), NewContext(), ct));
        Accepted(await notifications.AcceptAsync(new EventC(clients, null, "The import finished."), NewContext(), ct));
        Accepted(await notifications.AcceptAsync(new EventD(clients, null, "A contract changed."), NewContext(), ct));
        Accepted(await notifications.AcceptAsync(new EventE(clients, null, "The weekly summary is ready."), NewContext(), ct));
        Accepted(await notifications.AcceptAsync(new PasswordReset(1, "client1@acme.example", "en"), NewContext(), ct));
        Accepted(await notifications.AcceptAsync(new EventC(null, ExampleScenario.Acme, "The maintenance starts at 22:00."), NewContext(), ct));
    }

    /// <summary>The down states of the example history of one channel, oldest first, each with the time until its up row (D288).</summary>
    private static (ChannelDown Down, TimeSpan Duration)[] ExampleHistoryOf(ChannelId channel, DateTime nowUtc) =>
        channel == EmailChannel.Id
            ?
            [
                (DownForErrors(channel, nowUtc.AddDays(-40)), TimeSpan.FromHours(2)),
                (DownForErrors(channel, nowUtc.AddDays(-12)), TimeSpan.FromMinutes(45)),
                (new ChannelDown(channel, ChannelDownReason.Operator, nowUtc.AddDays(-5), NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, "Example maintenance"),
                    TimeSpan.FromHours(3)),
                (DownForErrors(channel, nowUtc.AddHours(-26)), TimeSpan.FromMinutes(7)),
            ]
            : channel == SignalRChannel.Id ? [(DownForErrors(channel, nowUtc.AddDays(-20)), TimeSpan.FromHours(6))]
            : [];

    private static ChannelDown DownForErrors(ChannelId channel, DateTime sinceUtc) =>
        new(channel, ChannelDownReason.Errors, sinceUtc, sinceUtc.AddSeconds(30), Errors.Unavailable.Code, Errors.Unavailable.Name, Note: null);

    private static MessageContext NewContext() => new(Guid.NewGuid());

    private static bool Succeeded(ErrorOr<bool> result) => result.IsError ? throw Failed(result.Error) : result.Value;

    private static void Accepted(ErrorOr<Guid> result) => ThrowIfFailed(result.Error);

    private static void ThrowIfFailed(Error? error)
    {
        if (error is not null)
        {
            throw Failed(error);
        }
    }

    private static InvalidOperationException Failed(Error error) =>
        new($"The startup work of the example failed with {error.Type.Name}: {error.Message}");
}
