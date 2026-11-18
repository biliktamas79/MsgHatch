namespace MsgHatch.Templates.Management;

/// <summary>The preview of a draft for one template registration with a sample payload (D254, D256).</summary>
/// <param name="Registration">The name of the registration, for example <c>Email:OrderShipped</c>.</param>
/// <param name="Text">The text that the engine of the registration rendered with the sample payload.</param>
public sealed record TemplatePreview(string Registration, string Text);
