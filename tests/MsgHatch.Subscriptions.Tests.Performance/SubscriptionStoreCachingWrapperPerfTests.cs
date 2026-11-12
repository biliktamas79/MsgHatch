using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.DependencyInjection;

namespace MsgHatch.Subscriptions.Tests.Performance;

/// <summary>
/// A subscription lookup for one event of Acme and the user 1, with a cache hit for the defaults, the company, and the
/// user, against the same lookup with no cache (section 4.11). Logging is off, so the result shows only the caching
/// wrapper and its backend.
/// </summary>
[MemoryDiagnoser]
public class SubscriptionStoreCachingWrapperPerfTests
{
    private readonly IReadOnlyList<CompanyId> _companies = [PerfSubscriptions.Acme];
    private readonly IReadOnlyList<UserId> _users = [new UserId("1")];
    private ServiceProvider _cached = null!;
    private ServiceProvider _plain = null!;
    private ISubscriptionStore _withCache = null!;
    private ISubscriptionStore _withNoCache = null!;

    [GlobalSetup]
    public void Setup()
    {
        _cached = Build(static builder => builder.UseSubscriptionCache<PerfCacheBackend>());
        _plain = Build(static _ => { });
        _withCache = _cached.GetRequiredService<ISubscriptionStore>();
        _withNoCache = _plain.GetRequiredService<ISubscriptionStore>();
        _ = FindForDeliveryAsync_CacheHit().Value ?? throw new InvalidOperationException("The first lookup failed.");
    }

    [GlobalCleanup]
    public void Cleanup()
    {
        _cached.Dispose();
        _plain.Dispose();
    }

    [Benchmark(Baseline = true)]
    public ErrorOr<IReadOnlyList<Subscription>> FindForDeliveryAsync_NoCache() =>
        _withNoCache.FindForDeliveryAsync(PerfSubscriptions.Type, _companies, _users, default).GetAwaiter().GetResult();

    [Benchmark]
    public ErrorOr<IReadOnlyList<Subscription>> FindForDeliveryAsync_CacheHit() =>
        _withCache.FindForDeliveryAsync(PerfSubscriptions.Type, _companies, _users, default).GetAwaiter().GetResult();

    private static ServiceProvider Build(Action<MsgHatchBuilder> cache)
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddLogging();
        var builder = services.AddMsgHatch(static o => o.EnableLogging = false)
            .UseSubscriptions()
            .UseSubscriptionStore(static (_, _) => new PerfSubscriptionStore());
        cache(builder);
        return services.BuildServiceProvider();
    }
}
