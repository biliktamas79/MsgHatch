using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Routing;

/// <summary>
/// The stateless rules of a registry entry (section 4.20, D171, D285). A rule that fails gives
/// <see cref="Errors.ValidationFailed"/>. The rules need no registry, so a client runs them before a call (D234). The check
/// that each disabled channel is a channel of the message type needs the registry, so the stored registry runs it after
/// the rules.
/// </summary>
public static class RegistryEntryRules
{
    private const int _maxDisplayNameLength = 200;
    private const int _maxDescriptionLength = 2000;

    /// <summary>Checks the display name, the description, and the revision of a channel entry.</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>Null for a valid entry, or the error of the first rule that fails.</returns>
    /// <exception cref="ArgumentNullException">The entry is null.</exception>
    public static Error? Check(ChannelEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return CheckCommon(entry.DisplayName, entry.Description, entry.Revision);
    }

    /// <summary>
    /// Checks the display name, the description, and the revision of a message type entry, and that
    /// <see cref="MessageTypeEntry.DisabledChannels"/> is not null and has each channel one time.
    /// </summary>
    /// <param name="entry">The entry.</param>
    /// <returns>Null for a valid entry, or the error of the first rule that fails.</returns>
    /// <exception cref="ArgumentNullException">The entry is null.</exception>
    public static Error? Check(MessageTypeEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return CheckCommon(entry.DisplayName, entry.Description, entry.Revision) ?? CheckDisabledChannels(entry.DisabledChannels);
    }

    private static Error? CheckCommon(string? displayName, string? description, int revision) =>
        CheckText(displayName, nameof(ChannelEntry.DisplayName), _maxDisplayNameLength)
        ?? CheckText(description, nameof(ChannelEntry.Description), _maxDescriptionLength)
        ?? (revision < 0 ? Invalid("Revision is the expected revision, 0 or more.") : null);

    private static Error? CheckText(string? text, string name, int maxLength) =>
        text is null ? null
        : string.IsNullOrWhiteSpace(text) ? Invalid($"{name} is null or has a character that is not white space.")
        : text.Length > maxLength ? Invalid($"{name} has at most {maxLength} characters.")
        : null;

    private static Error? CheckDisabledChannels(IReadOnlyList<ChannelId>? disabledChannels) =>
        disabledChannels is null ? Invalid("DisabledChannels is not null.")
        : disabledChannels.Distinct().Count() != disabledChannels.Count ? Invalid("DisabledChannels has each channel one time.")
        : null;

    private static Error Invalid(string message) => new(Errors.ValidationFailed, message);
}
