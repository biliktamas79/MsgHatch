using System.Text.Json;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;

/// <summary>
/// The test data of the SQLite outbox store: it reads the rows in the order of their insert as the document that
/// <see cref="MsgHatchJson"/> reads, and the column <c>Tries</c> in the format of section 4.5. A reset deletes the
/// database file.
/// </summary>
public sealed class SqliteOutboxStoreTestData(SqliteOutboxStore store) : IOutboxStoreTestData
{
    private const string _documents = """
        SELECT json_object(
            'schemaVersion', SchemaVersion,
            'delivery', json_object(
                'id', Id, 'status', Status, 'tryCount', TryCount, 'maxTryCount', MaxTryCount, 'createdAtUtc', CreatedAtUtc,
                'lastTryAtUtc', LastTryAtUtc, 'nextTryAtUtc', NextTryAtUtc, 'leaseUntilUtc', LeaseUntilUtc,
                'expiresAtUtc', ExpiresAtUtc, 'lastError', LastError),
            'sourceMessageId', SourceMessageId, 'tenant', NULLIF(TenantId, ''), 'type', MessageType, 'channel', Channel,
            'target', json_object('kind', TargetKind, 'id', TargetId), 'contentType', ContentType,
            'contentVersion', ContentVersion, 'flags', Flags, 'traceParent', TraceParent, 'trace', json(Trace),
            'envelope', json(Content))
        FROM MsgHatchOutbox ORDER BY rowid
        """;

    private readonly MsgHatchJson _json = new(TestContentTypes.ARegistry());

    public ValueTask ResetAsync(CancellationToken ct)
    {
        TestDatabases.Delete(store.ConnectionString);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<OutgoingMessage>> GetStoredAsync(CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<OutgoingMessage>>(
            [.. TestDatabases.Documents(store.ConnectionString, "MsgHatchOutbox", _documents).Select(document => _json.DeserializeOutgoing(document).Value)]);

    public ValueTask<IReadOnlyList<PublishTry>> GetTriesAsync(Guid outgoingMessageId, CancellationToken ct)
    {
        var tries = Assert.Single(TestDatabases.Rows(store.ConnectionString, $"SELECT Tries FROM MsgHatchOutbox WHERE Id = '{outgoingMessageId:D}'"))[0];
        using var array = JsonDocument.Parse(tries!);
        return ValueTask.FromResult<IReadOnlyList<PublishTry>>(
            [.. array.RootElement.EnumerateArray().Select(element => TryOf(outgoingMessageId, element))]);
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
