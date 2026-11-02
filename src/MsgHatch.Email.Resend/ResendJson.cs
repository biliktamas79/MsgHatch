using System.Text.Json.Serialization;

namespace MsgHatch.Email.Resend;

/// <summary>The body of the request that sends one email.</summary>
internal sealed record ResendEmail(
    string From, string[] To, string Subject, string? Html, string? Text, string[]? Cc, string[]? Bcc, ResendTag[] Tags);

/// <summary>A tag of an email. The name and the value hold only ASCII letters, digits, <c>_</c>, and <c>-</c>.</summary>
internal sealed record ResendTag(string Name, string Value);

/// <summary>The body of the response to a sent email.</summary>
internal sealed record ResendSentEmail(string? Id);

/// <summary>The body of an error response. The message can contain an address, so the client reads only the name.</summary>
internal sealed record ResendError(string? Name);

/// <summary>The JSON type information of the Resend API. Names are snake_case, and a null value is left out.</summary>
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.SnakeCaseLower, DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull)]
[JsonSerializable(typeof(ResendEmail))]
[JsonSerializable(typeof(ResendSentEmail))]
[JsonSerializable(typeof(ResendError))]
internal sealed partial class ResendJson : JsonSerializerContext;
