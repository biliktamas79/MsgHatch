using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Verification;

namespace MsgHatch.Subscriptions.Api.Contracts;

/// <summary>The body of <c>POST {prefix}/verification</c>: the input of a routing verification (section 4.18).</summary>
/// <param name="TestUsers">The ids of the test users, for example <c>["1", "3"]</c>.</param>
/// <param name="Languages">The UI languages of the test profiles, for example <c>["en", "hu"]</c>.</param>
/// <param name="MessageType">Only this message type, or null for each message type with a scope function.</param>
public sealed record RoutingVerificationBody(IReadOnlyList<string> TestUsers, IReadOnlyList<string> Languages, string? MessageType = null)
{
    /// <summary>Gives the body of a verification request (D228).</summary>
    /// <param name="request">The request.</param>
    /// <returns>The body.</returns>
    public static RoutingVerificationBody From(RoutingVerificationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return new RoutingVerificationBody([.. request.TestUsers.Select(static user => user.Value)], [.. request.Languages], request.Type?.Name);
    }

    /// <summary>Gives the verification request of the body. A value that is not valid gives <see cref="Errors.SerializationFailed"/> (D228).</summary>
    /// <returns>The request, or an error.</returns>
    public ErrorOr<RoutingVerificationRequest> ToRequest()
    {
        if (TestUsers is null || Languages is null)
        {
            return WireValues.Invalid("A verification needs the test users and the languages.");
        }

        var users = new UserId[TestUsers.Count];
        for (var index = 0; index < users.Length; index++)
        {
            var user = WireValues.UserOf(TestUsers[index]);
            if (user.IsError)
            {
                return user.Error;
            }

            users[index] = user.Value;
        }

        if (MessageType is null)
        {
            return new RoutingVerificationRequest(users, [.. Languages]);
        }

        var type = WireValues.MessageTypeOf(MessageType);
        return type.IsError ? type.Error : new RoutingVerificationRequest(users, [.. Languages], type.Value);
    }
}
