using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Tests.Unit.Contracts;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Data.FileSystem.Tests.Unit.TestTypes;

/// <summary>The test data of the file subscription store: a new temporary root folder, which a reset deletes.</summary>
public sealed class FileSystemSubscriptionStoreTestData(string rootPath) : ISubscriptionStoreTestData
{
    /// <summary>A new root folder in the temporary folder <c>msghatch-tests</c>.</summary>
    public static string NewRootPath() => Path.Combine(Path.GetTempPath(), "msghatch-tests", Guid.NewGuid().ToString("N"));

    /// <summary>Gives the store of each tenant key on one root folder.</summary>
    public static Func<TenantKey, FileSystemSubscriptionStore> StoresOn(string rootPath) =>
        key => new FileSystemSubscriptionStore(rootPath, key, ScenarioContentTypes.SettingsJson);

    public ValueTask ResetAsync(CancellationToken ct)
    {
        if (Directory.Exists(rootPath))
        {
            Directory.Delete(rootPath, recursive: true);
        }

        return ValueTask.CompletedTask;
    }
}
