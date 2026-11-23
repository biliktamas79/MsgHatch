using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.SignalR.DependencyInjection;
using SignalRDemo.CodePanel;
using SignalRDemo.Notices;
using SignalRDemo.Sessions;
using SignalRDemo.Tenants;

namespace SignalRDemo;

/// <summary>
/// The SignalR demo (D316): one page that shows how a web page finds its tenant, its company, and its user, which
/// SignalR groups it joins at each step, and which open session gets each notification. It needs no database, no
/// broker, and no network: MsgHatch has the in-memory stores, and the router of the host sends each notification
/// through the outbox to the SignalR transport.
/// </summary>
public static class DemoApplication
{
    /// <summary>The file of the demo data, in the content root.</summary>
    public const string DemoDataFile = "demo-data.json";

    /// <summary>The route of the demo data, for the drop-downs of the dashboard.</summary>
    public const string DataRoute = "/demo/data";

    /// <summary>Registers the services of the demo, and maps its endpoints.</summary>
    /// <param name="builder">The builder of the web app.</param>
    /// <returns>The web app, not started.</returns>
    public static WebApplication Build(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Configuration.AddJsonFile(DemoDataFile, optional: false, reloadOnChange: false);
        builder.Services.AddOptions<DemoDataOptions>().BindConfiguration(DemoDataOptions.SectionName).ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<DemoDataOptions>, DemoDataOptionsValidator>();
        builder.Services.AddSingleton<DomainResolver>();
        builder.Services.AddSingleton<DemoTokens>();
        builder.Services.AddSingleton<IUserIdProvider, DemoUserIdProvider>();
        builder.Services.AddAuthentication().AddJwtBearer();
        builder.Services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<DemoTokens>(static (options, tokens) => tokens.Configure(options));
        builder.Services.AddAuthorization();
        builder.Services.AddMsgHatch()
            .UseInMemoryStores()
            .UseSignalR<DemoHub>()
            .ConfigureOutboxDispatch(static o => o.EnablePolling = false)
            .AddMessage(DemoNotice.TypeName, 1, DemoJson.Default.DemoNotice, static message => message
                .WithRouter<DemoRouter>()
                .WithPayload<DemoPayload, DemoPayloadLoader>(static payload => payload
                    .AsSignalR(DemoNotice.TypeName, 1, DemoJson.Default.SignalREnvelopeDemoPayload)));
        builder.Services.AddHostedService<OutboxWorker>();
        builder.Services.ConfigureHttpJsonOptions(static o => o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()));
        builder.Services.AddRazorPages();
        builder.Services.AddSingleton<CodeBlocks>();

        var app = builder.Build();
        app.UseStaticFiles();
        app.UseAuthentication();
        app.UseAuthorization();
        app.MapRazorPages();
        app.MapHub<DemoHub>(DemoHub.Route);
        app.MapGet(DataRoute, static (IOptions<DemoDataOptions> data) => data.Value);
        app.MapGet(DomainResolver.Route, static (string? host, DomainResolver resolver) => resolver.Resolve(host));
        app.MapPost(DemoLogin.Route, DemoLogin.Handle);
        app.MapPost(DemoPublisher.Route, DemoPublisher.HandleAsync);
        return app;
    }
}
