using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Data.FileSystem.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Subscriptions.Data.FileSystem.DependencyInjection;
using MsgHatch.Subscriptions.Data.FileSystem.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Data.FileSystem.Tests.Unit.DependencyInjection;

/// <summary><c>UseFileSubscriptionStore</c> registers the file subscription store on the shared file store options (D59, D150). Each test has its own temporary folder.</summary>
public sealed class FileSubscriptionStoreExtensionsUnitTests : IAsyncLifetime
{
    private readonly string _rootPath = FileSystemSubscriptionStoreTestData.NewRootPath();

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public ValueTask InitializeAsync() => ValueTask.CompletedTask;

    public ValueTask DisposeAsync() => new FileSystemSubscriptionStoreTestData(_rootPath).ResetAsync(Ct);

    [Fact]
    public async Task UseFileSubscriptionStore_RootPath_ResolvesTheStoreAsItsLoggingWrapperOnTheSubscriptionsFolder()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddScenarioMessages().UseFileSubscriptionStore(_rootPath);
        await using var provider = services.Build();

        // Act
        await provider.GetRequiredService<ISubscriptionStore>().AddAsync(Scenario.S6, Ct);

        // Assert
        Assert.Equal(
            (1106, "subscriptions"),
            (Assert.Single(provider.LogRecords()).Id.Id, FirstFolderOfTheSubscriptionFile()));
    }

    [Fact]
    public async Task UseFileSubscriptionStore_NoArgumentAfterUseFileStores_UsesTheRootPathOfUseFileStores()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch().AddScenarioMessages().UseFileStores(_rootPath).UseFileSubscriptionStore();
        await using var provider = services.Build();

        // Act
        await provider.GetRequiredService<ISubscriptionStore>().AddAsync(Scenario.S6, Ct);

        // Assert
        Assert.Equal("subscriptions", FirstFolderOfTheSubscriptionFile());
    }

    [Fact]
    public async Task UseFileSubscriptionStore_RootPathInTheSection_ReachesTheStore()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: new Dictionary<string, string?> { ["MsgHatch:FileStores:RootPath"] = _rootPath });
        services.AddMsgHatch().AddScenarioMessages().UseFileSubscriptionStore();
        await using var provider = services.Build();

        // Act
        await provider.GetRequiredService<ISubscriptionStore>().AddAsync(Scenario.S6, Ct);

        // Assert
        Assert.Equal("subscriptions", FirstFolderOfTheSubscriptionFile());
    }

    [Fact]
    public async Task UseFileSubscriptionStore_RootPathAlsoInUseFileStores_FailsTheStartupValidationAndNamesBothMethods()
    {
        // Arrange
        var services = TestHost.CreateServices();
        services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.Consumer).UseFileStores(_rootPath).UseFileSubscriptionStore(_rootPath);
        await using var provider = services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.Single(Assert.IsType<OptionsValidationException>(exception).Failures);
        Assert.StartsWith("UseFileStores and UseFileSubscriptionStore give ", failure, StringComparison.Ordinal);
    }

    private string FirstFolderOfTheSubscriptionFile() =>
        Path.GetRelativePath(_rootPath, Assert.Single(Directory.GetFiles(_rootPath, "*.json", SearchOption.AllDirectories)))
            .Split(Path.DirectorySeparatorChar)[0];
}
