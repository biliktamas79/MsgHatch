using System.Text.Json.Serialization.Metadata;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Persistence;

namespace MsgHatch.Domain.DependencyInjection;

/// <summary>The setup of one channel registration of a message type (section 4.7).</summary>
/// <typeparam name="TEnvelope">The type of the channel envelope.</typeparam>
public sealed class ChannelBuilder<TEnvelope>
    where TEnvelope : class
{
    private readonly MsgHatchSetup _setup;
    private readonly string _contentType;
    private readonly Dictionary<int, Func<object, TEnvelope>> _upgrades = [];

    internal ChannelBuilder(MsgHatchSetup setup, string contentType)
    {
        _setup = setup;
        _contentType = contentType;
    }

    internal Action<RetryOptions>? Retry { get; private set; }

    internal IReadOnlyDictionary<int, Func<object, TEnvelope>> Upgrades => _upgrades;

    /// <summary>Registers an old version of the envelope, with its upgrade to the current type (section 4.13).</summary>
    /// <typeparam name="TOldEnvelope">The type of the old version.</typeparam>
    /// <param name="version">The old version.</param>
    /// <param name="json">The JSON type information of the old version.</param>
    /// <param name="upgrade">The upgrade from the old version to the current type.</param>
    /// <returns>This builder.</returns>
    public ChannelBuilder<TEnvelope> WithUpgrade<TOldEnvelope>(
        int version, JsonTypeInfo<TOldEnvelope> json, Func<TOldEnvelope, TEnvelope> upgrade)
        where TOldEnvelope : class
    {
        ArgumentNullException.ThrowIfNull(upgrade);
        _setup.ContentTypes.Add(ContentTypeRegistry.Entry(_contentType, version, typeof(OutgoingMessage<TOldEnvelope>), json));
        _upgrades.Add(version, old => upgrade((TOldEnvelope)old));
        return this;
    }

    /// <summary>
    /// Changes the retry options of this registration (D87). A value from this action wins over the configuration
    /// sections <c>MsgHatch:Retry:{Channel}</c> and <c>MsgHatch:Retry:{ContentType}</c>.
    /// </summary>
    /// <param name="configure">The change of the retry options.</param>
    /// <returns>This builder.</returns>
    public ChannelBuilder<TEnvelope> WithRetry(Action<RetryOptions> configure)
    {
        ArgumentNullException.ThrowIfNull(configure);
        Retry += configure;
        return this;
    }
}
