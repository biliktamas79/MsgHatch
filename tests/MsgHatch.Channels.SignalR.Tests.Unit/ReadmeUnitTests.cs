using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Channels.SignalR.DependencyInjection;
using MsgHatch.Tests.Shared;
using NSubstitute;

namespace MsgHatch.Channels.SignalR.Tests.Unit;

/// <summary>The examples of the README of the package run as tests, and the README check compares them (D311).</summary>
public sealed class ReadmeUnitTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables();

    [Fact]
    public async Task UseSignalR_HubOfTheHost_ResolvesTheTransportOfTheChannel()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().UseInMemoryStores();

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseSignalR<NotificationHub>();
        #endregion
        await using var provider = services.BuildServiceProvider();

        // Assert
        Assert.Equal(SignalRChannel.Id, Assert.Single(provider.GetServices<ITransport>()).Channel);
    }

    [Fact]
    public async Task OnConnectedAsync_HubOfTheReadme_AddsTheConnectionToTheGroupsOfItsTenantAndItsCompany()
    {
        // Arrange
        const string connectionId = "connection 1";
        const string tenant = "t1";
        const string company = "c1";
        var context = Substitute.For<HubCallerContext>();
        context.ConnectionId.Returns(connectionId);
        context.User.Returns(new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant", tenant), new Claim("company", company)], "test")));
        var groups = Substitute.For<IGroupManager>();
        using var hub = new TenantHub { Context = context, Groups = groups };

        // Act
        await hub.OnConnectedAsync();

        // Assert
        Assert.Equal(
            [(connectionId, $"msghatch:tenant:{tenant}"), (connectionId, $"msghatch:{tenant}:company:{company}")],
            groups.ReceivedCalls().Select(static call => ((string)call.GetArguments()[0]!, (string)call.GetArguments()[1]!)));
    }

    private sealed class NotificationHub : Hub;

    private sealed class TenantHub : Hub
    {
        #region readme:hub
        public override async Task OnConnectedAsync()
        {
            var tenant = new TenantId(Context.User!.FindFirstValue("tenant")!);
            var company = new CompanyId(Context.User!.FindFirstValue("company")!);
            await Groups.AddToGroupAsync(Context.ConnectionId, SignalRGroups.ForTenant(tenant), Context.ConnectionAborted);
            await Groups.AddToGroupAsync(Context.ConnectionId, SignalRGroups.ForCompany(tenant, company), Context.ConnectionAborted);
            await base.OnConnectedAsync();
        }
        #endregion
    }
}
