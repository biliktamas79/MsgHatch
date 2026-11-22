using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.SignalR.DependencyInjection;
using MsgHatch.Tests.Shared;

namespace MsgHatch.SignalR.Tests.Unit;

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

    private sealed class NotificationHub : Hub;
}
