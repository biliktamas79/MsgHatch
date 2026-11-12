using ExampleApp.Channels;
using ExampleApp.Notices;
using ExampleApp.PasswordResets;
using ExampleApp.Recipients;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
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
            await SeedAsync(directory, scope.ServiceProvider.GetRequiredService<ISubscriptionManager>(), ct);
        }

        await AcceptEventsAsync(scope.ServiceProvider.GetRequiredService<INotificationService>(), ct);
    }

    /// <summary>Writes the companies and the users into the directory, and the subscriptions through the manager.</summary>
    /// <param name="directory">The directory of one tenant.</param>
    /// <param name="manager">The subscription manager of the same tenant.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when the scenario is written.</returns>
    public static async Task SeedAsync(ExampleRecipientDirectory directory, ISubscriptionManager manager, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(directory);
        ArgumentNullException.ThrowIfNull(manager);
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
            ThrowIfFailed((await manager.CreateAsync(draft, ct)).Error);
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
