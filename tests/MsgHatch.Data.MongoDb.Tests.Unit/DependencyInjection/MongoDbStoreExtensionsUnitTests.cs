using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.MongoDb.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.MongoDb.Tests.Unit.DependencyInjection;

/// <summary>The options of <c>UseMongoDbStores</c> under the root of the configuration sections (D328, D329), with no server.</summary>
public sealed class MongoDbStoreExtensionsUnitTests
{
    [Fact]
    public async Task UseMongoDbStores_ConfigSectionPath_BindsTheOptionsUnderTheRoot()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?>
        {
            ["Messaging:MongoDb:ConnectionString"] = "mongodb://root.example.com:27017",
            ["MsgHatch:MongoDb:DatabaseName"] = "default",
        });
        services.AddMsgHatch(configSectionPath: "Messaging").UseMongoDbStores();
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<MongoDbStoreOptions>>().Value;

        // Assert
        Assert.Equal(("mongodb://root.example.com:27017", "msghatch"), (options.ConnectionString, options.DatabaseName));
    }
}
