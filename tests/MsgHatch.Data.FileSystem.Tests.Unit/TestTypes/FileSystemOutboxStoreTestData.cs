using System.Text.Json;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.FileSystem.Tests.Unit.TestTypes;

/// <summary>
/// The test data of the file outbox store: it reads the message files in the order of their names, and the property
/// <c>tries</c> of a file in the format of section 4.5. A reset deletes the folder.
/// </summary>
public sealed class FileSystemOutboxStoreTestData(FileSystemOutboxStore store) : IOutboxStoreTestData
{
    private readonly MsgHatchJson _json = new(TestContentTypes.ARegistry());

    public ValueTask ResetAsync(CancellationToken ct)
    {
        StoreFolders.Delete(store.RootPath);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<OutgoingMessage>> GetStoredAsync(CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<OutgoingMessage>>(
            [.. StoreFolders.MessageFiles(store.RootPath, "outbox").Select(path => _json.DeserializeOutgoing(File.ReadAllBytes(path)).Value)]);

    public ValueTask<IReadOnlyList<PublishTry>> GetTriesAsync(Guid outgoingMessageId, CancellationToken ct)
    {
        var path = StoreFolders.MessageFiles(store.RootPath, "outbox")
            .Single(path => path.EndsWith($"_{outgoingMessageId:D}.json", StringComparison.Ordinal));
        using var file = JsonDocument.Parse(File.ReadAllBytes(path));
        return ValueTask.FromResult<IReadOnlyList<PublishTry>>(
            [.. file.RootElement.GetProperty("tries").EnumerateArray().Select(element => TryOf(outgoingMessageId, element))]);
    }

    private static PublishTry TryOf(Guid outgoingMessageId, JsonElement element) =>
        new(
            outgoingMessageId,
            element.GetProperty("tryNumber").GetInt32(),
            element.GetProperty("startedAtUtc").GetDateTime(),
            element.GetProperty("finishedAtUtc").GetDateTime(),
            Enum.Parse<PublishOutcome>(element.GetProperty("outcome").GetString()!),
            element.GetProperty("providerMessageId").GetString(),
            element.GetProperty("errorCode").ValueKind == JsonValueKind.Null ? null : element.GetProperty("errorCode").GetInt32(),
            element.GetProperty("errorName").GetString(),
            element.GetProperty("errorMessage").GetString());
}
