using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;

namespace MsgHatch.Email.DependencyInjection;

/// <summary>The logging wrapper of the email client. <see cref="MsgHatchOptions.EnableLogging"/> adds or removes it (D35).</summary>
internal static class EmailLoggers
{
    public static IEmailClient Wrap(IServiceProvider services, IEmailClient client) =>
        IsEnabled(services)
            ? new EmailClientLoggingWrapper(client, For<EmailClientLoggingWrapper>(services), services.GetRequiredService<TimeProvider>())
            : client;

    private static bool IsEnabled(IServiceProvider services) =>
        services.GetRequiredService<IOptions<MsgHatchOptions>>().Value.EnableLogging;

    private static ILogger<T> For<T>(IServiceProvider services) => services.GetService<ILogger<T>>() ?? NullLogger<T>.Instance;
}
