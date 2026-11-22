using MsgHatch.Dispatch.RabbitMq.Tests.Integration.TestTypes;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Dispatch.RabbitMq.Tests.Integration;

/// <summary>
/// The use cases of each signal with the RabbitMQ signal (D305), on the broker of <see cref="RabbitMqBroker"/>. The
/// constructor skips each test while the URI of the broker is empty.
/// </summary>
public sealed class RabbitMqDispatchSignalContractIntegTests : DispatchSignalTestsBase<IDispatchSignal>, IAsyncDisposable
{
    private readonly SignalHosts _hosts;

    public RabbitMqDispatchSignalContractIntegTests()
        : this(HostsOrSkip())
    {
    }

    private RabbitMqDispatchSignalContractIntegTests(SignalHosts hosts)
        : base(hosts.NewSignal()) => _hosts = hosts;

    public ValueTask DisposeAsync() => _hosts.DisposeAsync();

    private static SignalHosts HostsOrSkip()
    {
        Assert.SkipUnless(RabbitMqBroker.IsConfigured, RabbitMqBroker.NotConfigured);
        return new SignalHosts();
    }
}
