using System.Text;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using static MsgHatch.Data.EFCore.Tests.Integration.TestTypes.PostgreSqlTestDatabase;

namespace MsgHatch.Data.EFCore.Tests.Integration.TestTypes;

/// <summary>
/// The test data of the EF Core inbox store on PostgreSQL: it reads the rows in the order of their insert as the document
/// that <see cref="MsgHatchJson"/> reads, with <c>json_build_object</c>, and a reset deletes the rows.
/// </summary>
public sealed class PostgreSqlInboxStoreTestData(PostgreSqlTestDatabase database) : IInboxStoreTestData
{
    private static readonly string _documents = $"""
        SELECT json_build_object(
            'schemaVersion', "SchemaVersion",
            'delivery', json_build_object(
                'id', "Id", 'status', "Status", 'tryCount', "TryCount", 'maxTryCount', "MaxTryCount", 'createdAtUtc', {IsoTime("CreatedAtUtc")},
                'lastTryAtUtc', {IsoTime("LastTryAtUtc")}, 'nextTryAtUtc', {IsoTime("NextTryAtUtc")},
                'leaseUntilUtc', {IsoTime("LeaseUntilUtc")}, 'expiresAtUtc', {IsoTime("ExpiresAtUtc")}, 'lastError', "LastError"),
            'tenant', NULLIF("TenantId", ''), 'type', "ContentType", 'contentVersion', "ContentVersion", 'flags', "Flags",
            'traceParent', "TraceParent", 'event', "Content"::json)::text
        FROM "msghatch"."MsgHatchInbox" ORDER BY "Sequence"
        """;

    private readonly MsgHatchJson _json = new(TestContentTypes.ARegistry());

    public ValueTask ResetAsync(CancellationToken ct) => database.ResetAsync(ct);

    public async ValueTask<IReadOnlyList<ReceivedMessage>> GetStoredAsync(CancellationToken ct) =>
        [.. (await database.RowsAsync(_documents, ct)).Select(row => _json.DeserializeReceived(Encoding.UTF8.GetBytes(row[0]!)).Value)];
}
