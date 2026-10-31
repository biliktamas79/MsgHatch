namespace MsgHatch.Domain.Identity;

/// <summary>The id of a channel, for example <c>Email</c>. Each channel package defines its own id (D2).</summary>
/// <param name="Name">
/// The name. It contains only ASCII letters, digits, and <c>_</c>, and starts with a letter.
/// An invalid name throws <see cref="ArgumentException"/>.
/// </param>
public readonly record struct ChannelId(string Name)
{
    /// <summary>The name.</summary>
    public string Name { get; } = IdentityNames.Validate(Name, nameof(Name));
}
