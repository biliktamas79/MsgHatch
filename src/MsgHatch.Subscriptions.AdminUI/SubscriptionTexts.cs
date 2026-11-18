using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.AdminUI;

internal static class SubscriptionTexts
{
    public static string OwnerOf(SubscriptionOwner owner) => owner.Level switch
    {
        SubscriptionLevel.Company => $"Company {owner.Company?.Value}",
        SubscriptionLevel.User => $"User {owner.User?.Value}",
        _ => "Default",
    };

    public static string RecipientsOf(SubscriptionRecipients recipients) => recipients.Kind switch
    {
        RecipientKind.Company => "Company",
        RecipientKind.NamedUsers => $"{recipients.Users.Count} named users",
        _ => "Scope users",
    };

    public static string? OptionalOf(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    public static IEnumerable<string> LinesOf(string value) =>
        value.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    public static ErrorOr<MessageType> MessageTypeOf(string name)
    {
        try
        {
            return new MessageType(name.Trim());
        }
        catch (ArgumentException)
        {
            return Invalid($"The message type {name} is not a name of ASCII letters, digits, and _ that starts with a letter.");
        }
    }

    public static ErrorOr<ChannelId> ChannelOf(string name)
    {
        try
        {
            return new ChannelId(name.Trim());
        }
        catch (ArgumentException)
        {
            return Invalid($"The channel {name} is not a name of ASCII letters, digits, and _ that starts with a letter.");
        }
    }

    public static ErrorOr<IReadOnlyList<UserId>> UsersOf(string lines)
    {
        var users = new List<UserId>();
        foreach (var line in LinesOf(lines))
        {
            var user = UserId.Create(line);
            if (user.IsError)
            {
                return user.Error;
            }

            users.Add(user.Value);
        }

        return users;
    }

    public static Error Invalid(string message) => new(Errors.ValidationFailed, message);
}
