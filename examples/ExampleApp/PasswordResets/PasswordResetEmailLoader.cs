using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;

namespace ExampleApp.PasswordResets;

/// <summary>Gives the reset link of a password reset. The link holds the message id, so a second try sends the same link.</summary>
public sealed class PasswordResetEmailLoader : IPayloadLoader<PasswordReset, PasswordResetEmail>
{
    /// <inheritdoc/>
    public ValueTask<ErrorOr<IReadOnlyList<PasswordResetEmail>>> LoadAsync(
        ReceivedMessage<PasswordReset> message, ReadOnlyMemory<MessageRoute> routes, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(message);
        var payload = new PasswordResetEmail($"https://app.example/reset?message={message.Delivery.Id}");
        return ValueTask.FromResult(ErrorOr<IReadOnlyList<PasswordResetEmail>>.ForValue([.. Enumerable.Repeat(payload, routes.Length)]));
    }
}
