using System.Data.Common;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using MsgHatch.Data.EFCore;
using MsgHatch.Data.EFCore.Model;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.Data.EFCore;

/// <summary>
/// The subscription store on EF Core (section 4.5, D78, D120): one row of the table <c>MsgHatchSubscription</c> for
/// each subscription, with its fields as columns and its channel settings as JSON (D149). The store of one tenant key
/// reads and writes only the rows of its tenant. A second subscription with the same owner, message type, channel, and
/// recipient kind, or with a stored id, gives <see cref="Errors.Conflict"/>. Each operation makes its own context through
/// the pooled factory (D130).
/// </summary>
public sealed class EntityFrameworkSubscriptionStore : ISubscriptionStore
{
    private const string _default = nameof(SubscriptionLevel.Default);

    private const string _company = nameof(SubscriptionLevel.Company);

    private const string _user = nameof(SubscriptionLevel.User);

    private readonly StoreContexts<MsgHatchSubscriptionDbContext> _contexts;
    private readonly string _tenant;
    private readonly ChannelSettingsJson _settingsJson;

    /// <summary>Makes the store of one tenant key on the contexts of a factory.</summary>
    /// <param name="contexts">The pooled factory of the contexts.</param>
    /// <param name="key">The tenant key of the store.</param>
    /// <param name="settingsJson">The JSON of the channel settings.</param>
    public EntityFrameworkSubscriptionStore(
        IDbContextFactory<MsgHatchSubscriptionDbContext> contexts, TenantKey key, ChannelSettingsJson settingsJson)
    {
        ArgumentNullException.ThrowIfNull(contexts);
        ArgumentNullException.ThrowIfNull(settingsJson);
        _contexts = new StoreContexts<MsgHatchSubscriptionDbContext>(contexts);
        _tenant = key.Tenant?.Value ?? string.Empty;
        _settingsJson = settingsJson;
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<Subscription>>> FindForDeliveryAsync(
        MessageType type, IReadOnlyList<CompanyId> companies, IReadOnlyList<UserId> users, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(companies);
        ArgumentNullException.ThrowIfNull(users);
        string[] companyIds = [.. companies.Select(static company => company.Value)];
        string[] userIds = [.. users.Select(static user => user.Value)];
        return ReadAllAsync(
            rows => rows.Where(row => row.MessageType == type.Name && (row.Level == _default
                || (row.Level == _company && companyIds.Contains(row.CompanyId))
                || (row.Level == _user && userIds.Contains(row.UserId)))),
            ct);
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<Subscription>>> QueryAsync(SubscriptionQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var level = query.Level?.ToString();
        return ReadAllAsync(
            rows =>
            {
                rows = query.Type is { } type ? rows.Where(row => row.MessageType == type.Name) : rows;
                rows = query.Company is { } company ? rows.Where(row => row.CompanyId == company.Value) : rows;
                rows = query.User is { } user ? rows.Where(row => row.UserId == user.Value) : rows;
                return level is null ? rows : rows.Where(row => row.Level == level);
            },
            ct);
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<Subscription?>> GetAsync(Guid id, CancellationToken ct)
    {
        var text = id.ToString("D");
        return _contexts.RunAsync<Subscription?>(
            async context =>
                await Rows(context).FirstOrDefaultAsync(row => row.Id == text, ct) is { } row
                    ? Read(row).ThenIfSuccess(static subscription => ErrorOr<Subscription?>.ForValue(subscription))
                    : ErrorOr<Subscription?>.ForValue(null),
            ct);
    }

    /// <inheritdoc />
    public ValueTask<Error?> AddAsync(Subscription subscription, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        var row = RowOf(subscription);
        return row.IsError
            ? ValueTask.FromResult<Error?>(row.Error)
            : _contexts.RunAsync(
                async context =>
                {
                    context.Subscriptions.Add(row.Value);
                    try
                    {
                        await context.SaveChangesAsync(ct);
                        return null;
                    }
                    catch (DbUpdateException)
                    {
                        if (await IsTakenAsync(context, row.Value, ct))
                        {
                            return SameOwnerTypeChannelAndRecipients();
                        }

                        throw;
                    }
                },
                ct);
    }

    /// <inheritdoc />
    public ValueTask<Error?> UpdateAsync(Subscription subscription, int expectedRevision, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        var row = RowOf(subscription);
        return row.IsError ? ValueTask.FromResult<Error?>(row.Error) : UpdateAsync(row.Value, expectedRevision, ct);
    }

    /// <inheritdoc />
    public ValueTask<Error?> DeleteAsync(Guid id, CancellationToken ct)
    {
        var text = id.ToString("D");
        return _contexts.RunAsync(
            async context =>
            {
                await context.Subscriptions.Where(row => row.TenantId == _tenant && row.Id == text).ExecuteDeleteAsync(ct);
                return (Error?)null;
            },
            ct);
    }

    private ValueTask<Error?> UpdateAsync(SubscriptionRow changed, int expectedRevision, CancellationToken ct) =>
        _contexts.RunAsync(
            async context =>
            {
                try
                {
                    var updated = await Rows(context).Where(row => row.Id == changed.Id && row.Revision == expectedRevision).ExecuteUpdateAsync(
                        set => set
                            .SetProperty(static row => row.Revision, changed.Revision)
                            .SetProperty(static row => row.Level, changed.Level)
                            .SetProperty(static row => row.CompanyId, changed.CompanyId)
                            .SetProperty(static row => row.UserId, changed.UserId)
                            .SetProperty(static row => row.MessageType, changed.MessageType)
                            .SetProperty(static row => row.Kind, changed.Kind)
                            .SetProperty(static row => row.Channel, changed.Channel)
                            .SetProperty(static row => row.SettingsType, changed.SettingsType)
                            .SetProperty(static row => row.SettingsVersion, changed.SettingsVersion)
                            .SetProperty(static row => row.Settings, changed.Settings)
                            .SetProperty(static row => row.Recipients, changed.Recipients)
                            .SetProperty(static row => row.NamedUsers, changed.NamedUsers)
                            .SetProperty(static row => row.Mandatory, changed.Mandatory)
                            .SetProperty(static row => row.CreatedAtUtc, changed.CreatedAtUtc)
                            .SetProperty(static row => row.LastModifiedAtUtc, changed.LastModifiedAtUtc),
                        ct);
                    return updated > 0 ? null : await IsStoredAsync(context, changed.Id, ct)
                        ? new Error(Errors.Conflict, "The stored revision is not the expected one.")
                        : new Error(Errors.NotFound, "The subscription is not stored.");
                }
                catch (DbException)
                {
                    if (await IsTakenAsync(context, changed, ct))
                    {
                        return SameOwnerTypeChannelAndRecipients();
                    }

                    throw;
                }
            },
            ct);

    private IQueryable<SubscriptionRow> Rows(MsgHatchSubscriptionDbContext context) =>
        context.Subscriptions.AsNoTracking().Where(row => row.TenantId == _tenant);

    private Task<bool> IsStoredAsync(MsgHatchSubscriptionDbContext context, string id, CancellationToken ct) =>
        Rows(context).AnyAsync(row => row.Id == id, ct);

    /// <summary>Whether another row has the id of the row, or its owner, message type, channel, and recipient kind.</summary>
    private static Task<bool> IsTakenAsync(MsgHatchSubscriptionDbContext context, SubscriptionRow row, CancellationToken ct) =>
        context.Subscriptions.AsNoTracking().AnyAsync(
            stored => stored.Id == row.Id
                || (stored.TenantId == row.TenantId && stored.MessageType == row.MessageType && stored.Level == row.Level
                    && stored.CompanyId == row.CompanyId && stored.UserId == row.UserId && stored.Channel == row.Channel
                    && stored.Recipients == row.Recipients),
            ct);

    private ValueTask<ErrorOr<IReadOnlyList<Subscription>>> ReadAllAsync(
        Func<IQueryable<SubscriptionRow>, IQueryable<SubscriptionRow>> filter, CancellationToken ct) =>
        _contexts.RunAsync<IReadOnlyList<Subscription>>(
            async context =>
            {
                var rows = await filter(Rows(context)).ToListAsync(ct);
                var subscriptions = new List<Subscription>(rows.Count);
                foreach (var row in rows)
                {
                    var subscription = Read(row);
                    if (subscription.IsError)
                    {
                        return subscription.Error!;
                    }

                    subscriptions.Add(subscription.Value);
                }

                return subscriptions;
            },
            ct);

    private ErrorOr<SubscriptionRow> RowOf(Subscription subscription)
    {
        SerializedSettings? settings = null;
        if (subscription.Settings is { } value)
        {
            var serialized = _settingsJson.Serialize(subscription.Channel.GetValueOrDefault(), value);
            if (serialized.IsError)
            {
                return serialized.Error!;
            }

            settings = serialized.Value;
        }

        return new SubscriptionRow
        {
            Id = subscription.Id.ToString("D"),
            Revision = subscription.Revision,
            TenantId = _tenant,
            Level = subscription.Owner.Level.ToString(),
            CompanyId = subscription.Owner.Company?.Value ?? string.Empty,
            UserId = subscription.Owner.User?.Value ?? string.Empty,
            MessageType = subscription.Type.Name,
            Kind = subscription.Kind.ToString(),
            Channel = subscription.Channel?.Name ?? string.Empty,
            SettingsType = settings?.ContentType,
            SettingsVersion = settings?.Version,
            Settings = settings?.Json,
            Recipients = subscription.Recipients.Kind.ToString(),
            NamedUsers = subscription.Recipients.Kind == RecipientKind.NamedUsers
                ? JsonSerializer.Serialize([.. subscription.Recipients.Users.Select(static user => user.Value)], IdsJson.Default.IReadOnlyListString)
                : null,
            Mandatory = subscription.Mandatory,
            CreatedAtUtc = subscription.CreatedAtUtc,
            LastModifiedAtUtc = subscription.LastModifiedAtUtc,
        };
    }

    /// <exception cref="ArgumentException">A column has an invalid id, name, or enum value.</exception>
    /// <exception cref="FormatException">The id is not a <see cref="Guid"/>.</exception>
    /// <exception cref="JsonException">The column <c>NamedUsers</c> is not a JSON array of strings.</exception>
    private ErrorOr<Subscription> Read(SubscriptionRow row)
    {
        ChannelSettings? settings = null;
        if (row.Settings is { } json)
        {
            var read = _settingsJson.Deserialize(row.SettingsType ?? string.Empty, row.SettingsVersion ?? 0, json);
            if (read.IsError)
            {
                return read.Error!;
            }

            settings = read.Value;
        }

        return new Subscription(
            Guid.Parse(row.Id),
            row.Revision,
            OwnerOf(Enum.Parse<SubscriptionLevel>(row.Level), row.CompanyId, row.UserId),
            new MessageType(row.MessageType),
            Enum.Parse<SubscriptionKind>(row.Kind),
            row.Channel.Length > 0 ? new ChannelId(row.Channel) : null,
            settings,
            RecipientsOf(Enum.Parse<RecipientKind>(row.Recipients), row.NamedUsers),
            row.Mandatory,
            row.CreatedAtUtc,
            row.LastModifiedAtUtc);
    }

    private static SubscriptionOwner OwnerOf(SubscriptionLevel level, string company, string user) => (level, company, user) switch
    {
        (SubscriptionLevel.Default, "", "") => SubscriptionOwner.Default,
        (SubscriptionLevel.Company, { Length: > 0 }, "") => SubscriptionOwner.ForCompany(new CompanyId(company)),
        (SubscriptionLevel.User, "", { Length: > 0 }) => SubscriptionOwner.ForUser(new UserId(user)),
        _ => throw new ArgumentException("A subscription owner has the level Default, Company, or User, and only the id of its level."),
    };

    private static SubscriptionRecipients RecipientsOf(RecipientKind kind, string? namedUsers) => kind switch
    {
        RecipientKind.NamedUsers => SubscriptionRecipients.ForUsers(
            [.. JsonSerializer.Deserialize(namedUsers ?? "null", IdsJson.Default.IReadOnlyListString)!.Select(static user => new UserId(user))]),
        RecipientKind.Company => SubscriptionRecipients.Company,
        _ => SubscriptionRecipients.ScopeUsers,
    };

    private static Error SameOwnerTypeChannelAndRecipients() =>
        new(Errors.Conflict, "The owner already has a subscription for the message type, the channel, and the recipient kind.");
}
