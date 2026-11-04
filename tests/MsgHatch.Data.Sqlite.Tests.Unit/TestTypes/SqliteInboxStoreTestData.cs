using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.Sqlite.Tests.Unit.TestTypes;

/// <summary>
/// The test data of the SQLite inbox store: it reads the rows in the order of their insert as the document that
/// <see cref="MsgHatchJson"/> reads, and a reset deletes the database file.
/// </summary>
public sealed class SqliteInboxStoreTestData(SqliteInboxStore store) : IInboxStoreTestData
{
    private const string _documents = """
        SELECT json_object(
            'schemaVersion', SchemaVersion,
            'delivery', json_object(
                'id', Id, 'status', Status, 'tryCount', TryCount, 'maxTryCount', MaxTryCount, 'createdAtUtc', CreatedAtUtc,
                'lastTryAtUtc', LastTryAtUtc, 'nextTryAtUtc', NextTryAtUtc, 'leaseUntilUtc', LeaseUntilUtc,
                'expiresAtUtc', ExpiresAtUtc, 'lastError', LastError),
            'tenant', NULLIF(TenantId, ''), 'type', ContentType, 'contentVersion', ContentVersion, 'flags', Flags,
            'traceParent', TraceParent, 'event', json(Content))
        FROM MsgHatchInbox ORDER BY Sequence
        """;

    private readonly MsgHatchJson _json = new(TestContentTypes.ARegistry());

    public ValueTask ResetAsync(CancellationToken ct)
    {
        TestDatabases.Delete(store.ConnectionString);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<ReceivedMessage>> GetStoredAsync(CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<ReceivedMessage>>(
            [.. TestDatabases.Documents(store.ConnectionString, "MsgHatchInbox", _documents).Select(document => _json.DeserializeReceived(document).Value)]);
}
