using System.Text.Json.Serialization;

namespace ExampleApp;

/// <summary>The domain event of the message type F: a password reset, which <see cref="PasswordResetRouter"/> routes.</summary>
/// <param name="UserId">The user who asked for the reset.</param>
/// <param name="Email">The email address of the user.</param>
/// <param name="Language">The language of the email.</param>
public sealed record PasswordReset(
    [property: JsonPropertyName("userId")] int UserId,
    [property: JsonPropertyName("email")] string Email,
    [property: JsonPropertyName("language")] string Language);
