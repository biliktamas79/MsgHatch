using System.Text;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.Contracts;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using static MsgHatch.Data.EFCore.Tests.Integration.TestTypes.MySqlTestDatabase;

namespace MsgHatch.Data.EFCore.Tests.Integration.TestTypes;

/// <summary>
/// The test data of the EF Core inbox store on MySQL: it reads the rows in the order of their insert as the document that
/// <see cref="MsgHatchJson"/> reads, with <see cref="MySqlTestDatabase.JsonObject"/>, and a reset empties the tables (<see cref="MySqlTestDatabase.ResetAsync"/>).
/// </summary>
public sealed class MySqlInboxStoreTestData(MySqlTestDatabase database) : IInboxStoreTestData
{
    private readonly string _documents = $"""
        SELECT {JsonObject(
            ("schemaVersion", "`SchemaVersion`"),
            ("delivery", JsonObject(
                ("id", JsonText("Id")), ("status", JsonText("Status")), ("tryCount", "`TryCount`"), ("maxTryCount", "`MaxTryCount`"),
                ("createdAtUtc", IsoTime("CreatedAtUtc")), ("lastTryAtUtc", IsoTime("LastTryAtUtc")), ("nextTryAtUtc", IsoTime("NextTryAtUtc")),
                ("leaseUntilUtc", IsoTime("LeaseUntilUtc")), ("expiresAtUtc", IsoTime("ExpiresAtUtc")), ("lastError", JsonText("LastError")))),
            ("tenant", "JSON_QUOTE(NULLIF(`TenantId`, ''))"), ("type", JsonText("ContentType")), ("contentVersion", "`ContentVersion`"),
            ("flags", JsonText("Flags")), ("traceParent", JsonText("TraceParent")), ("event", "`Content`"))}
        FROM {database.Table("Inbox")} ORDER BY `Sequence`
        """;

    private readonly MsgHatchJson _json = new(TestContentTypes.ARegistry());

    public ValueTask ResetAsync(CancellationToken ct) => database.ResetAsync(ct);

    public async ValueTask<IReadOnlyList<ReceivedMessage>> GetStoredAsync(CancellationToken ct) =>
        [.. (await database.RowsAsync(_documents, ct)).Select(row => _json.DeserializeReceived(Encoding.UTF8.GetBytes(row[0]!)).Value)];
}
