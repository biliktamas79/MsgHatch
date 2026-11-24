using System.Text;
using System.Text.Json;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.EFCore.Tests.Integration.TestTypes;

/// <summary>
/// The test data of the EF Core outbox store: it reads the rows in the order of their insert as the document that
/// <see cref="MsgHatchJson"/> reads, with <c>FOR JSON</c>, and the column <c>Tries</c> in the format of section 4.5. A
/// reset deletes the rows.
/// </summary>
public sealed class EntityFrameworkOutboxStoreTestData(TestDatabase database) : IOutboxStoreTestData
{
    private const string _documents = """
        SELECT (
            SELECT
                SchemaVersion AS schemaVersion, Id AS [delivery.id], Status AS [delivery.status], TryCount AS [delivery.tryCount],
                MaxTryCount AS [delivery.maxTryCount], CreatedAtUtc AS [delivery.createdAtUtc], LastTryAtUtc AS [delivery.lastTryAtUtc],
                NextTryAtUtc AS [delivery.nextTryAtUtc], LeaseUntilUtc AS [delivery.leaseUntilUtc], ExpiresAtUtc AS [delivery.expiresAtUtc],
                LastError AS [delivery.lastError], SourceMessageId AS sourceMessageId, NULLIF(TenantId, '') AS tenant, MessageType AS type,
                Channel AS channel, TargetKind AS [target.kind], TargetId AS [target.id], ContentType AS contentType,
                ContentVersion AS contentVersion, Flags AS flags, TraceParent AS traceParent, JSON_QUERY(Trace) AS trace,
                JSON_QUERY(Content) AS envelope
            FOR JSON PATH, INCLUDE_NULL_VALUES, WITHOUT_ARRAY_WRAPPER)
        FROM [msghatch].[Outbox] ORDER BY Sequence
        """;

    private readonly MsgHatchJson _json = new(TestContentTypes.ARegistry());

    public ValueTask ResetAsync(CancellationToken ct) => database.ResetAsync(ct);

    public async ValueTask<IReadOnlyList<OutgoingMessage>> GetStoredAsync(CancellationToken ct) =>
        [.. (await database.RowsAsync(_documents, ct)).Select(row => _json.DeserializeOutgoing(Encoding.UTF8.GetBytes(row[0]!)).Value)];

    public async ValueTask<IReadOnlyList<PublishTry>> GetTriesAsync(Guid outgoingMessageId, CancellationToken ct)
    {
        var rows = await database.RowsAsync($"SELECT Tries FROM [msghatch].[Outbox] WHERE Id = '{outgoingMessageId:D}'", ct);
        using var array = JsonDocument.Parse(Assert.Single(rows)[0]!);
        return [.. array.RootElement.EnumerateArray().Select(element => TryOf(outgoingMessageId, element))];
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
