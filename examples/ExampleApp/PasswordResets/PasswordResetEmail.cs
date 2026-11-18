namespace ExampleApp.PasswordResets;

/// <summary>The payload of the email template of the message type F, which the Liquid engine renders (D253).</summary>
/// <param name="ResetLink">The link that resets the password.</param>
public sealed record PasswordResetEmail(string ResetLink);
