using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;

namespace MsgHatch.Dispatch.NamedPipes.DependencyInjection;

/// <summary>The setup method of the named pipe dispatch signal (section 4.7, D189).</summary>
public static class NamedPipeSignalExtensions
{
    /// <summary>
    /// Selects the dispatch signal between the processes of one OS user on one machine (D188). Each process of the
    /// system calls it with the same <see cref="NamedPipeSignalOptions.PipeNamePrefix"/>. The options bind from the
    /// section <c>NamedPipeSignal</c> under the root of <c>AddMsgHatch</c>, by default <c>MsgHatch:NamedPipeSignal</c>,
    /// and a value from code wins. The startup validation fails when
    /// <see cref="DispatchOptions.EnablePolling"/> is false for the inbox or the outbox (D190), and on Linux and macOS
    /// when the pipe path of a topic is too long for the platform.
    /// </summary>
    /// <param name="builder">The builder of the setup.</param>
    /// <param name="configure">An optional change of the options in code.</param>
    /// <returns>The builder.</returns>
    public static MsgHatchBuilder UseNamedPipeDispatchSignal(this MsgHatchBuilder builder, Action<NamedPipeSignalOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var options = builder.Services.AddOptions<NamedPipeSignalOptions>().BindConfiguration(builder.SectionPathOf(NamedPipeSignalOptions.SectionKey)).ValidateOnStart();
        if (configure is not null)
        {
            options.Configure(configure);
        }

        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<NamedPipeSignalOptions>, NamedPipeSignalOptionsValidator>());
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<NamedPipeSignalOptions>, PipePathValidator>());
        RequirePolling(builder.Services, "Inbox");
        RequirePolling(builder.Services, "Outbox");
        return builder.UseDispatchSignal(
            static services => new NamedPipeDispatchSignal(
                services.GetRequiredService<IOptions<NamedPipeSignalOptions>>().Value.PipeNamePrefix,
                services.GetService<ILogger<NamedPipeDispatchSignal>>() ?? NullLogger<NamedPipeDispatchSignal>.Instance),
            nameof(UseNamedPipeDispatchSignal));
    }

    private static void RequirePolling(IServiceCollection services, string dispatcher) =>
        services.AddOptions<DispatchOptions>(dispatcher)
            .Validate(
                static options => options.EnablePolling,
                $"UseNamedPipeDispatchSignal needs EnablePolling true in the dispatch options {dispatcher}, because a lost signal then waits only until the next poll (D190).")
            .ValidateOnStart();
}
