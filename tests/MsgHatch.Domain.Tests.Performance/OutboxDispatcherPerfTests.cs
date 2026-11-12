using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Outbox;

namespace MsgHatch.Domain.Tests.Performance;

/// <summary>
/// The outbox dispatcher (section 4.11) with a transport that does nothing: one batch of 100 messages through
/// <c>DispatchPendingAsync</c>, and the same batch through the dispatch loop of <c>RunAsync</c>, which a signal wakes.
/// The memory diagnoser counts the bytes of all threads, so the result of <c>RunAsync</c> includes its send workers.
/// </summary>
[MemoryDiagnoser]
public class OutboxDispatcherPerfTests : IDisposable
{
    private const int _batchSize = 100;

    private readonly PerfTransport _transport = new();
    private readonly PerfOutboxStore _eachClaim = PerfOutboxStore.EachClaim(_batchSize);
    private readonly PerfOutboxStore _afterArm = PerfOutboxStore.AfterArm(_batchSize);
    private readonly CancellationTokenSource _stop = new();
    private ServiceProvider _pending = null!;
    private ServiceProvider _loop = null!;
    private IOutboxDispatcher _dispatcher = null!;
    private IDispatchSignal _signal = null!;
    private Task _running = null!;

    [GlobalSetup]
    public void Setup()
    {
        _pending = Build(_eachClaim);
        _dispatcher = _pending.GetRequiredService<IOutboxDispatcher>();
        _loop = Build(_afterArm);
        _signal = _loop.GetRequiredService<IDispatchSignal>();
        _running = _loop.GetRequiredService<IOutboxDispatcher>().RunAsync(_stop.Token).AsTask();
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _stop.Cancel();
        _running.GetAwaiter().GetResult();
        _pending.Dispose();
        _loop.Dispose();
    }

    public void Dispose()
    {
        _stop.Dispose();
        GC.SuppressFinalize(this);
    }

    [Benchmark(OperationsPerInvoke = _batchSize)]
    public async Task<ErrorOr<DispatchSummary>> DispatchPendingAsync_OneBatch() => await _dispatcher.DispatchPendingAsync(_batchSize, default);

    [Benchmark(OperationsPerInvoke = _batchSize)]
    public async Task RunAsync_OneBatch()
    {
        var sent = _transport.WaitForSendsAsync(_batchSize);
        _afterArm.ClaimOnce();
        await _signal.NotifyAsync(DispatchTopic.ForChannel(PerfChannel.Id), default);
        await sent;
    }

    private ServiceProvider Build(PerfOutboxStore store)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Publisher)
            .UseOutboxStore((_, _) => store)
            .UseOutboxDispatchStore(_ => store)
            .UseTransport(_ => _transport)
            .ConfigureOutboxDispatch(static o =>
            {
                o.EnablePolling = false;
                o.QueueCapacity = 128;
            })
            .AddPerfMessage();
        return services.BuildServiceProvider();
    }
}
