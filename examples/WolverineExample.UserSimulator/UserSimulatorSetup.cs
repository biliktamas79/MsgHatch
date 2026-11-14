using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace WolverineExample.UserSimulator;

/// <summary>The registration of the user simulator.</summary>
public static class UserSimulatorSetup
{
    /// <summary>
    /// Registers the validated options of the sections <c>UserSimulator</c> and <c>Authentication:Schemes:Bearer</c>,
    /// the token source, and the hosted service with one SignalR client for each simulated user.
    /// </summary>
    /// <param name="services">The services of the host.</param>
    /// <returns>The services.</returns>
    public static IServiceCollection AddUserSimulator(this IServiceCollection services)
    {
        services.AddOptions<UserSimulatorOptions>().BindConfiguration(UserSimulatorOptions.SectionName).ValidateOnStart();
        services.AddOptions<UserJwtsOptions>().BindConfiguration(UserJwtsOptions.SectionName).ValidateOnStart();
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<UserSimulatorOptions>, UserSimulatorOptionsValidator>());
        services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<UserJwtsOptions>, UserJwtsOptionsValidator>());
        services.TryAddSingleton(TimeProvider.System);
        services.AddSingleton<UserTokens>();
        services.AddHostedService<SimulatedUsers>();
        return services;
    }
}
