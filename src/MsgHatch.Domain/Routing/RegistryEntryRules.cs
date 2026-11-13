using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Routing;

/// <summary>The validation rules of a registry entry (section 4.20, D171). A rule that fails gives <see cref="Errors.ValidationFailed"/>.</summary>
internal static class RegistryEntryRules
{
    private const int _maxDisplayNameLength = 200;
    private const int _maxDescriptionLength = 2000;

    public static Error? Check(ChannelEntry entry) => CheckCommon(entry.DisplayName, entry.Description, entry.Revision);

    public static Error? Check(MessageTypeEntry entry, MessageTypeDescriptor registered) =>
        CheckCommon(entry.DisplayName, entry.Description, entry.Revision) ?? CheckDisabledChannels(entry.DisabledChannels, registered);

    private static Error? CheckCommon(string? displayName, string? description, int revision) =>
        CheckText(displayName, nameof(ChannelEntry.DisplayName), _maxDisplayNameLength)
        ?? CheckText(description, nameof(ChannelEntry.Description), _maxDescriptionLength)
        ?? (revision < 0 ? Invalid("Revision is the expected revision, 0 or more.") : null);

    private static Error? CheckText(string? text, string name, int maxLength) =>
        text is null ? null
        : string.IsNullOrWhiteSpace(text) ? Invalid($"{name} is null or has a character that is not white space.")
        : text.Length > maxLength ? Invalid($"{name} has at most {maxLength} characters.")
        : null;

    private static Error? CheckDisabledChannels(IReadOnlyList<ChannelId>? disabledChannels, MessageTypeDescriptor registered) =>
        disabledChannels is null ? Invalid("DisabledChannels is not null.")
        : disabledChannels.Any(channel => !registered.Channels.Contains(channel))
            ? Invalid($"DisabledChannels has only the channels of the message type {registered.Type.Name}.")
        : disabledChannels.Distinct().Count() != disabledChannels.Count ? Invalid("DisabledChannels has each channel one time.")
        : null;

    private static Error Invalid(string message) => new(Errors.ValidationFailed, message);
}
