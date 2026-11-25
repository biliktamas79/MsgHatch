using System.Text;
using System.Text.Json;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using static MsgHatch.Data.EFCore.Tests.Integration.TestTypes.MySqlTestDatabase;

namespace MsgHatch.Data.EFCore.Tests.Integration.TestTypes;

/// <summary>
/// The test data of the EF Core outbox store on MySQL: it reads the rows in the order of their insert as the document that
/// <see cref="MsgHatchJson"/> reads, with <see cref="MySqlTestDatabase.JsonObject"/>, and the column <c>Tries</c> in the
/// format of section 4.5. A reset empties the tables (<see cref="MySqlTestDatabase.ResetAsync"/>).
/// </summary>
public sealed class MySqlOutboxStoreTestData(MySqlTestDatabase database) : IOutboxStoreTestData
{
    private readonly string _documents = $"""
        SELECT {JsonObject(
            ("schemaVersion", "`SchemaVersion`"),
            ("delivery", JsonObject(
                ("id", JsonText("Id")), ("status", JsonText("Status")), ("tryCount", "`TryCount`"), ("maxTryCount", "`MaxTryCount`"),
                ("createdAtUtc", IsoTime("CreatedAtUtc")), ("lastTryAtUtc", IsoTime("LastTryAtUtc")), ("nextTryAtUtc", IsoTime("NextTryAtUtc")),
                ("leaseUntilUtc", IsoTime("LeaseUntilUtc")), ("expiresAtUtc", IsoTime("ExpiresAtUtc")), ("lastError", JsonText("LastError")))),
            ("sourceMessageId", JsonText("SourceMessageId")), ("tenant", "JSON_QUOTE(NULLIF(`TenantId`, ''))"), ("type", JsonText("MessageType")),
            ("channel", JsonText("Channel")), ("target", JsonObject(("kind", JsonText("TargetKind")), ("id", JsonText("TargetId")))),
            ("contentType", JsonText("ContentType")), ("contentVersion", "`ContentVersion`"), ("flags", JsonText("Flags")),
            ("traceParent", JsonText("TraceParent")), ("trace", "`Trace`"), ("envelope", "`Content`"))}
        FROM {database.Table("Outbox")} ORDER BY `Sequence`
        """;

    private readonly MsgHatchJson _json = new(TestContentTypes.ARegistry());

    public ValueTask ResetAsync(CancellationToken ct) => database.ResetAsync(ct);

    public async ValueTask<IReadOnlyList<OutgoingMessage>> GetStoredAsync(CancellationToken ct) =>
        [.. (await database.RowsAsync(_documents, ct)).Select(row => _json.DeserializeOutgoing(Encoding.UTF8.GetBytes(row[0]!)).Value)];

    public async ValueTask<IReadOnlyList<PublishTry>> GetTriesAsync(Guid outgoingMessageId, CancellationToken ct)
    {
        var rows = await database.RowsAsync($"SELECT `Tries` FROM {database.Table("Outbox")} WHERE `Id` = '{outgoingMessageId:D}'", ct);
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
