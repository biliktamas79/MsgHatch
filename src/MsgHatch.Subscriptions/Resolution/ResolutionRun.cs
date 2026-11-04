using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.Resolution;

/// <summary>
/// One run of <see cref="SubscriptionResolver.Resolve"/>: it collects the candidates of each delivery target and channel
/// from the overridable part, the group part, and the fixed part, and then keeps one of them for each pair (D91).
/// </summary>
internal sealed class ResolutionRun
{
    private static readonly SubscriptionLevel[] _levelOrder = [SubscriptionLevel.Default, SubscriptionLevel.Company, SubscriptionLevel.User];

    private readonly IReadOnlyList<ChannelDescriptor> _channels;
    private readonly List<Subscription> _subscriptions;
    private readonly IReadOnlyList<UserProfile> _userList;
    private readonly Dictionary<UserId, UserProfile> _users;
    private readonly Dictionary<CompanyId, CompanyProfile> _companies;
    private readonly string _defaultLanguage;
    private readonly Dictionary<(DeliveryTarget, ChannelId), int> _pairIndexes = [];
    private readonly List<Pair> _pairs = [];

    public ResolutionRun(
        IReadOnlyList<ChannelDescriptor> channels, IReadOnlyList<Subscription> subscriptions, IReadOnlyList<UserProfile> users,
        IReadOnlyList<CompanyProfile> companies, string defaultLanguage)
    {
        _channels = channels;
        _subscriptions = InLevelOrder(OfRegisteredChannels(subscriptions));
        _userList = users;
        _users = new Dictionary<UserId, UserProfile>(users.Count);
        foreach (var user in users)
        {
            _users.TryAdd(user.Id, user);
        }

        _companies = new Dictionary<CompanyId, CompanyProfile>(companies.Count);
        foreach (var company in companies)
        {
            _companies.TryAdd(company.Id, company);
        }

        _defaultLanguage = defaultLanguage;
    }

    public SubscriptionResolution Run(NotificationScope scope)
    {
        var scopeUsers = ScopeUsersOf(scope);
        foreach (var user in scopeUsers)
        {
            AddOverridablePart(user, groupChannelsGoToTheCompany: scope.Company is not null);
        }

        if (scope.Company is { } scopeCompany)
        {
            AddCompanyGroupPart(scopeCompany);
        }

        foreach (var company in CompaniesConcernedBy(scope, scopeUsers))
        {
            AddFixedPartOfCompany(company);
        }

        AddDefaultNamedUsersPart();
        return Merge();
    }

    public List<Subscription> AppliedToCompany(CompanyId company)
    {
        var applied = new List<Subscription>();
        foreach (var channel in _channels)
        {
            var (subscription, _) = ResolveOverridable(company, user: null, channel.Id);
            if (subscription is not null && !applied.Contains(subscription))
            {
                applied.Add(subscription);
            }
        }

        foreach (var subscription in _subscriptions)
        {
            if (IsFixedForCompany(subscription, company) && !applied.Contains(subscription))
            {
                applied.Add(subscription);
            }
        }

        return applied;
    }

    public List<Subscription> AppliedDefaults() => _subscriptions.FindAll(static subscription => subscription.Owner.Level == SubscriptionLevel.Default);

    private static bool IsFixedForCompany(Subscription subscription, CompanyId company) =>
        subscription.Kind == SubscriptionKind.Channel
        && (IsOwnedBy(subscription, company)
            ? subscription.Recipients.Kind != RecipientKind.ScopeUsers
            : subscription.Owner.Level == SubscriptionLevel.Default && subscription.Recipients.Kind == RecipientKind.Company);

    private List<UserProfile> ScopeUsersOf(NotificationScope scope)
    {
        var scopeUsers = new List<UserProfile>();
        if (scope.Company is { } company)
        {
            foreach (var user in _userList)
            {
                if (user.Company == company)
                {
                    scopeUsers.Add(user);
                }
            }

            return scopeUsers;
        }

        var seen = new HashSet<UserId>();
        foreach (var id in scope.Users.Span)
        {
            if (seen.Add(id) && _users.TryGetValue(id, out var user))
            {
                scopeUsers.Add(user);
            }
        }

        return scopeUsers;
    }

