using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Subscriptions.Resolution;

namespace MsgHatch.Subscriptions;

/// <summary>
/// The router of the message types with no router of their own (D97, D99, D147). It runs the routing procedure of
/// section 4.17 with the store and the directory of its tenant key (D116, D126): the scope, the directory, one store
/// call, the profiles of the fixed subscriptions, and the resolution.
/// </summary>
/// <typeparam name="TEvent">The type of the domain event.</typeparam>
internal sealed class SubscriptionRouter<TEvent> : IMessageRouter<TEvent>
{
    private readonly SubscriptionMessage<TEvent> _message;
    private readonly ISubscriptionStore _store;
    private readonly IRecipientDirectory _directory;
    private readonly IMessageRegistry _registry;
    private readonly string _defaultLanguage;
    private readonly ILogger _logger;

    public SubscriptionRouter(
        ISubscriptionStore store,
        IRecipientDirectory directory,
        IMessageRegistry registry,
        IOptions<MsgHatchOptions> options,
        SubscriptionMessage<TEvent>? message = null,
        ILogger<SubscriptionRouter<TEvent>>? logger = null)
    {
        _message = message ?? throw new InvalidOperationException("The message type has no scope function. Register one with WithScope.");
        _store = store;
        _directory = directory;
        _registry = registry;
        _defaultLanguage = options.Value.DefaultLanguage;
        _logger = logger ?? NullLogger<SubscriptionRouter<TEvent>>.Instance;
    }

    public async ValueTask<ErrorOr<IReadOnlyList<MessageRoute>>> RouteAsync(ReceivedMessage<TEvent> message, CancellationToken ct)
    {
        var channels = MessageChannels.Of(_registry, message.Type);
        var scope = _message.Scope(message.Event);
        var profiles = new LoadedProfiles();
        var error = await LoadScopeAsync(scope, channels, profiles, ct);
        if (error is not null)
        {
            return error;
        }

        var companies = profiles.Companies(scope);
        var subscriptions = await _store.FindForDeliveryAsync(message.Type, companies, profiles.ScopeUsers, ct);
        if (subscriptions.IsError)
        {
            return subscriptions.Error;
        }

        error = await LoadFixedProfilesAsync(scope, companies, subscriptions.Value, channels, profiles, ct);
        if (error is not null)
        {
            return error;
        }

        var resolution = SubscriptionResolver.Resolve(
            channels, scope, subscriptions.Value, profiles.Users, profiles.CompanyProfiles, _defaultLanguage);
        LogOptOuts(message, resolution.OptOuts);
        return ErrorOr<IReadOnlyList<MessageRoute>>.ForValue(resolution.Deliveries);
    }

    private async ValueTask<Error?> LoadScopeAsync(
        NotificationScope scope, List<ChannelDescriptor> channels, LoadedProfiles profiles, CancellationToken ct)
    {
        if (scope.Company is not { } company)
        {
            profiles.ScopeUsers = Distinct(scope.Users.Span);
            return await profiles.AddUsersAsync(_directory.GetUsersAsync(profiles.ScopeUsers, ct));
        }

        var error = await profiles.AddCompanyAsync(_directory.GetCompanyAsync(company, ct));
        if (error is not null || !channels.Exists(static channel => (channel.Features & ChannelFeatures.GroupDelivery) == 0))
        {
            return error;
        }

        error = await profiles.AddUsersAsync(_directory.GetCompanyUsersAsync(company, ct));
        profiles.ScopeUsers = profiles.UserIds();
        return error;
    }

    private async ValueTask<Error?> LoadFixedProfilesAsync(
        NotificationScope scope, List<CompanyId> companies, IReadOnlyList<Subscription> subscriptions, List<ChannelDescriptor> channels,
        LoadedProfiles profiles, CancellationToken ct)
    {
        if (scope.Company is null)
        {
            foreach (var company in companies)
            {
                if (HasSubscriptionForTheCompany(subscriptions, channels, company))
                {
                    var error = await profiles.AddCompanyAsync(_directory.GetCompanyAsync(company, ct));
                    if (error is not null)
                    {
                        return error;
                    }
                }
            }
        }

        var namedUsers = NamedUsersToLoad(subscriptions, channels, companies, profiles);
        return namedUsers.Length == 0 ? null : await profiles.AddUsersAsync(_directory.GetUsersAsync(namedUsers, ct));
    }

