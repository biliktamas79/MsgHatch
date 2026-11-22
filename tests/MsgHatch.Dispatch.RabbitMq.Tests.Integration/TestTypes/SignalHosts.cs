using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using MsgHatch.Dispatch.RabbitMq.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Dispatch.RabbitMq.Tests.Integration.TestTypes;

/// <summary>
/// DI containers that act as separate processes on the broker of <see cref="RabbitMqBroker"/>: each one has its own
/// RabbitMQ signal, and all of them use one random exchange and one fake clock.
/// </summary>
public sealed class SignalHosts : IAsyncDisposable
{
    private readonly List<ServiceProvider> _hosts = [];

    public string ExchangeName { get; } = $"msghatch-test-{Guid.NewGuid():N}";

    public FakeTimeProvider Time { get; } = TestHost.NewFakeTime();

    public IDispatchSignal NewSignal(Uri? broker = null)
    {
        var services = TestHost.CreateServices(Time);
        services.AddMsgHatch().UseInMemoryStores()
            .UseRabbitMqDispatchSignal(o => (o.Uri, o.ExchangeName) = (broker ?? RabbitMqBroker.Uri, ExchangeName));
        var host = services.Build();
        _hosts.Add(host);
        return host.GetRequiredService<IDispatchSignal>();
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var host in _hosts)
        {
            await host.DisposeAsync();
        }
    }
}
