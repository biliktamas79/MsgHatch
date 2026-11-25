using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Channels.Broker.Wolverine.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Channels.Broker.Wolverine.Tests.Unit;

/// <summary>The examples of the README of the package run as tests, and the README check compares them (D311).</summary>
public sealed class ReadmeUnitTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables();

    [Fact]
    public async Task UseWolverineBroker_ReadmeSetup_ResolvesTheWolverineClient()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.EnableLogging = false).UseInMemoryStores();

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseWolverineBroker();
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.IsType<WolverineBrokerClient>(provider.GetRequiredService<IBrokerClient>());
    }
}
