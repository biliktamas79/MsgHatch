using System.Text.Json;
using Microsoft.Data.Sqlite;
using MsgHatch.Data.Sqlite;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.Data.Sqlite;

/// <summary>
/// The subscription store in SQLite (section 4.5, D78, D97): one row of the table <c>MsgHatchSubscription</c> for each
/// subscription, with its fields as columns and its channel settings as JSON (D149). The store of one tenant key reads
/// and writes only the rows of its tenant. A unique index stops a second subscription with the same owner, message type,
/// channel, and recipient kind, and the store writes an empty text for a missing tenant, company, user, or channel,
/// because SQLite treats each NULL as a different value in a unique index. The store keeps its schema version in its own
/// table, so it can share a database file with the core SQLite stores. Each operation opens its own pooled connection.
/// It is thread-safe.
/// </summary>
public sealed class SqliteSubscriptionStore : ISubscriptionStore
{
    private const string _columns = """
        Id, Revision, TenantId, Level, CompanyId, UserId, MessageType, Kind, Channel, SettingsType, SettingsVersion, Settings,
        Recipients, NamedUsers, Mandatory, CreatedAtUtc, LastModifiedAtUtc
        """;

    private const string _select = $"SELECT {_columns} FROM MsgHatchSubscription WHERE TenantId = @tenantId";

    private const string _insert = $"""
        INSERT OR IGNORE INTO MsgHatchSubscription ({_columns})
        VALUES (
            @id, @revision, @tenantId, @level, @companyId, @userId, @messageType, @kind, @channel, @settingsType, @settingsVersion,
            @settings, @recipients, @namedUsers, @mandatory, @createdAtUtc, @lastModifiedAtUtc)
        """;

    private const string _update = """
        UPDATE OR IGNORE MsgHatchSubscription SET
            Revision = @revision, Level = @level, CompanyId = @companyId, UserId = @userId, MessageType = @messageType,
            Kind = @kind, Channel = @channel, SettingsType = @settingsType, SettingsVersion = @settingsVersion,
            Settings = @settings, Recipients = @recipients, NamedUsers = @namedUsers, Mandatory = @mandatory,
            CreatedAtUtc = @createdAtUtc, LastModifiedAtUtc = @lastModifiedAtUtc
        WHERE TenantId = @tenantId AND Id = @id AND Revision = @expectedRevision
        """;

    private const string _findForDelivery = $"""
        {_select} AND MessageType = @messageType AND (
            Level = 'Default'
            OR (Level = 'Company' AND CompanyId IN (SELECT value FROM json_each(@companies)))
            OR (Level = 'User' AND UserId IN (SELECT value FROM json_each(@users))))
        """;

    private const string _query = $"""
        {_select}
            AND (@messageType IS NULL OR MessageType = @messageType)
            AND (@companyId IS NULL OR CompanyId = @companyId)
            AND (@userId IS NULL OR UserId = @userId)
            AND (@level IS NULL OR Level = @level)
        """;

    private const string _exists = "SELECT 1 FROM MsgHatchSubscription WHERE TenantId = @tenantId AND Id = @id";

    private const string _delete = "DELETE FROM MsgHatchSubscription WHERE TenantId = @tenantId AND Id = @id";

    private static readonly SqliteSchema _schema = new("""
        PRAGMA journal_mode = WAL;
        CREATE TABLE IF NOT EXISTS MsgHatchSubscriptionSchema (Version INTEGER);
        INSERT INTO MsgHatchSubscriptionSchema (Version) SELECT 1 WHERE NOT EXISTS (SELECT 1 FROM MsgHatchSubscriptionSchema);
        CREATE TABLE IF NOT EXISTS MsgHatchSubscription (
            Id TEXT PRIMARY KEY, Revision INTEGER, TenantId TEXT, Level TEXT, CompanyId TEXT, UserId TEXT, MessageType TEXT,
            Kind TEXT, Channel TEXT, SettingsType TEXT, SettingsVersion INTEGER, Settings TEXT, Recipients TEXT,
            NamedUsers TEXT, Mandatory INTEGER, CreatedAtUtc TEXT, LastModifiedAtUtc TEXT);
        CREATE UNIQUE INDEX IF NOT EXISTS UX_MsgHatchSubscription_Owner
            ON MsgHatchSubscription (TenantId, MessageType, Level, CompanyId, UserId, Channel, Recipients);
        """);

    private readonly string _tenant;
    private readonly ChannelSettingsJson _settingsJson;

