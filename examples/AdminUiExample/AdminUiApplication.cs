using AdminUiExample.Components;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using MsgHatch.AdminUI;
using MsgHatch.AdminUI.DependencyInjection;
using MsgHatch.Api.Client.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Subscriptions.AdminUI;
using MsgHatch.Subscriptions.AdminUI.DependencyInjection;
using MsgHatch.Subscriptions.Api.Client.DependencyInjection;
using MsgHatch.Templates.AdminUI;
using MsgHatch.Templates.AdminUI.DependencyInjection;
using MsgHatch.Templates.Api.Client.DependencyInjection;
using MsgHatch.Templates.Liquid.AdminUI.DependencyInjection;

namespace AdminUiExample;

/// <summary>
/// The Interactive Server host of the admin UIs (D239). It has no message type and no store: it calls the subscription,
/// template, and registry APIs of <c>ExampleApp</c> through the HTTP clients, for all reads and writes (D235).
/// </summary>
public static class AdminUiApplication
{
    /// <summary>The assemblies of the admin pages, which the router of the host maps.</summary>
    public static readonly System.Reflection.Assembly[] AdminAssemblies =
        [typeof(SubscriptionAdminRoutes).Assembly, typeof(TemplateAdminRoutes).Assembly, typeof(RegistryAdminRoutes).Assembly];

    /// <summary>Registers the services of the example, and maps its pages.</summary>
    /// <param name="builder">The builder of the web app.</param>
    /// <returns>The web app, not started.</returns>
    public static WebApplication Build(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Services.AddOptions<AdminUiExampleOptions>().BindConfiguration(AdminUiExampleOptions.SectionName).ValidateOnStart();
        builder.Services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<AdminUiExampleOptions>, AdminUiExampleOptionsValidator>());
        builder.Services.AddTransient<ApiTokenHandler>();
        builder.Services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.None)
            .UseSubscriptionApiClient(configureHttp: static http => http.AddHttpMessageHandler<ApiTokenHandler>())
            .UseTemplateApiClient(configureHttp: static http => http.AddHttpMessageHandler<ApiTokenHandler>())
            .UseRegistryApiClient(configureHttp: static http => http.AddHttpMessageHandler<ApiTokenHandler>());
        builder.Services.AddMsgHatchAdminUI().AddMsgHatchSubscriptionsAdminUI().AddMsgHatchTemplatesAdminUI().AddLiquidTemplateEditor();
        builder.Services.AddRazorComponents().AddInteractiveServerComponents();

        var app = builder.Build();
        app.UseAntiforgery();
        app.MapStaticAssets();
        app.MapRazorComponents<App>().AddInteractiveServerRenderMode().AddAdditionalAssemblies(AdminAssemblies);
        return app;
    }
}
