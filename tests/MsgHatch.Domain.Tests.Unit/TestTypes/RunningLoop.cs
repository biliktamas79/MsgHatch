namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>
/// Starts the <c>RunAsync</c> of a dispatcher, and at its disposal cancels it and waits until it returns, so that a
/// test never leaves a loop that runs.
/// </summary>
public sealed class RunningLoop : IAsyncDisposable
{
    private readonly CancellationTokenSource _stop = CancellationTokenSource.CreateLinkedTokenSource(TestContext.Current.CancellationToken);

    public RunningLoop(Func<CancellationToken, ValueTask> run)
    {
        Run = run(_stop.Token).AsTask();
    }

    public Task Run { get; }

    public async Task StopAsync()
    {
        await _stop.CancelAsync();
        await Run.WaitAsync(TestHost.HangGuard, TimeProvider.System);
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _stop.Dispose();
    }
}
