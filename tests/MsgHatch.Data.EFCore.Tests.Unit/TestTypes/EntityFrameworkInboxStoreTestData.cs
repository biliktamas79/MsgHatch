using System.Text;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using static MsgHatch.Data.EFCore.Tests.Unit.TestTypes.SqliteTestDatabase;

namespace MsgHatch.Data.EFCore.Tests.Unit.TestTypes;

/// <summary>
/// The test data of the EF Core inbox store on SQLite: it reads the rows in the order of their insert as the document
/// that <see cref="MsgHatchJson"/> reads, and a reset makes a new file with the tables.
/// </summary>
public sealed class EntityFrameworkInboxStoreTestData(SqliteTestDatabase database) : IInboxStoreTestData
{
    private static readonly string _documents = $"""
        SELECT json_object(
            'schemaVersion', SchemaVersion,
            'delivery', json_object(
                'id', Id, 'status', Status, 'tryCount', TryCount, 'maxTryCount', MaxTryCount, 'createdAtUtc', {IsoTime("CreatedAtUtc")},
                'lastTryAtUtc', {IsoTime("LastTryAtUtc")}, 'nextTryAtUtc', {IsoTime("NextTryAtUtc")},
                'leaseUntilUtc', {IsoTime("LeaseUntilUtc")}, 'expiresAtUtc', {IsoTime("ExpiresAtUtc")}, 'lastError', LastError),
            'tenant', NULLIF(TenantId, ''), 'type', ContentType, 'contentVersion', ContentVersion, 'flags', Flags,
            'traceParent', TraceParent, 'event', json(Content))
        FROM Inbox ORDER BY Sequence
        """;

    private readonly MsgHatchJson _json = new(TestContentTypes.ARegistry());

    public ValueTask ResetAsync(CancellationToken ct) => database.ResetAsync(database.Contexts, ct);

    public ValueTask<IReadOnlyList<ReceivedMessage>> GetStoredAsync(CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<ReceivedMessage>>(
            [.. database.Rows(_documents).Select(row => _json.DeserializeReceived(Encoding.UTF8.GetBytes(row[0]!)).Value)]);
}
