using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.MongoDb.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Outbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Data.MongoDb.Tests.Integration;

/// <summary>
/// The README example that resolves a store, which builds a MongoDB client, and a client starts network calls (D330). The
/// README check and the other examples of the README are in the unit test project of the package (D311).
/// </summary>
public sealed class ReadmeIntegTests
{
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
}
