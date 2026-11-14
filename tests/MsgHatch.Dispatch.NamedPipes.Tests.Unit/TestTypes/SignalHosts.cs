using System.IO.Pipes;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Dispatch.NamedPipes.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Dispatch.NamedPipes.Tests.Unit.TestTypes;

/// <summary>
/// DI containers that act as separate processes: each one has its own named pipe signal, and all of them use one random
/// prefix. The pipe between two containers goes through the operating system, the same as between two processes.
/// </summary>
public sealed class SignalHosts : IAsyncDisposable
{
    private readonly List<ServiceProvider> _hosts = [];

    public string Prefix { get; } = $"mh{Guid.NewGuid():N}"[..14];

    public ServiceProvider NewHost()
    {
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseInMemoryStores().UseNamedPipeDispatchSignal(o => o.PipeNamePrefix = Prefix);
        var host = services.Build();
        _hosts.Add(host);
        return host;
    }

    public IDispatchSignal NewSignal() => NewHost().GetRequiredService<IDispatchSignal>();

    public string PipeNameOf(DispatchTopic topic) => $"{Prefix}.{topic.Name.Replace(':', '.')}";

    /// <summary>
    /// Holds the pipe name of a topic, so that no signal can serve it: on Windows with a server of one instance, and on
    /// Linux and macOS with a folder at the path of the socket (checked facts of phase 21 in the roadmap).
    /// </summary>
    public IDisposable HoldPipeName(DispatchTopic topic)
    {
        var name = PipeNameOf(topic);
        if (OperatingSystem.IsWindows())
        {
            return new NamedPipeServerStream(name, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
        }

        var folder = Path.Combine(Path.GetTempPath(), "CoreFxPipe_" + name);
        Directory.CreateDirectory(folder);
        return new FolderHold(folder);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }
    }

    private sealed class FolderHold(string folder) : IDisposable
    {
        public void Dispose() => Directory.Delete(folder);
    }
}
