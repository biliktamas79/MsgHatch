using System.Text.Json.Serialization;

namespace ExampleApp.Notices;

/// <summary>The domain event of the message type A of the subscription example. Its channel: Email.</summary>
/// <param name="UserIds">The users of a user scope, or null for a company scope.</param>
/// <param name="CompanyId">The company of a company scope, or null for a user scope.</param>
/// <param name="Text">The text of the notification.</param>
public sealed record EventA(
    [property: JsonPropertyName("userIds")] IReadOnlyList<int>? UserIds,
    [property: JsonPropertyName("companyId")] int? CompanyId,
    [property: JsonPropertyName("text")] string Text) : IExampleEvent;
