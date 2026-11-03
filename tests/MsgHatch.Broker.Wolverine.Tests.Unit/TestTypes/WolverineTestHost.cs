using JasperFx.CodeGeneration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using Wolverine;
using Wolverine.Runtime;

namespace MsgHatch.Broker.Wolverine.Tests.Unit.TestTypes;

/// <summary>
/// A started Wolverine host with a <see cref="FakeWolverineTransport"/>, and a <see cref="WolverineBrokerClient"/> on its
/// runtime. The host routes <see cref="TestPayload"/> to the fake transport, and no other type. It has no handler, so it
/// needs no generated code, and runs with <see cref="TypeLoadMode.Static"/> and no runtime compiler.
/// </summary>
public sealed class WolverineTestHost : IAsyncDisposable
{
    private readonly IHost _host;

    private WolverineTestHost(IHost host, FakeWolverineTransport transport)
    {
        _host = host;
        Transport = transport;
        Client = new WolverineBrokerClient(() => new MessageBus(host.Services.GetRequiredService<IWolverineRuntime>()));
    }

    public FakeWolverineTransport Transport { get; }

    public WolverineBrokerClient Client { get; }

    /// <summary>Starts a host with the required setup of section 4.7: an inline route and the sync retry block.</summary>
    public static Task<WolverineTestHost> StartAsync(CancellationToken ct) =>
        StartAsync(inline: true, syncRetryBlock: true, failedSends: 0, ct);

    /// <summary>Starts a host with the required setup of section 4.7, whose transport fails each send with <paramref name="failure"/>.</summary>
    public static Task<WolverineTestHost> StartAsync(Func<Exception> failure, CancellationToken ct) =>
        StartAsync(new FakeWolverineTransport(int.MaxValue, failure), inline: true, syncRetryBlock: true, ct);

    public static Task<WolverineTestHost> StartAsync(bool inline, bool syncRetryBlock, int failedSends, CancellationToken ct) =>
        StartAsync(new FakeWolverineTransport(failedSends), inline, syncRetryBlock, ct);

    private static async Task<WolverineTestHost> StartAsync(
        FakeWolverineTransport transport, bool inline, bool syncRetryBlock, CancellationToken ct)
    {
        var builder = Host.CreateApplicationBuilder();
        builder.Logging.ClearProviders();
        builder.Services.AddWolverine(options =>
        {
            options.Discovery.DisableConventionalDiscovery();
            options.CodeGeneration.TypeLoadMode = TypeLoadMode.Static;
            options.Durability.UseSyncRetryBlock = syncRetryBlock;
            options.Transports.Add(transport);
            var route = options.PublishMessage<TestPayload>().To(FakeWolverineTransport.Address);
            _ = inline ? route.SendInline() : route.BufferedInMemory();
        });
        var host = builder.Build();
        await host.StartAsync(ct);
        return new WolverineTestHost(host, transport);
    }

    public async ValueTask DisposeAsync()
    {
        await _host.StopAsync();
        _host.Dispose();
    }
}
