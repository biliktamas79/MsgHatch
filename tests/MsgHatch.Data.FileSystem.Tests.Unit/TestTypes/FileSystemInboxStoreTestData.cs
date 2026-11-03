using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.FileSystem.Tests.Unit.TestTypes;

/// <summary>The test data of the file inbox store: it reads the message files in the order of their names, and a reset deletes the folder.</summary>
public sealed class FileSystemInboxStoreTestData(FileSystemInboxStore store) : IInboxStoreTestData
{
    private readonly MsgHatchJson _json = new(TestContentTypes.ARegistry());

    public ValueTask ResetAsync(CancellationToken ct)
    {
        StoreFolders.Delete(store.RootPath);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<ReceivedMessage>> GetStoredAsync(CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<ReceivedMessage>>(
            [.. StoreFolders.MessageFiles(store.RootPath, "inbox").Select(path => _json.DeserializeReceived(File.ReadAllBytes(path)).Value)]);
}
