using MsgHatch.Templates.Placeholders;

namespace ExampleApp.PasswordResets;

/// <summary>The payload of the email template of the message type F.</summary>
/// <param name="ResetLink">The link that resets the password.</param>
[PlaceholderPayload]
public sealed partial record PasswordResetEmail(string ResetLink);
