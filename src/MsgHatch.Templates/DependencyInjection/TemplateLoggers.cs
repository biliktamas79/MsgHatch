using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Templates.DependencyInjection;

/// <summary>The logging wrappers of the template library. <see cref="MsgHatchOptions.EnableLogging"/> adds or removes them (D35).</summary>
internal static class TemplateLoggers
{
    public static ITemplateStore Wrap(IServiceProvider services, TenantKey key, ITemplateStore store)
    {
        if (!IsEnabled(services))
        {
            return store;
        }

        var logger = For<TemplateStoreLoggingWrapper>(services);
        var time = services.GetRequiredService<TimeProvider>();
        var reader = new TemplateStoreLoggingWrapper(store, key, logger, time);
        return store is ITemplateWriter writer ? new TemplateWriterLoggingWrapper(reader, writer, key, logger, time) : reader;
    }

    public static ITemplateEngine<TPayload> Wrap<TPayload>(IServiceProvider services, TenantKey key, ITemplateEngine<TPayload> engine) =>
        new TemplateEngineLoggingWrapper<TPayload>(
            engine, key, For<TemplateEngineLoggingWrapper<TPayload>>(services), services.GetRequiredService<TimeProvider>());

    private static bool IsEnabled(IServiceProvider services) =>
        services.GetRequiredService<IOptions<MsgHatchOptions>>().Value.EnableLogging;

    private static ILogger<T> For<T>(IServiceProvider services) => services.GetService<ILogger<T>>() ?? NullLogger<T>.Instance;
}
