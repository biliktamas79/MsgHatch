using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.MongoDb.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Data.MongoDb.Tests.Integration;

/// <summary>
/// The examples of the README of the package run as tests, and the README check compares them (D311). The tests use no
/// store and no signal, so they need no server.
/// </summary>
public sealed class ReadmeIntegTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables(typeof(MongoDbStoreOptions));

    [Fact]
    public async Task UseMongoDbStores_SettingsOfTheReadme_ResolvesTheOutboxStore()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseMongoDbStores();
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.NotNull(provider.GetRequiredService<IOutboxStore>());
        Assert.Equal("msghatch", provider.GetRequiredService<IOptions<MongoDbStoreOptions>>().Value.DatabaseName);
    }

    [Fact]
    public async Task UseMongoDbStores_DatabaseNameInCode_UsesTheValueOfTheCode()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));

        // Act
        #region readme:setup-code
        services.AddMsgHatch()
            .UseMongoDbStores(o => o.DatabaseName = "shop-messages");
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.Equal("shop-messages", provider.GetRequiredService<IOptions<MongoDbStoreOptions>>().Value.DatabaseName);
    }

    [Fact]
    public async Task UseMongoDbDispatchSignal_SettingsOfTheReadme_BuildsTheServices()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));

        // Act
        #region readme:signal
        services.AddMsgHatch()
            .UseMongoDbStores()
            .UseMongoDbDispatchSignal();
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.Equal("msghatch", provider.GetRequiredService<IOptions<MongoDbStoreOptions>>().Value.DatabaseName);
    }
}
