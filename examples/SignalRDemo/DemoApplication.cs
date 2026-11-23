using Microsoft.Extensions.Options;
using SignalRDemo.Tenants;

namespace SignalRDemo;

/// <summary>
/// The SignalR demo (D316): one page that shows how a web page finds its tenant, its company, and its user, which
/// SignalR groups it joins at each step, and which open session gets each notification. It needs no database, no
/// broker, and no network.
/// </summary>
public static class DemoApplication
{
    /// <summary>The file of the demo data, in the content root.</summary>
    public const string DemoDataFile = "demo-data.json";

    /// <summary>Registers the services of the demo, and maps its endpoints.</summary>
    /// <param name="builder">The builder of the web app.</param>
    /// <returns>The web app, not started.</returns>
    public static WebApplication Build(WebApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);
        builder.Configuration.AddJsonFile(DemoDataFile, optional: false, reloadOnChange: false);
        builder.Services.AddOptions<DemoDataOptions>().BindConfiguration(DemoDataOptions.SectionName).ValidateOnStart();
        builder.Services.AddSingleton<IValidateOptions<DemoDataOptions>, DemoDataOptionsValidator>();
        return builder.Build();
    }
}
