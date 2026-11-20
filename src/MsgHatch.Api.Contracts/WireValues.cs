using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Api.Contracts;

/// <summary>The checks of the names in a response body, which give <see cref="Errors.SerializationFailed"/> (D228).</summary>
internal static class WireValues
{
    public static Error Invalid(string message) => new(Errors.SerializationFailed, message);

    public static ErrorOr<ChannelId> ChannelOf(string? name) => NameOf(name, "channel", static valid => new ChannelId(valid));

    public static ErrorOr<MessageType> MessageTypeOf(string? name) => NameOf(name, "message type", static valid => new MessageType(valid));

    /// <summary>Gives the tenant of a body, or null for a body with no tenant.</summary>
    public static ErrorOr<TenantId?> TenantOf(string? id)
    {
        if (id is null)
        {
            return ErrorOr<TenantId?>.ForValue(null);
        }

        var tenant = TenantId.Create(id);
        return tenant.IsError ? Invalid($"The tenant id is not valid: {tenant.Error.Message}") : tenant.Value;
    }

    public static ErrorOr<IReadOnlyList<ChannelId>> ChannelsOf(IReadOnlyList<string>? names)
    {
        if (names is null)
        {
            return Invalid("A message type has a list of channels and a list of disabled channels.");
        }

        var channels = new ChannelId[names.Count];
        for (var index = 0; index < channels.Length; index++)
        {
            var channel = ChannelOf(names[index]);
            if (channel.IsError)
            {
                return channel.Error;
            }

            channels[index] = channel.Value;
        }

        return channels;
    }

    private static ErrorOr<T> NameOf<T>(string? name, string kind, Func<string, T> create)
    {
        if (name is null)
        {
            return Invalid($"A {kind} has a name.");
        }

        try
        {
            return create(name);
        }
        catch (ArgumentException exception)
        {
            return Invalid($"The {kind} name is not valid: {exception.Message}");
        }
    }
}
