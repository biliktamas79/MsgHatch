using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Outbox;

namespace MsgHatch.Domain.Tests.Performance;

/// <summary>
/// A call through a logging wrapper with the Debug level off, against the same call with no wrapper (section 4.11).
/// The inner store does nothing, so the difference is the cost of the wrapper.
/// </summary>
[MemoryDiagnoser]
public class OutboxStoreLoggingWrapperPerfTests
{
    private static readonly OutgoingMessage[] _noMessages = [];

    private readonly PerfOutboxStore _inner = PerfOutboxStore.EachClaim(1);
    private ServiceProvider _withWrapper = null!;
    private ServiceProvider _withNoWrapper = null!;
    private IOutboxStore _wrapped = null!;
    private IOutboxStore _plain = null!;

    [GlobalSetup]
    public void Setup()
    {
        _withWrapper = Build(enableLogging: true);
        _withNoWrapper = Build(enableLogging: false);
        _wrapped = _withWrapper.GetRequiredService<IOutboxStore>();
        _plain = _withNoWrapper.GetRequiredService<IOutboxStore>();
        if (ReferenceEquals(_wrapped, _inner) || !ReferenceEquals(_plain, _inner))
        {
            throw new InvalidOperationException("The wrapper setup is not as the benchmark expects.");
        }
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _withWrapper.Dispose();
        _withNoWrapper.Dispose();
    }

    [Benchmark(Baseline = true)]
    public Error? AddAsync_NoWrapper() => _plain.AddAsync(_noMessages, default).GetAwaiter().GetResult();

    [Benchmark]
    public Error? AddAsync_LoggingWrapperWithDebugOff() => _wrapped.AddAsync(_noMessages, default).GetAwaiter().GetResult();

    private ServiceProvider Build(bool enableLogging)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging(static logging => logging.SetMinimumLevel(LogLevel.Information));
        services.AddMsgHatch(o => o.EnableLogging = enableLogging).UseOutboxStore((_, _) => _inner).UseOutboxDispatchStore(_ => _inner);
        return services.BuildServiceProvider();
    }
}
