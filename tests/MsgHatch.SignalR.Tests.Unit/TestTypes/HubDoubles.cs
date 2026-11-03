using Microsoft.AspNetCore.SignalR;
using NSubstitute;

namespace MsgHatch.SignalR.Tests.Unit.TestTypes;

/// <summary>
/// Shared by the transport tests: an <see cref="IHubContext{THub}"/> substitute. Each user and each group gives the same
/// client proxy substitute, so a test checks the name on <see cref="Clients"/> and the send on <see cref="Proxy"/>.
/// </summary>
public sealed class HubDoubles
{
    public HubDoubles()
    {
        Clients.User(default!).ReturnsForAnyArgs(Proxy);
        Clients.Group(default!).ReturnsForAnyArgs(Proxy);
        Hub.Clients.Returns(Clients);
    }

    public IHubContext<TestHub> Hub { get; } = Substitute.For<IHubContext<TestHub>>();

    public IHubClients Clients { get; } = Substitute.For<IHubClients>();

    public IClientProxy Proxy { get; } = Substitute.For<IClientProxy>();

    /// <summary>The one call of <see cref="Clients"/>: the method, <c>User</c> or <c>Group</c>, and the name.</summary>
    public (string Method, object? Name) SingleName()
    {
        var call = Assert.Single(Clients.ReceivedCalls());
        return (call.GetMethodInfo().Name, call.GetArguments()[0]);
    }

    /// <summary>The one argument of the one send of <see cref="Proxy"/>.</summary>
    public object? SentPayload() => Assert.Single((object?[])Assert.Single(Proxy.ReceivedCalls()).GetArguments()[1]!);
}
