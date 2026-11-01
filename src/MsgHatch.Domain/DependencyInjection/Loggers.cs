using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>Gives the logger of a wrapper, or a logger that writes nothing when the host registered no logging.</summary>
internal static class Loggers
{
    public static ILogger<T> For<T>(IServiceProvider services) => services.GetService<ILogger<T>>() ?? NullLogger<T>.Instance;
}
