using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.Engines;
using MsgHatch.Email.Templates;

namespace MsgHatch.Email.DependencyInjection;

/// <summary>The logging wrappers of the email package. <see cref="MsgHatchOptions.EnableLogging"/> adds or removes them (D35).</summary>
internal static class EmailLoggers
{
    public static ITemplateStore Wrap(IServiceProvider services, TenantKey key, ITemplateStore store) =>
        IsEnabled(services)
            ? new TemplateStoreLoggingWrapper(store, key, For<TemplateStoreLoggingWrapper>(services), services.GetRequiredService<TimeProvider>())
            : store;

    public static IEmailTemplateEngine<TPayload> Wrap<TPayload>(IServiceProvider services, TenantKey key, IEmailTemplateEngine<TPayload> engine) =>
        new EmailTemplateEngineLoggingWrapper<TPayload>(
            engine, key, For<EmailTemplateEngineLoggingWrapper<TPayload>>(services), services.GetRequiredService<TimeProvider>());

    public static IEmailClient Wrap(IServiceProvider services, IEmailClient client) =>
        IsEnabled(services)
            ? new EmailClientLoggingWrapper(client, For<EmailClientLoggingWrapper>(services), services.GetRequiredService<TimeProvider>())
            : client;

    private static bool IsEnabled(IServiceProvider services) =>
        services.GetRequiredService<IOptions<MsgHatchOptions>>().Value.EnableLogging;

    private static ILogger<T> For<T>(IServiceProvider services) => services.GetService<ILogger<T>>() ?? NullLogger<T>.Instance;
}