    private static List<CompanyId> CompaniesConcernedBy(NotificationScope scope, List<UserProfile> scopeUsers)
    {
        if (scope.Company is { } company)
        {
            return [company];
        }

        var companies = new List<CompanyId>();
        foreach (var user in scopeUsers)
        {
            if (user.Company is { } userCompany && !companies.Contains(userCompany))
            {
                companies.Add(userCompany);
            }
        }

        return companies;
    }

    private void AddOverridablePart(UserProfile user, bool groupChannelsGoToTheCompany)
    {
        foreach (var channel in _channels)
        {
            if (groupChannelsGoToTheCompany && HasGroupDelivery(channel))
            {
                continue;
            }

            var (applied, skipped) = ResolveOverridable(user.Company, user.Id, channel.Id);
            if (applied is not null)
            {
                Add(UserTarget(user.Id), channel, user, company: null, new Candidate(applied, TargetSource.ScopeUser, skipped));
            }
        }
    }

    private void AddCompanyGroupPart(CompanyId company)
    {
        foreach (var channel in _channels)
        {
            if (!HasGroupDelivery(channel))
            {
                continue;
            }

            var (applied, skipped) = ResolveOverridable(company, user: null, channel.Id);
            if (applied is not null)
            {
                Add(CompanyTarget(company), channel, user: null, company, new Candidate(applied, TargetSource.CompanyGroup, skipped));
            }
        }
    }

    private void AddFixedPartOfCompany(CompanyId company)
    {
        foreach (var subscription in _subscriptions)
        {
            if (subscription.Kind != SubscriptionKind.Channel)
            {
                continue;
            }

            var channel = ChannelOf(subscription);
            if (subscription.Recipients.Kind == RecipientKind.Company && IsDefaultOrOwnedBy(subscription, company))
            {
                Add(CompanyTarget(company), channel, user: null, company, new Candidate(subscription, TargetSource.Company, Skipped: null));
            }
            else if (subscription.Recipients.Kind == RecipientKind.NamedUsers && IsOwnedBy(subscription, company))
            {
                AddNamedUsers(subscription, channel, company);
            }
        }
    }

    private void AddDefaultNamedUsersPart()
    {
        foreach (var subscription in _subscriptions)
        {
            if (subscription is { Kind: SubscriptionKind.Channel, Owner.Level: SubscriptionLevel.Default, Recipients.Kind: RecipientKind.NamedUsers })
            {
                AddNamedUsers(subscription, ChannelOf(subscription), companyOfTheUsers: null);
            }
        }
    }

    private void AddNamedUsers(Subscription subscription, ChannelDescriptor channel, CompanyId? companyOfTheUsers)
    {
        foreach (var id in subscription.Recipients.Users)
        {
            if (_users.TryGetValue(id, out var user) && (companyOfTheUsers is null || user.Company == companyOfTheUsers))
            {
                Add(UserTarget(id), channel, user, company: null, new Candidate(subscription, TargetSource.NamedUser, Skipped: null));
            }
        }
    }

    private (Subscription? Applied, List<SkippedSubscription>? Skipped) ResolveOverridable(
        CompanyId? company, UserId? user, ChannelId channel)
    {
        List<Subscription>? matching = null;
        Subscription? mandatoryDefault = null, mandatoryCompany = null, ofDefault = null, ofCompany = null, ofUser = null;
        foreach (var subscription in _subscriptions)
        {
            if (!IsForTheScopeUsers(subscription, company, user, channel))
            {
                continue;
            }

            (matching ??= []).Add(subscription);
            switch (subscription.Owner.Level)
            {
                case SubscriptionLevel.Default:
                    mandatoryDefault ??= subscription.Mandatory ? subscription : null;
                    ofDefault ??= subscription;
                    break;
                case SubscriptionLevel.Company:
                    mandatoryCompany ??= subscription.Mandatory ? subscription : null;
                    ofCompany ??= subscription;
                    break;
                default:
                    ofUser ??= subscription;
                    break;
            }
        }

        if (matching is null)
        {
            return (null, null);
        }

        var applied = mandatoryDefault ?? mandatoryCompany ?? ofUser ?? ofCompany ?? ofDefault!;
        List<SkippedSubscription>? skipped = null;
        foreach (var subscription in matching)
        {
            if (!ReferenceEquals(subscription, applied))
            {
                (skipped ??= []).Add(new SkippedSubscription(SourceOf(subscription), ReasonOfOverridable(subscription, applied)));
            }
        }

        return (applied, skipped);
    }

