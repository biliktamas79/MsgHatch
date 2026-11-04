using System.Text.Json;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.Data.FileSystem;

/// <summary>
/// The JSON document of one subscription file (section 4.5): the schema version, the tenant, the fields of
/// <see cref="Subscription"/>, and the channel settings as the JSON object <c>settings</c> with <c>settingsType</c> and
/// <c>settingsVersion</c> (D149).
/// </summary>
internal sealed record SubscriptionFile(
    int SchemaVersion,
    Guid Id,
    int Revision,
    string? Tenant,
    SubscriptionFileOwner Owner,
    string Type,
    SubscriptionKind Kind,
    string? Channel,
    string? SettingsType,
    int? SettingsVersion,
    JsonElement? Settings,
    SubscriptionFileRecipients Recipients,
    bool Mandatory,
    DateTime CreatedAtUtc,
    DateTime LastModifiedAtUtc)
{
    private const int _currentSchemaVersion = 1;

    public static ErrorOr<SubscriptionFile> From(Subscription subscription, TenantId? tenant, ChannelSettingsJson settingsJson)
    {
        SerializedSettings? settings = null;
        if (subscription.Settings is { } value)
        {
            var serialized = settingsJson.Serialize(subscription.Channel.GetValueOrDefault(), value);
            if (serialized.IsError)
            {
                return serialized.Error!;
            }

            settings = serialized.Value;
        }

        return new SubscriptionFile(
            _currentSchemaVersion, subscription.Id, subscription.Revision, tenant?.Value, SubscriptionFileOwner.From(subscription.Owner),
            subscription.Type.Name, subscription.Kind, subscription.Channel?.Name, settings?.ContentType, settings?.Version,
            settings is null ? null : ElementOf(settings.Json), SubscriptionFileRecipients.From(subscription.Recipients),
            subscription.Mandatory, subscription.CreatedAtUtc, subscription.LastModifiedAtUtc);
    }

    /// <summary>Gives the subscription of the file.</summary>
    /// <exception cref="JsonException">The owner does not match its level.</exception>
    /// <exception cref="ArgumentException">An id or a name in the file is not valid.</exception>
    public ErrorOr<Subscription> ToSubscription(ChannelSettingsJson settingsJson)
    {
        if (SchemaVersion > _currentSchemaVersion)
        {
            return new Error(Errors.UnsupportedVersion, $"The schema version {SchemaVersion} of a subscription file is newer than the code.");
        }

        ChannelSettings? settings = null;
        if (Settings is { } element)
        {
            var read = settingsJson.Deserialize(SettingsType ?? string.Empty, SettingsVersion ?? 0, element.GetRawText());
            if (read.IsError)
            {
                return read.Error!;
            }

            settings = read.Value;
        }

        return new Subscription(
            Id, Revision, Owner.ToOwner(), new MessageType(Type), Kind, Channel is null ? null : new ChannelId(Channel), settings,
            Recipients.ToRecipients(), Mandatory, CreatedAtUtc, LastModifiedAtUtc);
    }

    private static JsonElement ElementOf(string json)
    {
        using var document = JsonDocument.Parse(json);
        return document.RootElement.Clone();
    }
}

/// <summary>The owner of a subscription file: <c>{ "level": "User", "company": null, "user": "1" }</c>.</summary>
internal sealed record SubscriptionFileOwner(SubscriptionLevel Level, string? Company, string? User)
{
    public static SubscriptionFileOwner From(SubscriptionOwner owner) => new(owner.Level, owner.Company?.Value, owner.User?.Value);

    public SubscriptionOwner ToOwner() => (Level, Company, User) switch
    {
        (SubscriptionLevel.Default, null, null) => SubscriptionOwner.Default,
        (SubscriptionLevel.Company, { } company, null) => SubscriptionOwner.ForCompany(new CompanyId(company)),
        (SubscriptionLevel.User, null, { } user) => SubscriptionOwner.ForUser(new UserId(user)),
        _ => throw new JsonException("A subscription owner has the level Default, Company, or User, and only the id of its level."),
    };
}

/// <summary>The recipients of a subscription file: the kind, and the named users in their order.</summary>
internal sealed record SubscriptionFileRecipients(RecipientKind Kind, IReadOnlyList<string> Users)
{
    public static SubscriptionFileRecipients From(SubscriptionRecipients recipients) =>
        new(recipients.Kind, [.. recipients.Users.Select(static user => user.Value)]);

    public SubscriptionRecipients ToRecipients() => Kind switch
    {
        RecipientKind.NamedUsers => SubscriptionRecipients.ForUsers([.. Users.Select(static user => new UserId(user))]),
        RecipientKind.Company => SubscriptionRecipients.Company,
        _ => SubscriptionRecipients.ScopeUsers,
    };
}
