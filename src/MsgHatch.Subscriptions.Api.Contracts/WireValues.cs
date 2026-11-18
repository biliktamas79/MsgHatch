using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.Api.Contracts;

/// <summary>
/// Checks each value of a body from the wire, as each value from outside (D228). A value that is not valid gives
/// <see cref="Errors.SerializationFailed"/>, because the other side wrote a body that the contract does not allow.
/// </summary>
internal static class WireValues
{
    public static ErrorOr<MessageType> MessageTypeOf(string name) => NameOf(name, static valid => new MessageType(valid), "message type");

    public static ErrorOr<ChannelId> ChannelOf(string name) => NameOf(name, static valid => new ChannelId(valid), "channel");

    public static ErrorOr<ChannelId?> OptionalChannelOf(string? name)
    {
        if (name is null)
        {
            return (ChannelId?)null;
        }

        var channel = ChannelOf(name);
        return channel.IsError ? channel.Error : channel.Value;
    }

    public static ErrorOr<UserId> UserOf(string id) => Checked(UserId.Create(id), "user id");

    public static ErrorOr<CompanyId> CompanyOf(string id) => Checked(CompanyId.Create(id), "company id");

    public static ErrorOr<SubscriptionOwner> OwnerOf(SubscriptionLevel level, string? companyId, string? userId) =>
        (level, companyId, userId) switch
        {
            (SubscriptionLevel.Default, null, null) => SubscriptionOwner.Default,
            (SubscriptionLevel.Company, { } company, null) => CompanyOf(company).ThenIfSuccess<SubscriptionOwner>(static id => SubscriptionOwner.ForCompany(id)),
            (SubscriptionLevel.User, null, { } user) => UserOf(user).ThenIfSuccess<SubscriptionOwner>(static id => SubscriptionOwner.ForUser(id)),
            _ => Invalid("The owner has the level Default, Company, or User, and only the id of its level."),
        };

    public static ErrorOr<T> EnumOf<T>(T value, string field)
        where T : struct, Enum =>
        Enum.IsDefined(value) ? value : Invalid($"The {field} {value} is not known.");

    public static Error Invalid(string message) => new(Errors.SerializationFailed, message);

    private static ErrorOr<T> Checked<T>(ErrorOr<T> created, string field) =>
        created.IsError ? Invalid($"The {field} is not valid: {created.Error.Message}") : created;

    private static ErrorOr<T> NameOf<T>(string name, Func<string, T> create, string field)
    {
        try
        {
            return create(name);
        }
        catch (ArgumentException exception)
        {
            return Invalid($"The {field} is not valid: {exception.Message}");
        }
    }
}