    private static bool HasSubscriptionForTheCompany(IReadOnlyList<Subscription> subscriptions, List<ChannelDescriptor> channels, CompanyId company)
    {
        foreach (var subscription in subscriptions)
        {
            if (subscription.Recipients.Kind == RecipientKind.Company
                && IsDeliveredOn(subscription, channels)
                && (subscription.Owner.Level == SubscriptionLevel.Default || subscription.Owner.Company == company))
            {
                return true;
            }
        }

        return false;
    }

    private static UserId[] NamedUsersToLoad(
        IReadOnlyList<Subscription> subscriptions, List<ChannelDescriptor> channels, List<CompanyId> companies, LoadedProfiles profiles)
    {
        var toLoad = new List<UserId>();
        foreach (var subscription in subscriptions)
        {
            if (subscription.Recipients.Kind != RecipientKind.NamedUsers
                || !IsDeliveredOn(subscription, channels)
                || !(subscription.Owner.Level == SubscriptionLevel.Default
                    || (subscription.Owner.Company is { } owner && companies.Contains(owner))))
            {
                continue;
            }

            foreach (var user in subscription.Recipients.Users)
            {
                if (!profiles.WasAskedFor(user) && !toLoad.Contains(user))
                {
                    toLoad.Add(user);
                }
            }
        }

        return [.. toLoad];
    }

    private static bool IsDeliveredOn(Subscription subscription, List<ChannelDescriptor> channels) =>
        subscription.Kind == SubscriptionKind.Channel && channels.Exists(channel => channel.Id == subscription.Channel);

    private static UserId[] Distinct(ReadOnlySpan<UserId> users)
    {
        var distinct = new List<UserId>(users.Length);
        foreach (var user in users)
        {
            if (!distinct.Contains(user))
            {
                distinct.Add(user);
            }
        }

        return [.. distinct];
    }

    private void LogOptOuts(ReceivedMessage<TEvent> message, IReadOnlyList<OptOutDecision> optOuts)
    {
        if (!_logger.IsEnabled(LogLevel.Debug))
        {
            return;
        }

        foreach (var decision in optOuts)
        {
            Log.OptOutApplied(
                _logger, message.Delivery.Id, message.Type.Name, decision.Channel.Name, decision.OptOut.Id, decision.OptOut.Owner.Level,
                message.Tenant?.Value, message.Flags);
        }
    }

    /// <summary>The profiles that the router loaded for one event. The directory was asked for each scope user.</summary>
    private sealed class LoadedProfiles
    {
        public UserId[] ScopeUsers { get; set; } = [];

        public List<UserProfile> Users { get; } = [];

        public List<CompanyProfile> CompanyProfiles { get; } = [];

        public bool WasAskedFor(UserId user) => ScopeUsers.Contains(user);

        public UserId[] UserIds() => [.. Users.ConvertAll(static user => user.Id)];

        public List<CompanyId> Companies(NotificationScope scope)
        {
            if (scope.Company is { } company)
            {
                return [company];
            }

            var companies = new List<CompanyId>();
            foreach (var user in Users)
            {
                if (user.Company is { } userCompany && ScopeUsers.Contains(user.Id) && !companies.Contains(userCompany))
                {
                    companies.Add(userCompany);
                }
            }

            return companies;
        }

        public async ValueTask<Error?> AddUsersAsync(ValueTask<ErrorOr<IReadOnlyList<UserProfile>>> load)
        {
            var users = await load;
            if (users.IsError)
            {
                return users.Error;
            }

            foreach (var user in users.Value)
            {
                Users.Add(user);
            }

            return null;
        }

        public async ValueTask<Error?> AddCompanyAsync(ValueTask<ErrorOr<CompanyProfile?>> load)
        {
            var company = await load;
            if (company.IsError)
            {
                return company.Error;
            }

            if (company.Value is not null)
            {
                CompanyProfiles.Add(company.Value);
            }

            return null;
        }
    }
}
