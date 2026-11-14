using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Management;
using WolverineExample.Consumer.Recipients;

namespace WolverineExample.Consumer.Startup;

/// <summary>The startup work of the consumer: it writes the scenario of each tenant whose directory has no user.</summary>
public static class ConsumerStartup
{
    /// <summary>Writes the companies, the users, and the subscriptions of each tenant of the scenario that has no user yet.</summary>
    /// <param name="services">The services of the app.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes when each tenant has its data.</returns>
    public static async Task SeedAsync(IServiceProvider services, CancellationToken ct)
    {
        foreach (var tenant in ConsumerScenario.Tenants)
        {
            var key = TenantKey.For(new TenantId(tenant.Id));
            await using var scope = services.CreateAsyncScope();
            var directory = ConsumerDirectory.For(scope.ServiceProvider, key);
            if (!Succeeded(await directory.HasUsersAsync(ct)))
            {
                await SeedTenantAsync(tenant, directory, scope.ServiceProvider.GetRequiredKeyedService<ISubscriptionManager>(key), ct);
            }
        }
    }

    private static async Task SeedTenantAsync(ScenarioTenant tenant, ConsumerDirectory directory, ISubscriptionManager manager, CancellationToken ct)
    {
        foreach (var company in tenant.Companies)
        {
            ThrowIfFailed(await directory.SaveCompanyAsync(company, ct));
        }

        foreach (var user in tenant.Users)
        {
            ThrowIfFailed(await directory.SaveUserAsync(user, ct));
        }

        foreach (var draft in ConsumerScenario.Subscriptions)
        {
            ThrowIfFailed((await manager.CreateAsync(draft, ct)).Error);
        }
    }

    private static bool Succeeded(ErrorOr<bool> result) => result.IsError ? throw Failed(result.Error) : result.Value;

    private static void ThrowIfFailed(Error? error)
    {
        if (error is not null)
        {
            throw Failed(error);
        }
    }

    private static InvalidOperationException Failed(Error error) =>
        new($"The seed of the consumer failed with {error.Type.Name}: {error.Message}");
}
