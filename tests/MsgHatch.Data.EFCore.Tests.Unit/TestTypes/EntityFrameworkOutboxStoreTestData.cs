using System.Text;
using System.Text.Json;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using static MsgHatch.Data.EFCore.Tests.Unit.TestTypes.SqliteTestDatabase;

namespace MsgHatch.Data.EFCore.Tests.Unit.TestTypes;

/// <summary>
/// The test data of the EF Core outbox store on SQLite: it reads the rows in the order of their insert as the document
/// that <see cref="MsgHatchJson"/> reads, and the column <c>Tries</c> in the format of section 4.5. A reset makes a new
/// file with the tables.
/// </summary>
public sealed class EntityFrameworkOutboxStoreTestData(SqliteTestDatabase database) : IOutboxStoreTestData
{
    private static readonly string _documents = $"""
        SELECT json_object(
            'schemaVersion', SchemaVersion,
            'delivery', json_object(
                'id', Id, 'status', Status, 'tryCount', TryCount, 'maxTryCount', MaxTryCount, 'createdAtUtc', {IsoTime("CreatedAtUtc")},
                'lastTryAtUtc', {IsoTime("LastTryAtUtc")}, 'nextTryAtUtc', {IsoTime("NextTryAtUtc")},
                'leaseUntilUtc', {IsoTime("LeaseUntilUtc")}, 'expiresAtUtc', {IsoTime("ExpiresAtUtc")}, 'lastError', LastError),
            'sourceMessageId', SourceMessageId, 'tenant', NULLIF(TenantId, ''), 'type', MessageType, 'channel', Channel,
            'target', json_object('kind', TargetKind, 'id', TargetId), 'contentType', ContentType,
            'contentVersion', ContentVersion, 'flags', Flags, 'traceParent', TraceParent, 'trace', json(Trace),
            'envelope', json(Content))
        FROM MsgHatchOutbox ORDER BY Sequence
        """;

    private readonly MsgHatchJson _json = new(TestContentTypes.ARegistry());

    public ValueTask ResetAsync(CancellationToken ct) => database.ResetAsync(database.Contexts, ct);

    public ValueTask<IReadOnlyList<OutgoingMessage>> GetStoredAsync(CancellationToken ct) =>
        ValueTask.FromResult<IReadOnlyList<OutgoingMessage>>(
            [.. database.Rows(_documents).Select(row => _json.DeserializeOutgoing(Encoding.UTF8.GetBytes(row[0]!)).Value)]);

    public ValueTask<IReadOnlyList<PublishTry>> GetTriesAsync(Guid outgoingMessageId, CancellationToken ct)
    {
        using var array = JsonDocument.Parse(Assert.Single(database.Rows($"SELECT Tries FROM MsgHatchOutbox WHERE Id = '{outgoingMessageId:D}'"))[0]!);
        return ValueTask.FromResult<IReadOnlyList<PublishTry>>([.. array.RootElement.EnumerateArray().Select(element => TryOf(outgoingMessageId, element))]);
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
