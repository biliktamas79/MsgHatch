using ExampleApp.Channels;
using System.Security.Claims;
using JasperFx.CodeGeneration;
using MsgHatch.AspNetCore;
using MsgHatch.Broker.Wolverine.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.DependencyInjection;
using MsgHatch.SignalR.DependencyInjection;
using MsgHatch.Subscriptions.AspNetCore;
using MsgHatch.Templates.AspNetCore;
using OpenTelemetry;
using Wolverine;

namespace ExampleApp;

/// <summary>
/// The web app of the example, in one process with both roles (D101, D102): the event consumer, the publishers of email,
/// SignalR, and the broker, the subscription endpoints, the delivery endpoints, the registry endpoints, the template
/// endpoints, and the SignalR hub.
/// </summary>
public static class ExampleApplication
{
    /// <summary>The prefix of the subscription endpoints.</summary>
    public const string SubscriptionsPrefix = "/api/subscriptions";

    /// <summary>The prefix of the two route groups of the delivery endpoints (section 4.19).</summary>
    public const string DeliveryPrefix = "/api/delivery";

    /// <summary>The policy and the role of the overview and the actions of the delivery endpoints (D165).</summary>
    public const string DeliveryAdmin = "DeliveryAdmin";

    /// <summary>The policy and the role of the content routes of the delivery endpoints, which show events and envelopes (D109, D165).</summary>
    public const string DeliveryContent = "DeliveryContent";

    /// <summary>The prefix of the registry endpoints (section 4.20).</summary>
    public const string RegistryPrefix = "/api/registry";

    /// <summary>The policy and the role of the registry endpoints, which change the registry settings (D170).</summary>
    public const string RegistryAdmin = "RegistryAdmin";

    /// <summary>The prefix of the template endpoints (section 4.22).</summary>
    public const string TemplatesPrefix = "/api/templates";

    /// <summary>The policy and the role of the template endpoints, which change the email texts (D225).</summary>
    public const string TemplateAdmin = "TemplateAdmin";

    /// <summary>The route of the SignalR hub.</summary>
    public const string HubRoute = "/hubs/notifications";

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
        builder.Services.AddAuthorization(static options =>
        {
            options.AddPolicy(DeliveryAdmin, static policy => policy.RequireRole(DeliveryAdmin));
            options.AddPolicy(DeliveryContent, static policy => policy.RequireRole(DeliveryContent));
            options.AddPolicy(RegistryAdmin, static policy => policy.RequireRole(RegistryAdmin));
            options.AddPolicy(TemplateAdmin, static policy => policy.RequireRole(TemplateAdmin));
        });

        var app = builder.Build();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapHub<NotificationHub>(HubRoute);
        // ponytail: one policy for all subscription routes, add an administrator policy for the routes with no /me
        app.MapMsgHatchSubscriptions(SubscriptionsPrefix, static o => o.CurrentUser = CurrentUser).RequireAuthorization();
        app.MapMsgHatchDelivery(DeliveryPrefix).RequireAuthorization(DeliveryAdmin);
        app.MapMsgHatchDeliveryContent(DeliveryPrefix).RequireAuthorization(DeliveryContent);
        app.MapMsgHatchRegistry(RegistryPrefix).RequireAuthorization(RegistryAdmin);
        app.MapMsgHatchTemplates(TemplatesPrefix).RequireAuthorization(TemplateAdmin);
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
        // Wolverine routes each message type with a local handler to a local queue by convention, so the Notice of the broker needs no route.
        options.Discovery.DisableConventionalDiscovery().IncludeType<BrokerMessageLogger>();
        options.CodeGeneration.TypeLoadMode = TypeLoadMode.Dynamic;
        options.Durability.UseSyncRetryBlock = true;
    }
}
