using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Logging.Testing;

namespace ExampleApp.Tests.Unit.TestTypes;

/// <summary>
/// The web app of the example on a <c>TestServer</c>, with the configuration of the test and fake logging. At its
/// disposal it stops the app, so a test never leaves a dispatch loop that runs. The OpenTelemetry SDK is off, because a
/// test has no OTLP collector, and the exporter of the example waits about 8 s at each disposal for one.
/// </summary>
public sealed class ExampleWebApp : IAsyncDisposable
{
    private ExampleWebApp(WebApplication app)
    {
        App = app;
    }

    public WebApplication App { get; }

    public static ExampleWebApp Build(Dictionary<string, string?> configuration)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Testing" });
        builder.WebHost.UseTestServer();
        builder.Configuration.AddInMemoryCollection(configuration);
        builder.Configuration.AddInMemoryCollection([new("OTEL_SDK_DISABLED", "true")]);
        builder.Logging.ClearProviders().AddFakeLogging();
        return new ExampleWebApp(ExampleApplication.Build(builder));
    }

    public async ValueTask DisposeAsync()
    {
        await App.StopAsync();
        await App.DisposeAsync();
    }
}
