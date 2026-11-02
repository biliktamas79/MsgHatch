namespace MsgHatch.Email.Placeholders;

/// <summary>
/// Marks a template payload type of the placeholder engine. The source generator of <c>MsgHatch.SourceGeneration</c>
/// adds <see cref="IPlaceholderPayload{TSelf}"/> and a placeholder writer to the type (D18). The type is
/// <c>partial</c>, else the build fails with <c>MSGHATCH001</c>.
/// </summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class PlaceholderPayloadAttribute : Attribute;
