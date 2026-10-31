namespace MsgHatch.Domain.Identity;

/// <summary>The registered name of a kind of notification, for example <c>OrderShipped</c>.</summary>
/// <param name="Name">
/// The name. It contains only ASCII letters, digits, and <c>_</c>, and starts with a letter.
/// An invalid name throws <see cref="ArgumentException"/>.
/// </param>
public readonly record struct MessageType(string Name)
{
    /// <summary>The name.</summary>
    public string Name { get; } = IdentityNames.Validate(Name, nameof(Name));
}
