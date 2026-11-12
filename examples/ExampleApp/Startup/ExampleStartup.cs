using ExampleApp.Notices;
using ExampleApp.PasswordResets;
using ExampleApp.Recipients;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Subscriptions.Management;

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
