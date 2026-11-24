using System.Text;
using System.Text.Json;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using static MsgHatch.Data.EFCore.Tests.Integration.TestTypes.PostgreSqlTestDatabase;

namespace MsgHatch.Data.EFCore.Tests.Integration.TestTypes;

/// <summary>
/// The test data of the EF Core outbox store on PostgreSQL: it reads the rows in the order of their insert as the document
/// that <see cref="MsgHatchJson"/> reads, with <c>json_build_object</c>, and the column <c>Tries</c> in the format of
/// section 4.5. A reset deletes the rows.
/// </summary>
public sealed class PostgreSqlOutboxStoreTestData(PostgreSqlTestDatabase database) : IOutboxStoreTestData
{
    private static readonly string _documents = $"""
        SELECT json_build_object(
            'schemaVersion', "SchemaVersion",
            'delivery', json_build_object(
                'id', "Id", 'status', "Status", 'tryCount', "TryCount", 'maxTryCount', "MaxTryCount", 'createdAtUtc', {IsoTime("CreatedAtUtc")},
                'lastTryAtUtc', {IsoTime("LastTryAtUtc")}, 'nextTryAtUtc', {IsoTime("NextTryAtUtc")},
                'leaseUntilUtc', {IsoTime("LeaseUntilUtc")}, 'expiresAtUtc', {IsoTime("ExpiresAtUtc")}, 'lastError', "LastError"),
            'sourceMessageId', "SourceMessageId", 'tenant', NULLIF("TenantId", ''), 'type', "MessageType", 'channel', "Channel",
            'target', json_build_object('kind', "TargetKind", 'id', "TargetId"), 'contentType', "ContentType",
            'contentVersion', "ContentVersion", 'flags', "Flags", 'traceParent', "TraceParent", 'trace', "Trace"::json,
            'envelope', "Content"::json)::text
        FROM "msghatch"."Outbox" ORDER BY "Sequence"
        """;

    private readonly MsgHatchJson _json = new(TestContentTypes.ARegistry());

    public ValueTask ResetAsync(CancellationToken ct) => database.ResetAsync(ct);

    public async ValueTask<IReadOnlyList<OutgoingMessage>> GetStoredAsync(CancellationToken ct) =>
        [.. (await database.RowsAsync(_documents, ct)).Select(row => _json.DeserializeOutgoing(Encoding.UTF8.GetBytes(row[0]!)).Value)];

    public async ValueTask<IReadOnlyList<PublishTry>> GetTriesAsync(Guid outgoingMessageId, CancellationToken ct)
    {
        var rows = await database.RowsAsync($"""SELECT "Tries" FROM "msghatch"."Outbox" WHERE "Id" = '{outgoingMessageId:D}'""", ct);
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
