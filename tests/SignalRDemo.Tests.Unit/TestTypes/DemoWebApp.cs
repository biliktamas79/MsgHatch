using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

namespace SignalRDemo.Tests.Unit.TestTypes;

/// <summary>
/// The demo on a <c>TestServer</c>, with the demo data of its build output and fake logging. At its disposal it stops
/// the app, so a test never leaves a dispatch loop that runs.
/// </summary>
public sealed class DemoWebApp : IAsyncDisposable
{
    private DemoWebApp(WebApplication app)
    {
        App = app;
    }

    public WebApplication App { get; }

    public static DemoWebApp Build()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            EnvironmentName = "Testing",
            ContentRootPath = AppContext.BaseDirectory,
        });
        builder.WebHost.UseTestServer();
        builder.Logging.ClearProviders().AddFakeLogging();
        return new DemoWebApp(DemoApplication.Build(builder));
    }

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
    }
}
