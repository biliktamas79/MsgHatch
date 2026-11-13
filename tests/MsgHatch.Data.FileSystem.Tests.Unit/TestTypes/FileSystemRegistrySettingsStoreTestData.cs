using MsgHatch.Domain.Tests.Unit.Contracts;

namespace MsgHatch.Data.FileSystem.Tests.Unit.TestTypes;

/// <summary>The test data of the file registry settings store. A reset deletes the root folder.</summary>
public sealed class FileSystemRegistrySettingsStoreTestData(FileSystemRegistrySettingsStore store) : IRegistrySettingsStoreTestData
{
    public ValueTask ResetAsync(CancellationToken ct)
    {
        StoreFolders.Delete(store.RootPath);
        return ValueTask.CompletedTask;
    }
}
