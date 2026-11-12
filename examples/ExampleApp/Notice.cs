using System.Text.Json.Serialization;

namespace ExampleApp;

/// <summary>The payload of the SignalR and broker messages of the example. The outbox stores it in the envelope.</summary>
/// <param name="Type">The name of the message type.</param>
/// <param name="Text">The text of the notification.</param>
public sealed record Notice(
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("text")] string Text);
