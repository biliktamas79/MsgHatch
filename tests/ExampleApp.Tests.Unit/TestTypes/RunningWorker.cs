using Microsoft.Extensions.DependencyInjection;

namespace ExampleApp.Tests.Unit.TestTypes;

/// <summary>
/// A started <see cref="MsgHatchWorker"/>. At its disposal it stops the worker and waits until its loops return, so the
/// test never deletes a database file that a loop still uses.
/// </summary>
public sealed class RunningWorker : IAsyncDisposable
{
    private readonly MsgHatchWorker _worker;

    private RunningWorker(MsgHatchWorker worker)
    {
        _worker = worker;
    }

    public static async Task<RunningWorker> StartAsync(IServiceProvider provider, CancellationToken ct)
    {
        var worker = ActivatorUtilities.CreateInstance<MsgHatchWorker>(provider);
        await worker.StartAsync(ct);
        return new RunningWorker(worker);
    }

    public async ValueTask DisposeAsync()
    {
        await _worker.StopAsync(CancellationToken.None);
        _worker.Dispose();
    }
}
