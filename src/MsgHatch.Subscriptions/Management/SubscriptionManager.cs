using System.Globalization;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Subscriptions.Resolution;

namespace MsgHatch.Subscriptions.Management;

/// <summary>
/// The subscription management API on <see cref="ISubscriptionStore"/> (D53, D78, section 4.17). A create or an update
/// validates the draft first, and checks the conflicts only for a valid draft, so an invalid draft never gives
/// <see cref="Errors.Conflict"/>.
/// </summary>
internal sealed class SubscriptionManager(
    ISubscriptionStore store,
    IRecipientDirectory directory,
    IMessageRegistry registry,
    IEnumerable<IChannelSettingsValidator> validators,
    IEnumerable<ISubscriptionMessage> scopedMessages,
    TimeProvider time,
    IOptions<MsgHatchOptions> options)
    : ISubscriptionManager
{
    private const int _maxNamedUsers = 100;

    private readonly HashSet<Type> _scopedEventTypes = [.. scopedMessages.Select(static message => message.EventType)];

    public ValueTask<ErrorOr<IReadOnlyList<ChannelDescriptor>>> GetChannelsAsync(CancellationToken ct) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<ChannelDescriptor>>.ForValue(registry.Channels));

    public async ValueTask<ErrorOr<IReadOnlyList<MessageTypeState>>> GetMessageTypesAsync(MessageTypeQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        UserProfile? user = null;
        if (query.User is { } userId)
        {
            var profile = await ProfileOfAsync(userId, ct);
            if (profile.IsError || profile.Value is null)
            {
                return profile.Error ?? new Error(Errors.NotFound, "The directory does not know the user.");
            }

            user = profile.Value;
        }

        var states = new List<MessageTypeState>();
        foreach (var type in RoutedMessageTypes(query.Type))
        {
            var company = user is null ? query.Company : user.Company;
            var subscriptions = await store.FindForDeliveryAsync(
                type.Type, company is { } c ? [c] : [], user is null ? [] : [user.Id], ct);
            if (subscriptions.IsError)
            {
                return subscriptions.Error;
            }

            states.Add(new MessageTypeState(type.Type, type.Channels, Applied(type.Type, subscriptions.Value, user, company)));
        }

        return states;
    }

    public ValueTask<ErrorOr<IReadOnlyList<Subscription>>> GetSubscriptionsAsync(SubscriptionQuery query, CancellationToken ct) =>
        store.QueryAsync(query, ct);

    public async ValueTask<ErrorOr<Subscription>> GetSubscriptionAsync(Guid id, CancellationToken ct)
    {
        var stored = await store.GetAsync(id, ct);
        return stored.IsError ? stored.Error : stored.Value ?? (ErrorOr<Subscription>)NotFound();
    }

    public async ValueTask<ErrorOr<Subscription>> CreateAsync(SubscriptionDraft draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var recipients = draft.Recipients ?? SubscriptionRecipients.ScopeUsers;
        var error = await ValidationOrConflictAsync(draft, recipients, stored: null, ct);
        if (error is not null)
        {
            return error;
        }

        var now = time.GetUtcNow();
        var subscription = new Subscription(
            Guid.CreateVersion7(now), 1, draft.Owner, draft.Type, draft.Kind, draft.Channel, draft.Settings, recipients, draft.Mandatory,
            now.UtcDateTime, now.UtcDateTime);
        error = await store.AddAsync(subscription, ct);
        return error is null ? subscription : error;
    }

    public async ValueTask<ErrorOr<Subscription>> UpdateAsync(Guid id, int expectedRevision, SubscriptionDraft draft, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(draft);
        var stored = await store.GetAsync(id, ct);
        if (stored.IsError || stored.Value is null)
        {
            return stored.Error ?? NotFound();
        }

        var recipients = draft.Recipients ?? SubscriptionRecipients.ScopeUsers;
        var error = await ValidationOrConflictAsync(draft, recipients, stored.Value, ct);
        if (error is not null)
        {
            return error;
        }

        var updated = stored.Value with
        {
            Revision = stored.Value.Revision + 1,
            Kind = draft.Kind,
            Channel = draft.Channel,
            Settings = draft.Settings,
            Recipients = recipients,
            Mandatory = draft.Mandatory,
            LastModifiedAtUtc = time.GetUtcNow().UtcDateTime,
        };
        error = await store.UpdateAsync(updated, expectedRevision, ct);
        return error is null ? updated : error;
    }

    public ValueTask<Error?> DeleteAsync(Guid id, CancellationToken ct) => store.DeleteAsync(id, ct);

    private List<Subscription> Applied(MessageType type, IReadOnlyList<Subscription> subscriptions, UserProfile? user, CompanyId? company)
    {
        var channels = MessageChannels.Of(registry, type);
        return user is not null ? AppliedToUser(channels, subscriptions, user)
            : company is { } c ? SubscriptionResolver.AppliedToCompany(channels, subscriptions, c)
            : SubscriptionResolver.AppliedDefaults(channels, subscriptions);
    }

    private List<Subscription> AppliedToUser(List<ChannelDescriptor> channels, IReadOnlyList<Subscription> subscriptions, UserProfile user)
    {
        var resolution = SubscriptionResolver.Resolve(
            channels, NotificationScope.ForUsers(new[] { user.Id }), subscriptions, [user], [], options.Value.DefaultLanguage);
        var target = new DeliveryTarget(DeliveryTargetKind.User, user.Id.Value);
        var appliedIds = resolution.Deliveries
            .Where(route => route.Target == target)
            .Select(static route => ((ResolutionTrace)route.Trace!).Applied.Id)
            .Concat(resolution.OptOuts.Where(decision => decision.Target == target).Select(static decision => decision.OptOut.Id))
            .Distinct();
        return [.. appliedIds.Select(id => subscriptions.First(subscription => subscription.Id == id))];
    }

    private IEnumerable<MessageTypeDescriptor> RoutedMessageTypes(MessageType? only) =>
        registry.MessageTypes.Where(type => _scopedEventTypes.Contains(type.EventType) && (only is null || type.Type == only));

    private async ValueTask<ErrorOr<UserProfile?>> ProfileOfAsync(UserId user, CancellationToken ct)
    {
        var profiles = await directory.GetUsersAsync([user], ct);
        return profiles.IsError ? profiles.Error : profiles.Value.FirstOrDefault(profile => profile.Id == user);
    }

    private async ValueTask<Error?> ValidationOrConflictAsync(
        SubscriptionDraft draft, SubscriptionRecipients recipients, Subscription? stored, CancellationToken ct) =>
        ValidationError(draft, recipients, stored)
        ?? await NamedUsersErrorAsync(draft, recipients, ct)
        ?? await ConflictAsync(draft, recipients, stored?.Id, ct);

    private Error? ValidationError(SubscriptionDraft draft, SubscriptionRecipients recipients, Subscription? stored)
    {
        var type = RoutedMessageTypes(draft.Type).FirstOrDefault();
        return !OwnerMatchesItsLevel(draft.Owner) ? Invalid("The owner does not match its level.")
            : type is null ? Invalid("The message type is not registered, or has no scope function.")
            : stored is not null && ChangesTheIdentity(stored, draft, recipients) ? Invalid("An update keeps the owner, the message type, and the recipient kind.")
            : draft.Kind == SubscriptionKind.OptOut ? OptOutError(draft, recipients, type)
            : ChannelSubscriptionError(draft, recipients, type);
    }

    private static bool OwnerMatchesItsLevel(SubscriptionOwner owner) => owner.Level switch
    {
        SubscriptionLevel.Default => owner is { Company: null, User: null },
        SubscriptionLevel.Company => owner is { Company.Value: not null, User: null },
        _ => owner is { User.Value: not null, Company: null },
    };

    private static bool ChangesTheIdentity(Subscription stored, SubscriptionDraft draft, SubscriptionRecipients recipients) =>
        stored.Owner != draft.Owner || stored.Type != draft.Type || stored.Recipients.Kind != recipients.Kind;

    private static Error? OptOutError(SubscriptionDraft draft, SubscriptionRecipients recipients, MessageTypeDescriptor type) =>
        draft.Settings is not null ? Invalid("An opt-out has no settings.")
        : recipients.Kind != RecipientKind.ScopeUsers || draft.Mandatory ? Invalid("An opt-out is for the scope users, and is not mandatory.")
        : draft.Channel is { } channel && !type.Channels.Contains(channel) ? Invalid("The message type has not registered the channel.")
        : null;

    private Error? ChannelSubscriptionError(SubscriptionDraft draft, SubscriptionRecipients recipients, MessageTypeDescriptor type)
    {
        if (draft.Channel is not { } channelId || draft.Settings is null)
        {
            return Invalid("A channel subscription has a channel and settings.");
        }

        var channel = type.Channels.Contains(channelId) ? registry.FindChannel(channelId) : null;
        return channel is null ? Invalid("The message type has not registered the channel.")
            : draft.Settings.GetType() != channel.SettingsType ? Invalid("The settings type is not the settings type of the channel.")
            : SettingsLanguageError(draft.Settings, channel)
            ?? SettingsValidatorError(draft.Settings, channel)
            ?? RecipientsError(draft, recipients);
    }

    private static Error? SettingsLanguageError(ChannelSettings settings, ChannelDescriptor channel) =>
        settings is not ILanguageSettings { Language: { } language } ? null
        : (channel.Features & ChannelFeatures.Languages) == 0 ? Invalid("The channel has no feature Languages.")
        : !IsKnownCulture(language) ? Invalid("The language is not a known culture.")
        : null;

    private Error? SettingsValidatorError(ChannelSettings settings, ChannelDescriptor channel) =>
        validators.FirstOrDefault(validator => validator.Channel == channel.Id)?.Validate(settings, channel.Features) is { } error
            ? Invalid($"The settings validator of the channel rejects the settings: {error.Type.Name}.")
            : null;

    private static Error? RecipientsError(SubscriptionDraft draft, SubscriptionRecipients recipients) =>
        draft.Owner.Level == SubscriptionLevel.User && recipients.Kind != RecipientKind.ScopeUsers
            ? Invalid("A user subscription is for the scope users.")
        : draft.Mandatory && (draft.Owner.Level == SubscriptionLevel.User || recipients.Kind != RecipientKind.ScopeUsers)
            ? Invalid("A mandatory subscription is a default or company subscription for the scope users.")
        : recipients.Kind == RecipientKind.NamedUsers && !HasOneToHundredDifferentUsers(recipients.Users)
            ? Invalid("Named users are 1 to 100 different users.")
        : null;

    private static bool HasOneToHundredDifferentUsers(IReadOnlyList<UserId> users) =>
        users.Count is > 0 and <= _maxNamedUsers && users.Distinct().Count() == users.Count;

    private async ValueTask<Error?> NamedUsersErrorAsync(SubscriptionDraft draft, SubscriptionRecipients recipients, CancellationToken ct)
    {
        if (draft.Kind != SubscriptionKind.Channel || recipients.Kind != RecipientKind.NamedUsers)
        {
            return null;
        }

        var profiles = await directory.GetUsersAsync(recipients.Users, ct);
        if (profiles.IsError)
        {
            return profiles.Error;
        }

        return recipients.Users.Any(user => !profiles.Value.Any(profile => profile.Id == user)) ? Invalid("The directory does not know a named user.")
            : draft.Owner.Company is { } company && profiles.Value.Any(profile => profile.Company != company)
                ? Invalid("A named user of a company subscription belongs to another company.")
            : null;
    }

    private async ValueTask<Error?> ConflictAsync(
        SubscriptionDraft draft, SubscriptionRecipients recipients, Guid? updated, CancellationToken ct)
    {
        CompanyId? companyOfTheUser = null;
        if (draft.Owner.User is { } user)
        {
            var profile = await ProfileOfAsync(user, ct);
            if (profile.IsError)
            {
                return profile.Error;
            }

            companyOfTheUser = profile.Value?.Company;
        }

        var company = draft.Owner.Company ?? companyOfTheUser;
        var existing = await store.FindForDeliveryAsync(
            draft.Type, company is { } c ? [c] : [], draft.Owner.User is { } u ? [u] : [], ct);
        if (existing.IsError)
        {
            return existing.Error;
        }

        foreach (var subscription in existing.Value)
        {
            if (subscription.Id != updated && ConflictsWith(subscription, draft, recipients, companyOfTheUser))
            {
                return new Error(Errors.Conflict, "The change conflicts with a stored subscription.");
            }
        }

        return null;
    }

    private static bool ConflictsWith(Subscription stored, SubscriptionDraft draft, SubscriptionRecipients recipients, CompanyId? companyOfTheUser) =>
        (stored.Owner == draft.Owner && IsTheSameSubscriptionOfTheOwner(stored, draft, recipients))
        || IsLockedByAMandatorySubscription(stored, draft, recipients, companyOfTheUser);

    private static bool IsTheSameSubscriptionOfTheOwner(Subscription stored, SubscriptionDraft draft, SubscriptionRecipients recipients) =>
        (stored.Channel == draft.Channel && stored.Recipients.Kind == recipients.Kind)
        || (IsOptOutForAllChannels(draft.Kind, draft.Channel) && stored.Recipients.Kind == RecipientKind.ScopeUsers)
        || (recipients.Kind == RecipientKind.ScopeUsers && IsOptOutForAllChannels(stored.Kind, stored.Channel));

    private static bool IsLockedByAMandatorySubscription(
        Subscription stored, SubscriptionDraft draft, SubscriptionRecipients recipients, CompanyId? companyOfTheUser) =>
        draft.Owner.Level != SubscriptionLevel.Default
        && draft.Channel is not null
        && (draft.Kind == SubscriptionKind.OptOut || recipients.Kind == RecipientKind.ScopeUsers)
        && stored.Mandatory
        && stored.Channel == draft.Channel
        && (stored.Owner.Level == SubscriptionLevel.Default
            || (draft.Owner.Level == SubscriptionLevel.User && stored.Owner.Company is { } owner && owner == companyOfTheUser));

    private static bool IsOptOutForAllChannels(SubscriptionKind kind, ChannelId? channel) => kind == SubscriptionKind.OptOut && channel is null;

    private static bool IsKnownCulture(string name)
    {
        try
        {
            _ = CultureInfo.GetCultureInfo(name, predefinedOnly: true);
            return true;
        }
        catch (CultureNotFoundException)
        {
            return false;
        }
    }

    private static Error Invalid(string message) => new(Errors.ValidationFailed, message);

    private static Error NotFound() => new(Errors.NotFound, "The subscription is not stored.");
}
