using System.Text.Json.Serialization;

namespace MsgHatch.Subscriptions.Data.Sqlite;

/// <summary>
/// The JSON type information of a list of ids as a JSON array of strings: the column <c>NamedUsers</c>, and the
/// companies and the users of <c>FindForDeliveryAsync</c> for <c>json_each</c>.
/// </summary>
[JsonSourceGenerationOptions(RespectNullableAnnotations = true)]
[JsonSerializable(typeof(IReadOnlyList<string>))]
internal sealed partial class IdsJson : JsonSerializerContext;
