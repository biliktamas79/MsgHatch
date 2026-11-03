using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Broker;

/// <summary>
/// Checks the settings of a broker route (D77): the settings type, and the header names. A header name is not empty,
/// and does not start with <c>msghatch-</c>, because the broker transport uses these names.
/// </summary>
public sealed class BrokerChannelSettingsValidator : IChannelSettingsValidator
{
    private const string _libraryPrefix = "msghatch-";

    /// <inheritdoc/>
    public ChannelId Channel => BrokerChannel.Id;

    /// <inheritdoc/>
    public Error? Validate(ChannelSettings settings, ChannelFeatures features) => settings switch
    {
        BrokerChannelSettings { Headers: { } headers } when headers.Keys.Any(IsInvalidName) =>
            new Error(Errors.ValidationFailed, $"A header name is empty, or starts with {_libraryPrefix}."),
        BrokerChannelSettings => null,
        _ => new Error(Errors.ValidationFailed, "The settings are not broker settings."),
    };

    private static bool IsInvalidName(string name) =>
        string.IsNullOrWhiteSpace(name) || name.StartsWith(_libraryPrefix, StringComparison.OrdinalIgnoreCase);
}
