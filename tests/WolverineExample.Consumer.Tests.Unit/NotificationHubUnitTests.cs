using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using WolverineExample.Consumer.Channels;
using WolverineExample.Consumer.Tests.Unit.TestTypes;

namespace WolverineExample.Consumer.Tests.Unit;

/// <summary>The groups of a new connection of the user <c>north:1</c> of the company Acme <c>100</c> (D315).</summary>
public sealed class NotificationHubUnitTests : IAsyncLifetime
{
    private ConsumerTestHost _host = null!;

    public async ValueTask InitializeAsync() => _host = await ConsumerTestHost.StartAsync();

    public ValueTask DisposeAsync() => _host.DisposeAsync();

    [Fact]
    public async Task OnConnectedAsync_UserOfACompany_AddsTheConnectionToTheGroupsOfItsTenantAndOfItsCompany()
    {
        // Arrange
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns("connection 1");
        context.User.Returns(new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(HubCaller.TenantClaim, "north"), new Claim(ClaimTypes.NameIdentifier, "1")], "test")));
        var groups = Substitute.For<IGroupManager>();
        await using var scope = _host.Provider.CreateAsyncScope();
        using var hub = new NotificationHub(scope.ServiceProvider) { Context = context, Groups = groups };

        // Act
        await hub.OnConnectedAsync();

        // Assert
        Assert.Equal(
            [("connection 1", "msghatch:tenant:north"), ("connection 1", "msghatch:north:company:100")],
            groups.ReceivedCalls().Select(static call => ((string)call.GetArguments()[0]!, (string)call.GetArguments()[1]!)));
    }
}
