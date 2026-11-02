using System.Text.Json.Serialization;

namespace MsgHatch.Email;

/// <summary>
/// The JSON type information of the email envelope and the email settings, so that an email registration needs no
/// JSON type information (D17). Names are camelCase and enum values are strings (D23).
/// </summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, UseStringEnumConverter = true)]
[JsonSerializable(typeof(EmailMessage))]
[JsonSerializable(typeof(EmailChannelSettings))]
internal sealed partial class EmailJson : JsonSerializerContext;
