using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;

namespace MsgHatch.Domain.Tests.Performance;

/// <summary>
/// One event through the pipeline (section 4.11), with 1, 100, and 10,000 routes from a router that does nothing else,
/// on the in-memory stores. Each call has the same message id, so the in-memory outbox ignores the messages of each
/// call after the first, and keeps no more messages. The service comes from one DI scope, so the benchmark measures the
/// pipeline, not the scope.
/// </summary>
[MemoryDiagnoser]
public class NotificationServicePerfTests
{
    private static readonly MessageContext _context = new(Guid.Parse("6d1c2bd5-0f1a-4cc5-9a3b-5d3c08f1b1aa"));

    private ServiceProvider _provider = null!;
    private AsyncServiceScope _scope;
    private INotificationService _notifications = null!;
    private PerfEvent _event = null!;

    [Params(1, 100, 10_000)]
    public int Routes { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseInMemoryStores().AddPerfMessage();
        _provider = services.BuildServiceProvider();
        _scope = _provider.CreateAsyncScope();
        _notifications = _scope.ServiceProvider.GetRequiredService<INotificationService>();
        _event = new PerfEvent(Routes);
        _ = ProcessAsync().Value ?? throw new InvalidOperationException("The first event failed.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _scope.Dispose();
        _provider.Dispose();
    }

    [Benchmark]
    public ErrorOr<NotificationResult> ProcessAsync() => _notifications.ProcessAsync(_event, _context, default).GetAwaiter().GetResult();
}
