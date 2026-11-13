using System.Text.Json.Serialization;

namespace MsgHatch.Subscriptions.Data.EFCore;

/// <summary>The JSON type information of a list of ids as a JSON array of strings: the column <c>NamedUsers</c>.</summary>
[JsonSourceGenerationOptions(RespectNullableAnnotations = true)]
[JsonSerializable(typeof(IReadOnlyList<string>))]
internal sealed partial class IdsJson : JsonSerializerContext;
