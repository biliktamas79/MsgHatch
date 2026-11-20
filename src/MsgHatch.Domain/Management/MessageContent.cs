namespace MsgHatch.Domain.Management;

/// <summary>
/// A whole received or outgoing message with its event or its envelope, as the JSON text that <c>MsgHatchJson</c> writes with
/// the content type registry of the server (D109, D290). An admin host has no message types (D235), so it shows the text.
/// </summary>
/// <param name="Id">The id of the message.</param>
/// <param name="Json">The JSON text of the message.</param>
public sealed record MessageContent(Guid Id, string Json);
