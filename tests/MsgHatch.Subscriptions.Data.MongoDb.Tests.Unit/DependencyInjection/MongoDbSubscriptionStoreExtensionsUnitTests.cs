using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.MongoDb;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Subscriptions.Data.MongoDb.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Data.MongoDb.Tests.Unit.DependencyInjection;

/// <summary>The options of <c>UseMongoDbSubscriptionStore</c> under the root of the configuration sections (D328, D329), with no server.</summary>
public sealed class MongoDbSubscriptionStoreExtensionsUnitTests
{
    [Fact]
    public async Task UseMongoDbSubscriptionStore_ConfigSectionPath_BindsTheOptionsUnderTheRoot()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?>
        {
            ["Messaging:MongoDb:ConnectionString"] = "mongodb://root.example.com:27017",
            ["MsgHatch:MongoDb:DatabaseName"] = "default",
        });
        services.AddMsgHatch(configSectionPath: "Messaging").UseMongoDbSubscriptionStore();
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<MongoDbStoreOptions>>().Value;

        // Assert
        Assert.Equal(("mongodb://root.example.com:27017", "msghatch"), (options.ConnectionString, options.DatabaseName));
    }
}
