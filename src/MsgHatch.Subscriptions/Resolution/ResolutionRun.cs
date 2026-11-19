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
    private readonly Dictionary<ChannelId, ChannelDescriptor> _channelsById;
    private readonly List<Subscription> _subscriptions;
    private readonly IReadOnlyList<UserProfile> _userList;
    private readonly Dictionary<UserId, UserProfile> _users;
    private readonly Dictionary<CompanyId, CompanyProfile> _companies;
    private readonly string _defaultLanguage;
    private readonly Dictionary<(DeliveryTarget, ChannelId), int> _pairIndexes;
    private readonly List<Pair> _pairs;

    /// <summary>
    /// Makes a run. <paramref name="scopeCapacity"/> is the number of users of the scope, as the first capacity of the pairs
    /// (D282), or zero when the run resolves no scope.
    /// </summary>
    public ResolutionRun(
        IReadOnlyList<ChannelDescriptor> channels, IReadOnlyList<Subscription> subscriptions, IReadOnlyList<UserProfile> users,
        IReadOnlyList<CompanyProfile> companies, string defaultLanguage, int scopeCapacity = 0)
    {
        _channels = channels;
        _channelsById = new Dictionary<ChannelId, ChannelDescriptor>(channels.Count);
        for (var index = 0; index < channels.Count; index++)
        {
            var channel = channels[index];
            _channelsById.TryAdd(channel.Id, channel);
        }

        _subscriptions = InLevelOrder(OfRegisteredChannels(subscriptions));
        _userList = users;
        _users = new Dictionary<UserId, UserProfile>(users.Count);
        for (var index = 0; index < users.Count; index++)
        {
            var user = users[index];
            _users.TryAdd(user.Id, user);
        }

        _companies = new Dictionary<CompanyId, CompanyProfile>(companies.Count);
        for (var index = 0; index < companies.Count; index++)
        {
            var company = companies[index];
            _companies.TryAdd(company.Id, company);
        }

        _defaultLanguage = defaultLanguage;
        _pairIndexes = new Dictionary<(DeliveryTarget, ChannelId), int>(scopeCapacity);
        _pairs = new List<Pair>(scopeCapacity);
    }

    /// <summary>The number of users of <paramref name="scope"/>, or of all loaded users for a company scope.</summary>
    public static int ScopeCapacityOf(NotificationScope scope, IReadOnlyList<UserProfile> users) =>
        scope.Company is null ? scope.Users.Length : users.Count;

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
        for (var index = 0; index < _channels.Count; index++)
        {
            var (subscription, _) = ResolveOverridable(company, user: null, _channels[index].Id);
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
        var scopeUsers = new List<UserProfile>(ScopeCapacityOf(scope, _userList));
        if (scope.Company is { } company)
        {
            for (var index = 0; index < _userList.Count; index++)
            {
                var user = _userList[index];
                if (user.Company == company)
                {
                    scopeUsers.Add(user);
                }
            }

            return scopeUsers;
        }

        var seen = new HashSet<UserId>(scope.Users.Length);
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
        for (var index = 0; index < _channels.Count; index++)
        {
            var channel = _channels[index];
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
        for (var index = 0; index < _channels.Count; index++)
        {
            var channel = _channels[index];
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
        var users = subscription.Recipients.Users;
        for (var index = 0; index < users.Count; index++)
        {
            var id = users[index];
            if (_users.TryGetValue(id, out var user) && (companyOfTheUsers is null || user.Company == companyOfTheUsers))
            {
                Add(UserTarget(id), channel, user, company: null, new Candidate(subscription, TargetSource.NamedUser, Skipped: null));
            }
        }
    }

    private (Subscription? Applied, SkippedSubscription[]? Skipped) ResolveOverridable(
        CompanyId? company, UserId? user, ChannelId channel)
    {
        var matching = 0;
        Subscription? mandatoryDefault = null, mandatoryCompany = null, ofDefault = null, ofCompany = null, ofUser = null;
        foreach (var subscription in _subscriptions)
        {
            if (!IsForTheScopeUsers(subscription, company, user, channel))
            {
                continue;
            }

            matching++;
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

        if (matching == 0)
        {
            return (null, null);
        }

        var applied = mandatoryDefault ?? mandatoryCompany ?? ofUser ?? ofCompany ?? ofDefault!;
        return (applied, matching == 1 ? null : SkippedOfOverridable(company, user, channel, applied, matching - 1));
    }

    /// <summary>
    /// The second pass over the subscriptions: each matching subscription other than <paramref name="applied"/>, in level
    /// order, in an array of the exact size (D282).
    /// </summary>
    private SkippedSubscription[] SkippedOfOverridable(CompanyId? company, UserId? user, ChannelId channel, Subscription applied, int count)
    {
        var skipped = new SkippedSubscription[count];
        var next = 0;
        foreach (var subscription in _subscriptions)
        {
            if (IsForTheScopeUsers(subscription, company, user, channel) && !ReferenceEquals(subscription, applied))
            {
                skipped[next++] = new SkippedSubscription(SourceOf(subscription), ReasonOfOverridable(subscription, applied));
            }
        }

        return next == skipped.Length ? skipped : skipped[..next];
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
        if (_pairIndexes.TryGetValue((target, channel.Id), out var index))
        {
            _pairs[index].Add(candidate);
            return;
        }

        _pairIndexes.Add((target, channel.Id), _pairs.Count);
        _pairs.Add(new Pair(target, channel, user, company, candidate));
    }

    /// <summary>Keeps one candidate for each pair. It counts the opt-outs first, so the routes and the opt-out decisions are arrays of their exact size (D282).</summary>
    private SubscriptionResolution Merge()
    {
        var optOutCount = 0;
        foreach (var pair in _pairs)
        {
            optOutCount += WinnerOf(pair).Subscription.Kind == SubscriptionKind.OptOut ? 1 : 0;
        }

        MessageRoute[] deliveries = optOutCount == _pairs.Count ? [] : new MessageRoute[_pairs.Count - optOutCount];
        OptOutDecision[] optOuts = optOutCount == 0 ? [] : new OptOutDecision[optOutCount];
        var (nextDelivery, nextOptOut) = (0, 0);
        foreach (var pair in _pairs)
        {
            var winner = WinnerOf(pair);
            var skipped = SkippedOf(pair, winner);
            if (winner.Subscription.Kind == SubscriptionKind.OptOut)
            {
                optOuts[nextOptOut++] = new OptOutDecision(pair.Target, pair.Channel.Id, SourceOf(winner.Subscription), skipped);
            }
            else
            {
                deliveries[nextDelivery++] = RouteOf(pair, winner, skipped);
            }
        }

        return new SubscriptionResolution(deliveries, optOuts);
    }

    private static Candidate WinnerOf(Pair pair)
    {
        var winner = pair.First;
        if (pair.Others is not { } others)
        {
            return winner;
        }

        foreach (var candidate in others)
        {
            if (MergeRank(candidate.Subscription) < MergeRank(winner.Subscription))
            {
                winner = candidate;
            }
        }

        return winner;
    }

    private static int MergeRank(Subscription subscription) =>
        (subscription.IsFixed ? 0 : 100)
        + ((int)subscription.Owner.Level * 10)
        + (subscription.Recipients.Kind == RecipientKind.ScopeUsers ? 1 : 0);

    /// <summary>
    /// The skipped subscriptions of a pair, in level order (D282). A pair with one candidate keeps the skipped array of
    /// that candidate, because it is already in level order. A pair with more candidates makes one array of the exact size.
    /// </summary>
    private static SkippedSubscription[] SkippedOf(Pair pair, Candidate winner)
    {
        if (pair.Others is not { } others)
        {
            return pair.First.Skipped ?? [];
        }

        var count = SkippedCountOf(pair.First, winner);
        foreach (var candidate in others)
        {
            count += SkippedCountOf(candidate, winner);
        }

        var skipped = new SkippedSubscription[count];
        var next = 0;
        foreach (var level in _levelOrder)
        {
            next = AddSkippedOfLevel(pair.First, winner, level, skipped, next);
            foreach (var candidate in others)
            {
                next = AddSkippedOfLevel(candidate, winner, level, skipped, next);
            }
        }

        return skipped;
    }

    private static int SkippedCountOf(Candidate candidate, Candidate winner) =>
        (candidate.Skipped?.Length ?? 0) + (IsMergedAway(candidate, winner) ? 1 : 0);

    private static bool IsMergedAway(Candidate candidate, Candidate winner) => !ReferenceEquals(candidate.Subscription, winner.Subscription);

    /// <summary>Writes the skipped subscriptions of one candidate on <paramref name="level"/>, and then the candidate itself when the merge removed it.</summary>
    private static int AddSkippedOfLevel(Candidate candidate, Candidate winner, SubscriptionLevel level, SkippedSubscription[] skipped, int next)
    {
        foreach (var subscription in candidate.Skipped ?? [])
        {
            if (subscription.Subscription.Owner.Level == level)
            {
                skipped[next++] = subscription;
            }
        }

        if (candidate.Subscription.Owner.Level == level && IsMergedAway(candidate, winner))
        {
            skipped[next++] = new SkippedSubscription(SourceOf(candidate.Subscription), SkipReason.Merged);
        }

        return next;
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
        for (var index = 0; index < subscriptions.Count; index++)
        {
            var subscription = subscriptions[index];
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

    private bool IsRegistered(ChannelId channel) => _channelsById.ContainsKey(channel);

    private ChannelDescriptor ChannelOf(Subscription subscription) => _channelsById[subscription.Channel!.Value];

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

    private readonly record struct Candidate(Subscription Subscription, TargetSource Source, SkippedSubscription[]? Skipped);

    /// <summary>One delivery target and channel with its candidates. It keeps the first candidate in a field, and makes a list only for a second candidate (D282).</summary>
    private sealed class Pair(DeliveryTarget target, ChannelDescriptor channel, UserProfile? user, CompanyId? company, Candidate first)
    {
        public DeliveryTarget Target { get; } = target;

        public ChannelDescriptor Channel { get; } = channel;

        public UserProfile? User { get; } = user;

        public CompanyId? Company { get; } = company;

        public Candidate First { get; } = first;

        public List<Candidate>? Others { get; private set; }

        public void Add(Candidate candidate) => (Others ??= []).Add(candidate);
    }
}