    /// <summary>Makes the store of one tenant key on a database.</summary>
    /// <param name="connectionString">The connection string of the database, for example <c>Data Source=msghatch.db</c>.</param>
    /// <param name="key">The tenant key of the store.</param>
    /// <param name="settingsJson">The JSON of the channel settings.</param>
    /// <exception cref="ArgumentException">The connection string is not a valid SQLite connection string.</exception>
    public SqliteSubscriptionStore(string connectionString, TenantKey key, ChannelSettingsJson settingsJson)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        ArgumentNullException.ThrowIfNull(settingsJson);
        _ = new SqliteConnectionStringBuilder(connectionString);
        ConnectionString = connectionString;
        _tenant = SqliteValues.Tenant(key.Tenant);
        _settingsJson = settingsJson;
    }

    /// <summary>The connection string of the database.</summary>
    public string ConnectionString { get; }

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<Subscription>>> FindForDeliveryAsync(
        MessageType type, IReadOnlyList<CompanyId> companies, IReadOnlyList<UserId> users, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(companies);
        ArgumentNullException.ThrowIfNull(users);
        return ReadAll(_findForDelivery, command => command
            .With("@messageType", type.Name)
            .With("@companies", IdsOf(companies.Select(static company => company.Value)))
            .With("@users", IdsOf(users.Select(static user => user.Value))));
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<IReadOnlyList<Subscription>>> QueryAsync(SubscriptionQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ReadAll(_query, command => command
            .With("@messageType", query.Type?.Name)
            .With("@companyId", query.Company?.Value)
            .With("@userId", query.User?.Value)
            .With("@level", query.Level?.ToString()));
    }

    /// <inheritdoc />
    public ValueTask<ErrorOr<Subscription?>> GetAsync(Guid id, CancellationToken ct) =>
        ValueTask.FromResult(SqliteFailures.Run<Subscription?>(() =>
        {
            using var connection = _schema.Open(ConnectionString);
            using var command = connection.Command($"{_select} AND Id = @id").With("@tenantId", _tenant).With("@id", SqliteValues.Id(id));
            using var row = command.ExecuteReader();
            if (!row.Read())
            {
                return ErrorOr<Subscription?>.ForValue(null);
            }

            var subscription = Read(row);
            return subscription.IsError ? subscription.Error! : subscription.Value;
        }));

    /// <inheritdoc />
    public ValueTask<Error?> AddAsync(Subscription subscription, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        return ValueTask.FromResult(Write(_insert, subscription, static _ => SameOwnerTypeChannelAndRecipients()));
    }

    /// <inheritdoc />
    public ValueTask<Error?> UpdateAsync(Subscription subscription, int expectedRevision, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(subscription);
        return ValueTask.FromResult(Write(
            _update, subscription, connection => IsStored(connection, subscription.Id)
                ? new Error(Errors.Conflict, "The stored revision is not the expected one, or another subscription of the owner conflicts.")
                : new Error(Errors.NotFound, "The subscription is not stored."),
            expectedRevision));
    }

    /// <inheritdoc />
    public ValueTask<Error?> DeleteAsync(Guid id, CancellationToken ct) =>
        ValueTask.FromResult(SqliteFailures.Run(() =>
        {
            using var connection = _schema.Open(ConnectionString);
            using var command = connection.Command(_delete).With("@tenantId", _tenant).With("@id", SqliteValues.Id(id));
            command.ExecuteNonQuery();
            return null;
        }));

    private Error? Write(string sql, Subscription subscription, Func<SqliteConnection, Error> noRowChanged, int? expectedRevision = null) =>
        SqliteFailures.Run(() =>
        {
            SerializedSettings? settings = null;
            if (subscription.Settings is { } value)
            {
                var serialized = _settingsJson.Serialize(subscription.Channel.GetValueOrDefault(), value);
                if (serialized.IsError)
                {
                    return serialized.Error;
                }

                settings = serialized.Value;
            }

            using var connection = _schema.Open(ConnectionString);
            using var command = connection.Command(sql)
                .With("@id", SqliteValues.Id(subscription.Id))
                .With("@revision", subscription.Revision)
                .With("@tenantId", _tenant)
                .With("@level", subscription.Owner.Level.ToString())
                .With("@companyId", subscription.Owner.Company?.Value ?? string.Empty)
                .With("@userId", subscription.Owner.User?.Value ?? string.Empty)
                .With("@messageType", subscription.Type.Name)
                .With("@kind", subscription.Kind.ToString())
                .With("@channel", subscription.Channel?.Name ?? string.Empty)
                .With("@settingsType", settings?.ContentType)
                .With("@settingsVersion", settings?.Version)
                .With("@settings", settings?.Json)
                .With("@recipients", subscription.Recipients.Kind.ToString())
                .With("@namedUsers", subscription.Recipients.Kind == RecipientKind.NamedUsers
                    ? IdsOf(subscription.Recipients.Users.Select(static user => user.Value))
                    : null)
                .With("@mandatory", subscription.Mandatory ? 1 : 0)
                .With("@createdAtUtc", SqliteValues.Time(subscription.CreatedAtUtc))
                .With("@lastModifiedAtUtc", SqliteValues.Time(subscription.LastModifiedAtUtc))
                .With("@expectedRevision", expectedRevision);
            return command.ExecuteNonQuery() == 0 ? noRowChanged(connection) : null;
        });

    private bool IsStored(SqliteConnection connection, Guid id)
    {
        using var command = connection.Command(_exists).With("@tenantId", _tenant).With("@id", SqliteValues.Id(id));
        return command.ExecuteScalar() is not null;
    }

    private ValueTask<ErrorOr<IReadOnlyList<Subscription>>> ReadAll(string sql, Func<SqliteCommand, SqliteCommand> withFilters) =>
        ValueTask.FromResult(SqliteFailures.Run<IReadOnlyList<Subscription>>(() =>
        {
            using var connection = _schema.Open(ConnectionString);
            using var command = withFilters(connection.Command(sql).With("@tenantId", _tenant));
            using var row = command.ExecuteReader();
            var subscriptions = new List<Subscription>();
            while (row.Read())
            {
                var subscription = Read(row);
                if (subscription.IsError)
                {
                    return subscription.Error!;
                }

                subscriptions.Add(subscription.Value);
            }

            return subscriptions;
        }));

    /// <exception cref="ArgumentException">A column has an invalid id, name, or enum value.</exception>
    /// <exception cref="FormatException">A column has an invalid id or time, or a required column is NULL.</exception>
    /// <exception cref="JsonException">The column <c>NamedUsers</c> is not a JSON array of strings.</exception>
    private ErrorOr<Subscription> Read(SqliteDataReader row)
    {
        ChannelSettings? settings = null;
        if (Text(row, 11) is { } json)
        {
            var read = _settingsJson.Deserialize(Text(row, 9) ?? string.Empty, (int)(Number(row, 10) ?? 0), json);
            if (read.IsError)
            {
                return read.Error!;
            }

            settings = read.Value;
        }

        return new Subscription(
            Guid.Parse(Text(row, 0)!),
            (int)Required(Number(row, 1)),
            OwnerOf(Enum.Parse<SubscriptionLevel>(Text(row, 3)!), Text(row, 4), Text(row, 5)),
            new MessageType(Text(row, 6)!),
            Enum.Parse<SubscriptionKind>(Text(row, 7)!),
            Text(row, 8) is { Length: > 0 } channel ? new ChannelId(channel) : null,
            settings,
            RecipientsOf(Enum.Parse<RecipientKind>(Text(row, 12)!), Text(row, 13)),
            Required(Number(row, 14)) != 0,
            SqliteValues.ParseTime(Text(row, 15)!),
            SqliteValues.ParseTime(Text(row, 16)!));
    }

    private static SubscriptionOwner OwnerOf(SubscriptionLevel level, string? company, string? user) => (level, company, user) switch
    {
        (SubscriptionLevel.Default, "", "") => SubscriptionOwner.Default,
        (SubscriptionLevel.Company, { Length: > 0 }, "") => SubscriptionOwner.ForCompany(new CompanyId(company)),
        (SubscriptionLevel.User, "", { Length: > 0 }) => SubscriptionOwner.ForUser(new UserId(user)),
        _ => throw new ArgumentException("A subscription owner has the level Default, Company, or User, and only the id of its level."),
    };

    private static SubscriptionRecipients RecipientsOf(RecipientKind kind, string? namedUsers) => kind switch
    {
        RecipientKind.NamedUsers => SubscriptionRecipients.ForUsers(
            [.. JsonSerializer.Deserialize(namedUsers!, IdsJson.Default.IReadOnlyListString)!.Select(static user => new UserId(user))]),
        RecipientKind.Company => SubscriptionRecipients.Company,
        _ => SubscriptionRecipients.ScopeUsers,
    };

    private static string IdsOf(IEnumerable<string> ids) => JsonSerializer.Serialize([.. ids], IdsJson.Default.IReadOnlyListString);

    private static string? Text(SqliteDataReader row, int column) => row.IsDBNull(column) ? null : row.GetString(column);

    private static long? Number(SqliteDataReader row, int column) => row.IsDBNull(column) ? null : row.GetInt64(column);

    private static long Required(long? value) => value ?? throw new FormatException("A required column of a subscription is NULL.");

    private static Error SameOwnerTypeChannelAndRecipients() =>
        new(Errors.Conflict, "The owner already has a subscription for the message type, the channel, and the recipient kind.");
}