    private static SkipReason ReasonOfOverridable(Subscription skipped, Subscription applied) =>
        applied.Mandatory && skipped.Owner.Level > applied.Owner.Level ? SkipReason.Locked : SkipReason.Overridden;

    private static bool IsForTheScopeUsers(Subscription subscription, CompanyId? company, UserId? user, ChannelId channel) =>
        subscription.Recipients.Kind == RecipientKind.ScopeUsers
        && (subscription.Channel == channel || subscription is { Kind: SubscriptionKind.OptOut, Channel: null })
        && subscription.Owner.Level switch
        {
            SubscriptionLevel.Default => true,
            SubscriptionLevel.Company => company is not null && subscription.Owner.Company == company,
            _ => user is not null && subscription.Owner.User == user,
        };

    private static bool IsOwnedBy(Subscription subscription, CompanyId company) =>
        subscription.Owner.Level == SubscriptionLevel.Company && subscription.Owner.Company == company;

    private static bool IsDefaultOrOwnedBy(Subscription subscription, CompanyId company) =>
        subscription.Owner.Level == SubscriptionLevel.Default || IsOwnedBy(subscription, company);

    private void Add(DeliveryTarget target, ChannelDescriptor channel, UserProfile? user, CompanyId? company, Candidate candidate)
    {
        if (!_pairIndexes.TryGetValue((target, channel.Id), out var index))
        {
            index = _pairs.Count;
            _pairIndexes.Add((target, channel.Id), index);
            _pairs.Add(new Pair(target, channel, user, company));
        }

        _pairs[index].Candidates.Add(candidate);
    }

    private SubscriptionResolution Merge()
    {
        var deliveries = new List<MessageRoute>(_pairs.Count);
        List<OptOutDecision>? optOuts = null;
        foreach (var pair in _pairs)
        {
            var winner = WinnerOf(pair.Candidates);
            var skipped = SkippedOf(pair.Candidates, winner);
            if (winner.Subscription.Kind == SubscriptionKind.OptOut)
            {
                (optOuts ??= []).Add(new OptOutDecision(pair.Target, pair.Channel.Id, SourceOf(winner.Subscription), skipped));
            }
            else
            {
                deliveries.Add(RouteOf(pair, winner, skipped));
            }
        }

        return new SubscriptionResolution(deliveries, optOuts is null ? [] : optOuts);
    }

    private static Candidate WinnerOf(List<Candidate> candidates)
    {
        var winner = candidates[0];
        for (var index = 1; index < candidates.Count; index++)
        {
            if (MergeRank(candidates[index].Subscription) < MergeRank(winner.Subscription))
            {
                winner = candidates[index];
            }
        }

        return winner;
    }

    private static int MergeRank(Subscription subscription) =>
        (subscription.IsFixed ? 0 : 100)
        + ((int)subscription.Owner.Level * 10)
        + (subscription.Recipients.Kind == RecipientKind.ScopeUsers ? 1 : 0);

    private static SkippedSubscription[] SkippedOf(List<Candidate> candidates, Candidate winner)
    {
        List<SkippedSubscription>? skipped = null;
        foreach (var candidate in candidates)
        {
            if (candidate.Skipped is not null)
            {
                (skipped ??= []).AddRange(candidate.Skipped);
            }

            if (!ReferenceEquals(candidate.Subscription, winner.Subscription))
            {
                (skipped ??= []).Add(new SkippedSubscription(SourceOf(candidate.Subscription), SkipReason.Merged));
            }
        }

        return skipped is null ? [] : InLevelOrder(skipped);
    }

    private static SkippedSubscription[] InLevelOrder(List<SkippedSubscription> skipped)
    {
        var ordered = new SkippedSubscription[skipped.Count];
        var next = 0;
        foreach (var level in _levelOrder)
        {
            foreach (var subscription in skipped)
            {
                if (subscription.Subscription.Owner.Level == level)
                {
                    ordered[next++] = subscription;
                }
            }
        }

        return ordered;
    }

