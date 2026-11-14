using System.Globalization;
using System.Text;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Persistence;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Data.MongoDb.Tests.Integration.TestTypes;

/// <summary>
/// Reads the stored message documents back in the test data abstractions, apart from the code of the stores: it takes
/// out <c>_id</c> and <c>tries</c>, writes each time <c>{ d, t }</c> of the delivery state as ISO 8601 text from its
/// ticks, and gives the relaxed JSON of the driver to <see cref="MsgHatchJson"/>.
/// </summary>
public static class StoredDocuments
{
    private static readonly MsgHatchJson _json = new(TestContentTypes.ARegistry());

    private static readonly JsonWriterSettings _relaxed = new() { OutputMode = JsonOutputMode.RelaxedExtendedJson };

    public static ReceivedMessage Received(BsonDocument document) => _json.DeserializeReceived(Utf8Json(document)).Value;

    public static OutgoingMessage Outgoing(BsonDocument document) => _json.DeserializeOutgoing(Utf8Json(document)).Value;

    /// <summary>The time of a sub-document <c>{ d, t }</c>, from its ticks.</summary>
    public static DateTime? TimeOf(BsonValue time) => time.IsBsonNull ? null : new DateTime(time["t"].AsInt64, DateTimeKind.Utc);

    public static PublishTry TryOf(Guid outgoingMessageId, BsonDocument publishTry) =>
        new(
            outgoingMessageId, publishTry["tryNumber"].AsInt32, TimeOf(publishTry["startedAtUtc"])!.Value, TimeOf(publishTry["finishedAtUtc"])!.Value,
            Enum.Parse<PublishOutcome>(publishTry["outcome"].AsString), Text(publishTry["providerMessageId"]),
            publishTry["errorCode"].IsBsonNull ? null : publishTry["errorCode"].AsInt32, Text(publishTry["errorName"]), Text(publishTry["errorMessage"]));

    private static string? Text(BsonValue value) => value.IsBsonNull ? null : value.AsString;

    private static byte[] Utf8Json(BsonDocument stored)
    {
        var document = stored.DeepClone().AsBsonDocument;
        document.Remove("_id");
        document.Remove("tries");
        var delivery = document["delivery"].AsBsonDocument;
        foreach (var name in delivery.Names.ToArray())
        {
            if (delivery[name] is BsonDocument time)
            {
                delivery[name] = TimeOf(time)!.Value.ToString("O", CultureInfo.InvariantCulture);
            }
        }

        return Encoding.UTF8.GetBytes(document.ToJson(_relaxed));
    }
}
