using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.MongoDb;
using MsgHatch.Data.MongoDb.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Subscriptions.Data.MongoDb.DependencyInjection;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Data.MongoDb.Tests.Unit.DependencyInjection;

/// <summary>
/// The options of <c>UseMongoDbSubscriptionStore</c>: the shared <c>MongoDbStoreOptions</c> (D59) under the root of the
/// configuration sections (D328, D329). No test sends a command to a server.
/// </summary>
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

    [Fact]
    public async Task UseMongoDbSubscriptionStore_ValueInCodeAlsoInUseMongoDbStores_FailsTheStartupValidationAndNamesBothMethods()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: Section());
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer)
            .UseMongoDbStores(static o => o.DatabaseName = "a")
            .UseMongoDbSubscriptionStore(static o => o.DatabaseName = "b");
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.StartsWith("UseMongoDbStores and UseMongoDbSubscriptionStore give ", failure, StringComparison.Ordinal);
    }

    private static Dictionary<string, string?> Section() =>
        new()
        {
            ["MsgHatch:MongoDb:ConnectionString"] = "mongodb://127.0.0.1:1",
            ["MsgHatch:MongoDb:DatabaseName"] = "setup",
        };
}
