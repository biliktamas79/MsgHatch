using System.Text;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.Tests.Integration.TestTypes;

/// <summary>
/// The test data of the EF Core inbox store: it reads the rows in the order of their insert as the document that
/// <see cref="MsgHatchJson"/> reads, with <c>FOR JSON</c>, and a reset deletes the rows.
/// </summary>
public sealed class EntityFrameworkInboxStoreTestData(TestDatabase database) : IInboxStoreTestData
{
    private const string _documents = """
        SELECT (
            SELECT
                SchemaVersion AS schemaVersion, Id AS [delivery.id], Status AS [delivery.status], TryCount AS [delivery.tryCount],
                MaxTryCount AS [delivery.maxTryCount], CreatedAtUtc AS [delivery.createdAtUtc], LastTryAtUtc AS [delivery.lastTryAtUtc],
                NextTryAtUtc AS [delivery.nextTryAtUtc], LeaseUntilUtc AS [delivery.leaseUntilUtc], ExpiresAtUtc AS [delivery.expiresAtUtc],
                LastError AS [delivery.lastError], NULLIF(TenantId, '') AS tenant, ContentType AS type, ContentVersion AS contentVersion,
                Flags AS flags, TraceParent AS traceParent, JSON_QUERY(Content) AS event
            FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER)
        FROM [msghatch].[MsgHatchInbox] ORDER BY Sequence
        """;

    private readonly MsgHatchJson _json = new(TestContentTypes.ARegistry());

    public ValueTask ResetAsync(CancellationToken ct) => database.ResetAsync(ct);

    public async ValueTask<IReadOnlyList<ReceivedMessage>> GetStoredAsync(CancellationToken ct) =>
        [.. (await database.RowsAsync(_documents, ct)).Select(row => _json.DeserializeReceived(Encoding.UTF8.GetBytes(row[0]!)).Value)];
}
