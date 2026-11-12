using ExampleApp.Channels;
using ExampleApp.Notices;
using System.Security.Claims;
using JasperFx.CodeGeneration;
using MsgHatch.Broker.Wolverine.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.SignalR.DependencyInjection;
using MsgHatch.Subscriptions.AspNetCore;
using OpenTelemetry;
using Wolverine;

namespace ExampleApp;

/// <summary>
/// The web app of the example, in one process with both roles (D101, D102): the event consumer, the publishers of email,
/// SignalR, and the broker, the subscription endpoints, and the SignalR hub.
/// </summary>
public static class ExampleApplication
{
    /// <summary>The prefix of the subscription endpoints.</summary>
    public const string SubscriptionsPrefix = "/api/subscriptions";

    /// <summary>The route of the SignalR hub.</summary>
    public const string HubRoute = "/hubs/notifications";

    /// <summary>The local Wolverine queue of the broker messages, so the example needs no broker server.</summary>
    public const string BrokerQueue = "broker-messages";

    /// <summary>Registers the services of the example, and maps its endpoints.</summary>
    /// <param name="builder">The builder of the web app.</param>
    /// <returns>The web app, not started.</returns>
    public static WebApplication Build(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddExampleMsgHatch()
            .UseEmailClient<LogEmailClient>()
            .UseSignalR<NotificationHub>()
            .UseWolverineBroker()
            .ConfigureInboxDispatch(static o => o.EnablePolling = false)
            .ConfigureOutboxDispatch(static o => o.EnablePolling = false);
        builder.Services.AddWolverine(ConfigureWolverine);
        builder.Services.AddHostedService<MsgHatchWorker>();
        builder.Services.AddExampleTelemetry().UseOtlpExporter();
        builder.Services.AddAuthentication().AddJwtBearer();
        builder.Services.AddAuthorization();

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHub<NotificationHub>(HubRoute);
        // ponytail: one policy for all subscription routes, add an administrator policy for the routes with no /me
        app.MapMsgHatchSubscriptions(SubscriptionsPrefix, static o => o.CurrentUser = CurrentUser).RequireAuthorization();
        return app;
    }

    /// <summary>Gives the user id of a caller from the claim that <c>dotnet user-jwts create --name</c> sets (D158).</summary>
    /// <param name="caller">The signed-in caller.</param>
    /// <returns>The user id, or null when the caller has no valid one.</returns>
    public static UserId? CurrentUser(ClaimsPrincipal caller)
    {
        ArgumentNullException.ThrowIfNull(caller);
        var user = UserId.Create(caller.FindFirstValue(ClaimTypes.NameIdentifier));
        return user.IsError ? null : user.Value;
    }

    private static void ConfigureWolverine(WolverineOptions options)
    {
        options.Discovery.DisableConventionalDiscovery().IncludeType<BrokerMessageLogger>();
        options.CodeGeneration.TypeLoadMode = TypeLoadMode.Dynamic;
        options.Durability.UseSyncRetryBlock = true;
        options.PublishMessage<Notice>().ToLocalQueue(BrokerQueue);
    }
}
