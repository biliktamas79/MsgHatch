using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>The switch of the logging wrappers (D35), and the logger of a wrapper.</summary>
internal static class Loggers
{
    public static bool IsEnabled(IServiceProvider services) =>
        services.GetRequiredService<IOptions<MsgHatchOptions>>().Value.EnableLogging;

    public static TService Wrap<TService>(
        IServiceProvider services, TService inner, Func<IServiceProvider, TService, TService> wrapWithLogging) =>
        IsEnabled(services) ? wrapWithLogging(services, inner) : inner;

    public static ILogger<T> For<T>(IServiceProvider services) => services.GetService<ILogger<T>>() ?? NullLogger<T>.Instance;
}
