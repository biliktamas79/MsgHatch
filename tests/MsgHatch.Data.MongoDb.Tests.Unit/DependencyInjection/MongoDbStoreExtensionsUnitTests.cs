using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MongoDB.Driver;
using MsgHatch.Data.MongoDb.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Data.MongoDb.Tests.Unit.DependencyInjection;

/// <summary>
/// The options and the setup checks of <c>UseMongoDbStores</c> (section 4.7, D60), also under the root of the
/// configuration sections (D328, D329). No test builds a MongoDB client, because a client starts network calls (D330).
/// </summary>
public sealed class MongoDbStoreExtensionsUnitTests
{
    private const string _connectionString = "mongodb://127.0.0.1:1";

    private const string _connectionStringKey = "MsgHatch:MongoDb:ConnectionString";

    private const string _databaseNameKey = "MsgHatch:MongoDb:DatabaseName";

    [Fact]
    public async Task UseMongoDbStores_ConfigSectionPath_BindsTheOptionsUnderTheRoot()
    {
        // Arrange
        const string connectionString = "mongodb://root.example.com:27017";
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?>
        {
            ["Messaging:MongoDb:ConnectionString"] = connectionString,
            ["MsgHatch:MongoDb:DatabaseName"] = "default",
        });
        services.AddMsgHatch(configSectionPath: "Messaging").UseMongoDbStores();
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<MongoDbStoreOptions>>().Value;

        // Assert
        Assert.Equal((connectionString, "msghatch"), (options.ConnectionString, options.DatabaseName));
    }

    [Fact]
    public async Task UseMongoDbStores_DatabaseNameInTheSection_ReachesTheOptions()
    {
        // Arrange
        const string databaseName = "from_section";
        var services = TestHost.CreateServices(configuration: Section(databaseName: databaseName));
        services.AddMsgHatch().UseMongoDbStores();
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<MongoDbStoreOptions>>().Value;

        // Assert
        Assert.Equal((databaseName, _connectionString), (options.DatabaseName, options.ConnectionString));
    }

    [Fact]
    public async Task UseMongoDbStores_DatabaseNameFromCode_WinsOverTheSection()
    {
        // Arrange
        const string databaseName = "from_code";
        var services = TestHost.CreateServices(configuration: Section(databaseName: "from_section"));
        services.AddMsgHatch().UseMongoDbStores(static o => o.DatabaseName = databaseName);
        await using var provider = services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<MongoDbStoreOptions>>().Value;

        // Assert
        Assert.Equal(databaseName, options.DatabaseName);
    }

    [Fact]
    public async Task UseMongoDbStores_DatabaseNameWithADot_FailsTheValidationOnDatabaseName()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: Section(databaseName: "msg.hatch"));
        services.AddMsgHatch().UseMongoDbStores();
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(() => provider.GetRequiredService<IOptions<MongoDbStoreOptions>>().Value);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.Equal("DatabaseName", failure[..failure.IndexOf(':', StringComparison.Ordinal)]);
    }

    [Fact]
    public async Task UseMongoDbStores_ClientOfTheHost_StaysTheClientOfTheStores()
    {
        // Arrange
        var client = Substitute.For<IMongoClient>();
        var services = TestHost.CreateServices(configuration: Section(databaseName: "msghatch"));
        services.AddSingleton(client);
        services.AddMsgHatch().UseMongoDbStores();
        await using var provider = services.Build();

        // Act
        var resolved = provider.GetRequiredService<IMongoClient>();

        // Assert
        Assert.Same(client, resolved);
    }

    [Fact]
    public async Task UseMongoDbStores_AfterUseInMemoryStores_NamesBothSetupMethodsForEachStoreInterface()
    {
        // Arrange
        const string names = "UseInMemoryStores and UseMongoDbStores select an implementation of ";
        var services = TestHost.CreateServices(configuration: Section(databaseName: "msghatch"));
        services.AddMsgHatch().UseInMemoryStores().UseMongoDbStores();
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Equal(
            ["IInboxDispatchStore", "IInboxStore", "IOutboxDispatchStore", "IOutboxStore", "IRegistrySettingsStore"],
            Assert.IsType<OptionsValidationException>(exception).Failures
                .Where(static failure => failure.StartsWith(names, StringComparison.Ordinal))
                .Select(static failure => failure[names.Length..^1])
                .Order(StringComparer.Ordinal));
    }

    private static Dictionary<string, string?> Section(string databaseName) =>
        new() { [_connectionStringKey] = _connectionString, [_databaseNameKey] = databaseName };
}
