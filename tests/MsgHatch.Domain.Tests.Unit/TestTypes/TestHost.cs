using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;

namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>
/// Shared by the tests that need a DI container: the services that a host gives before it calls
/// <c>AddMsgHatch</c>, which are the configuration, the fake clock, and the fake logging.
/// </summary>
public static class TestHost
{
    public static readonly DateTimeOffset Start = new(2030, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static FakeTimeProvider NewFakeTime() => new(Start);

    public static IServiceCollection CreateServices(
        TimeProvider? time = null, IReadOnlyDictionary<string, string?>? configuration = null)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(
            new ConfigurationBuilder().AddInMemoryCollection(configuration ?? new Dictionary<string, string?>()).Build());
        services.AddSingleton(time ?? NewFakeTime());
        services.AddLogging(static logging => logging.SetMinimumLevel(LogLevel.Trace).AddFakeLogging());
        return services;
    }

    public static ServiceProvider Build(this IServiceCollection services) =>
        services.BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
}
