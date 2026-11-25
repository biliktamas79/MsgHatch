using JasperFx.CodeGeneration;
using Microsoft.AspNetCore.SignalR;
using MsgHatch.Channels.Email.DependencyInjection;
using MsgHatch.Channels.SignalR.DependencyInjection;
using Wolverine;
using Wolverine.RabbitMQ;
using WolverineExample.Consumer.Channels;
using WolverineExample.Contracts;

namespace WolverineExample.Consumer;

/// <summary>
/// The consumer app: a Wolverine listener on the RabbitMQ queue of the domain events, MsgHatch with both roles in one
/// process (D102), the log email client, and the SignalR hub. It is a web app on Kestrel only because the SignalR hub
/// needs an HTTP server.
/// </summary>
public static class ConsumerApplication
{
    /// <summary>The route of the SignalR hub.</summary>
    public const string HubRoute = "/hubs/notifications";

    /// <summary>
    /// Registers the services of the consumer, and maps the hub. The hub closes a connection when its token expires, so a
    /// user that left a company or a tenant does not keep the groups of an old token. The client then reconnects with a
    /// new token, and the hub reads the company of the user again.
    /// </summary>
    /// <param name="builder">The builder of the web app.</param>
    /// <returns>The web app, not started.</returns>
    public static WebApplication Build(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        var rabbitMq = RabbitMqOptions.Load(builder.Configuration);
        builder.Services.AddConsumerMsgHatch()
            .UseEmailClient<LogEmailClient>()
            .UseSignalR<NotificationHub>()
            .ConfigureInboxDispatch(static o => o.EnablePolling = false)
            .ConfigureOutboxDispatch(static o => o.EnablePolling = false);
        builder.Services.AddSingleton<IUserIdProvider, TenantUserIdProvider>();
        builder.Services.AddWolverine(options => ConfigureWolverine(options, rabbitMq.Uri!));
        builder.Services.AddHostedService<MsgHatchWorker>();
        builder.Services.AddAuthentication().AddJwtBearer();
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHub<NotificationHub>(HubRoute, static o => o.CloseOnAuthenticationExpiration = true);
        return app;
    }

    // ProcessInline acknowledges an envelope only after the handler returns, so the broker keeps each event until the
    // inbox has it.
    private static void ConfigureWolverine(WolverineOptions options, Uri broker)
    {
        options.Discovery.DisableConventionalDiscovery().IncludeType<DomainEventReceiver>();
        options.CodeGeneration.TypeLoadMode = TypeLoadMode.Dynamic;
        options.UseRabbitMq(broker).AutoProvision().DisableSystemRequestReplyQueueDeclaration();
        options.ListenToRabbitQueue(RabbitMqOptions.EventQueue).ProcessInline();
    }
}
