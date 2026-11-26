using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.MongoDb;
using MsgHatch.Data.MongoDb.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Subscriptions.Data.MongoDb.DependencyInjection;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Subscriptions.Data.MongoDb.Tests.Unit;

/// <summary>
/// The examples of the README of the package run as tests, and the README check compares them (D311). The tests use no
/// store, so they need no server.
/// </summary>
public sealed class ReadmeUnitTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables();

    [Fact]
    public async Task UseMongoDbSubscriptionStore_SettingsOfTheReadme_SharesTheDatabaseOfTheStores()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));

        // Act
        #region readme:setup
        services.AddMsgHatch()
            .UseMongoDbStores()
            .UseSubscriptions()
            .UseMongoDbSubscriptionStore();
        #endregion
        await using var provider = services.Build();

        // Assert
        Assert.Equal("msghatch", provider.GetRequiredService<IOptions<MongoDbStoreOptions>>().Value.DatabaseName);
    }
}
