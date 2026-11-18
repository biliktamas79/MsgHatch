using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Api.Contracts;
using MsgHatch.Subscriptions.Verification;

namespace MsgHatch.Subscriptions.AspNetCore;

/// <summary>
/// Checks the texts of a query or a body, as each value from outside (section 4.4). An invalid value gives
/// <see cref="Errors.ValidationFailed"/>, and so 400.
/// </summary>
internal static class RequestValues
{
    public static ErrorOr<T?> Optional<T>(string? value, Func<string, ErrorOr<T>> parse)
        where T : struct
    {
        if (value is null)
        {
            return (T?)null;
        }

        var parsed = parse(value);
        return parsed.IsError ? parsed.Error : parsed.Value;
    }

    public static ErrorOr<MessageType> MessageTypeOf(string name) => NameOf(name, static valid => new MessageType(valid));

    public static ErrorOr<ChannelId> ChannelOf(string name) => NameOf(name, static valid => new ChannelId(valid));

    public static ErrorOr<SubscriptionLevel> LevelOf(string level) =>
        Enum.GetNames<SubscriptionLevel>().Contains(level, StringComparer.Ordinal)
            ? Enum.Parse<SubscriptionLevel>(level)
            : new Error(Errors.ValidationFailed, $"The level {level} is not Default, Company, or User.");

    public static ErrorOr<SubscriptionOwner> OwnerOf(SubscriptionRequest request) =>
        (request.Level, request.CompanyId, request.UserId) switch
        {
            (SubscriptionLevel.Default, null, null) => SubscriptionOwner.Default,
            (SubscriptionLevel.Company, { } company, null) =>
                CompanyId.Create(company).ThenIfSuccess<SubscriptionOwner>(static id => SubscriptionOwner.ForCompany(id)),
            (SubscriptionLevel.User, null, { } user) =>
                UserId.Create(user).ThenIfSuccess<SubscriptionOwner>(static id => SubscriptionOwner.ForUser(id)),
            _ => new Error(Errors.ValidationFailed, "The owner has the level Default, Company, or User, and only the id of its level."),
        };

    public static ErrorOr<SubscriptionRecipients?> RecipientsOf(SubscriptionRecipientsBody? body)
    {
        if (body is null)
        {
            return (SubscriptionRecipients?)null;
        }

        var users = body.Users ?? [];
        return body.Kind switch
        {
            RecipientKind.ScopeUsers when users.Count == 0 => SubscriptionRecipients.ScopeUsers,
            RecipientKind.Company when users.Count == 0 => SubscriptionRecipients.Company,
            RecipientKind.NamedUsers => NamedUsersOf(users),
            _ => new Error(Errors.ValidationFailed, "The recipients are ScopeUsers or Company with no users, or NamedUsers."),
        };
    }

    public static ErrorOr<RoutingVerificationRequest> VerificationRequestOf(ErrorOr<RoutingVerificationBody> read)
    {
        if (read.IsError)
        {
            return read.Error;
        }

        var body = read.Value;
        if (body.TestUsers is null || body.Languages is null || body.Languages.Any(string.IsNullOrWhiteSpace))
        {
            return new Error(Errors.ValidationFailed, "A verification needs the test users and the languages, and no language is empty.");
        }

        var users = UsersOf(body.TestUsers);
        var type = Optional(body.MessageType, MessageTypeOf);
        return users.Error ?? type.Error ?? (ErrorOr<RoutingVerificationRequest>)new RoutingVerificationRequest(users.Value, body.Languages, type.Value);
    }

    private static ErrorOr<SubscriptionRecipients?> NamedUsersOf(IReadOnlyList<string> users)
    {
        var ids = UsersOf(users);
        return ids.IsError ? ids.Error : SubscriptionRecipients.ForUsers(ids.Value);
    }

    private static ErrorOr<UserId[]> UsersOf(IReadOnlyList<string> users)
    {
        var ids = new UserId[users.Count];
        for (var index = 0; index < users.Count; index++)
        {
            var id = UserId.Create(users[index]);
            if (id.IsError)
            {
                return id.Error;
            }

            ids[index] = id.Value;
        }

        return ids;
    }

    private static ErrorOr<T> NameOf<T>(string name, Func<string, T> create)
    {
        try
        {
            return create(name);
        }
        catch (ArgumentException exception)
        {
            return new Error(Errors.ValidationFailed, exception.Message);
        }
    }
}