    private MessageRoute RouteOf(Pair pair, Candidate winner, IReadOnlyList<SkippedSubscription> skipped)
    {
        var subscription = winner.Subscription;
        var (language, languageSource) = LanguageOf(subscription, pair);
        var (address, addressSource) = AddressOf(pair);
        var templateScope = pair.User is not null ? pair.User.Company?.Value : pair.Company?.Value;
        var trace = new ResolutionTrace(SourceOf(subscription), winner.Source, languageSource, addressSource, skipped);
        return new MessageRoute(pair.Target, pair.Channel.Id, subscription.Settings!, language, address, templateScope, trace);
    }

    private (string Language, LanguageSource Source) LanguageOf(Subscription subscription, Pair pair)
    {
        if (subscription.Settings is ILanguageSettings { Language: { } language })
        {
            return (language, LanguageSource.ChannelSettings);
        }

        if (pair.User is { UiLanguage: { } uiLanguage })
        {
            return (uiLanguage, LanguageSource.UserUiLanguage);
        }

        if (pair.Company is { } company && _companies.TryGetValue(company, out var profile) && profile.DefaultLanguage is { } defaultLanguage)
        {
            return (defaultLanguage, LanguageSource.CompanyDefaultLanguage);
        }

        return (_defaultLanguage, LanguageSource.LibraryDefault);
    }

    private (string? Address, AddressSource Source) AddressOf(Pair pair)
    {
        if (pair.User is not null)
        {
            return pair.User.Addresses.TryGetValue(pair.Channel.Id, out var userAddress)
                ? (userAddress, AddressSource.UserProfile)
                : (null, AddressSource.None);
        }

        return !HasGroupDelivery(pair.Channel)
            && _companies.TryGetValue(pair.Company!.Value, out var company)
            && company.Addresses.TryGetValue(pair.Channel.Id, out var companyAddress)
            ? (companyAddress, AddressSource.CompanyProfile)
            : (null, AddressSource.None);
    }

    private List<Subscription> OfRegisteredChannels(IReadOnlyList<Subscription> subscriptions)
    {
        var registered = new List<Subscription>(subscriptions.Count);
        foreach (var subscription in subscriptions)
        {
            if (IsForARegisteredChannel(subscription))
            {
                registered.Add(subscription);
            }
        }

        return registered;
    }

    private bool IsForARegisteredChannel(Subscription subscription) =>
        subscription.Kind == SubscriptionKind.OptOut
            ? subscription.Channel is null || IsRegistered(subscription.Channel.Value)
            : subscription is { Channel: { } channel, Settings: not null } && IsRegistered(channel);

    private bool IsRegistered(ChannelId channel)
    {
        foreach (var descriptor in _channels)
        {
            if (descriptor.Id == channel)
            {
                return true;
            }
        }

        return false;
    }

    private ChannelDescriptor ChannelOf(Subscription subscription)
    {
        foreach (var descriptor in _channels)
        {
            if (descriptor.Id == subscription.Channel)
            {
                return descriptor;
            }
        }

        throw new InvalidOperationException("A subscription of a channel that is not registered passed the channel filter.");
    }

    private static List<Subscription> InLevelOrder(List<Subscription> subscriptions)
    {
        var ordered = new List<Subscription>(subscriptions.Count);
        foreach (var level in _levelOrder)
        {
            foreach (var subscription in subscriptions)
            {
                if (subscription.Owner.Level == level)
                {
                    ordered.Add(subscription);
                }
            }
        }

        return ordered;
    }

    private static bool HasGroupDelivery(ChannelDescriptor channel) => (channel.Features & ChannelFeatures.GroupDelivery) != 0;

    private static DeliveryTarget UserTarget(UserId user) => new(DeliveryTargetKind.User, user.Value);

    private static DeliveryTarget CompanyTarget(CompanyId company) => new(DeliveryTargetKind.Company, company.Value);

    private static SubscriptionSource SourceOf(Subscription subscription) =>
        new(subscription.Id, subscription.Owner, subscription.Recipients.Kind, subscription.Mandatory);

    private readonly record struct Candidate(Subscription Subscription, TargetSource Source, List<SkippedSubscription>? Skipped);

    private sealed class Pair(DeliveryTarget target, ChannelDescriptor channel, UserProfile? user, CompanyId? company)
    {
        public DeliveryTarget Target { get; } = target;

        public ChannelDescriptor Channel { get; } = channel;

        public UserProfile? User { get; } = user;

        public CompanyId? Company { get; } = company;

        public List<Candidate> Candidates { get; } = [];
    }
}
