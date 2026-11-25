using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using MsgHatch.Channels.Email;
using MsgHatch.Data.Sqlite;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions;

namespace ExampleApp.Recipients;

/// <summary>
/// The recipient directory of the example (D76), on the SQLite file of the stores. The table <c>Companies</c> holds the
/// default language and the email address of each company, and the table <c>Users</c> holds the company, the UI
/// language, and the email address of each user. The ids are <c>int</c> values, mapped with <see cref="UserId.From{T}"/>
/// and <see cref="CompanyId.From{T}"/> (D73). Each row has the tenant of its directory, so the user <c>"1"</c> of one
/// tenant is not the user <c>"1"</c> of another tenant (D114).
/// </summary>
/// <param name="key">The tenant of this directory (D126).</param>
/// <param name="options">The options of the SQLite stores, with the connection string of the file.</param>
public sealed class ExampleRecipientDirectory(TenantKey key, IOptions<SqliteStoreOptions> options) : IRecipientDirectory
{
    private const string _schema = """
        CREATE TABLE IF NOT EXISTS Companies (
            TenantId TEXT NOT NULL, Id INTEGER NOT NULL, DefaultLanguage TEXT NOT NULL, Email TEXT,
            PRIMARY KEY (TenantId, Id));
        CREATE TABLE IF NOT EXISTS Users (
            TenantId TEXT NOT NULL, Id INTEGER NOT NULL, CompanyId INTEGER, UiLanguage TEXT NOT NULL, Email TEXT,
            PRIMARY KEY (TenantId, Id));
        CREATE INDEX IF NOT EXISTS IX_Users_Company ON Users (TenantId, CompanyId);
        """;

    private const string _userColumns = "Id, CompanyId, UiLanguage, Email";

    private readonly string _tenant = key.Tenant?.Value ?? string.Empty;
    private readonly string _connectionString = options?.Value.ConnectionString ?? throw new ArgumentNullException(nameof(options));

    /// <inheritdoc/>
    public ValueTask<ErrorOr<IReadOnlyList<UserProfile>>> GetUsersAsync(IReadOnlyList<UserId> users, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(users);
        var ids = users.Select(static user => user.TryAs<int>(out var id) ? id : (int?)null).OfType<int>();
        return ReadUsersAsync($"Id IN ({string.Join(',', ids)})", company: null, ct);
    }

    /// <inheritdoc/>
    public async ValueTask<ErrorOr<IReadOnlyList<UserProfile>>> GetCompanyUsersAsync(CompanyId company, CancellationToken ct) =>
        company.TryAs<int>(out var id) ? await ReadUsersAsync("CompanyId = $company", id, ct) : ErrorOr<IReadOnlyList<UserProfile>>.ForValue([]);

    /// <inheritdoc/>
    public async ValueTask<ErrorOr<CompanyProfile?>> GetCompanyAsync(CompanyId company, CancellationToken ct)
    {
        if (!company.TryAs<int>(out var id))
        {
            return ErrorOr<CompanyProfile?>.ForValue(null);
        }

        return await RunAsync<CompanyProfile?>(async connection =>
        {
            await using var command = Command(connection, "SELECT Id, DefaultLanguage, Email FROM Companies WHERE TenantId = $tenant AND Id = $id");
            command.Parameters.AddWithValue("$id", id);
            await using var reader = await command.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct)
                ? new CompanyProfile(CompanyId.From(reader.GetInt32(0)), reader.GetString(1), AddressesOf(reader, 2))
                : null;
        }, ct);
    }

    /// <summary>Writes a company, or replaces the company with the same id.</summary>
    /// <param name="company">The company.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null, or the error of the database.</returns>
    public ValueTask<Error?> SaveCompanyAsync(ExampleCompany company, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(company);
        return WriteAsync(
            "INSERT OR REPLACE INTO Companies (TenantId, Id, DefaultLanguage, Email) VALUES ($tenant, $id, $language, $email)",
            command =>
            {
                command.Parameters.AddWithValue("$id", company.Id);
                command.Parameters.AddWithValue("$language", company.DefaultLanguage);
                command.Parameters.AddWithValue("$email", (object?)company.Email ?? DBNull.Value);
            }, ct);
    }

    /// <summary>Writes a user, or replaces the user with the same id.</summary>
    /// <param name="user">The user.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null, or the error of the database.</returns>
    public ValueTask<Error?> SaveUserAsync(ExampleUser user, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(user);
        return WriteAsync(
            "INSERT OR REPLACE INTO Users (TenantId, Id, CompanyId, UiLanguage, Email) VALUES ($tenant, $id, $company, $language, $email)",
            command =>
            {
                command.Parameters.AddWithValue("$id", user.Id);
                command.Parameters.AddWithValue("$company", (object?)user.CompanyId ?? DBNull.Value);
                command.Parameters.AddWithValue("$language", user.UiLanguage);
                command.Parameters.AddWithValue("$email", (object?)user.Email ?? DBNull.Value);
            }, ct);
    }

    /// <summary>Tells whether the directory of this tenant has a user.</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>True when a user exists, or the error of the database.</returns>
    public ValueTask<ErrorOr<bool>> HasUsersAsync(CancellationToken ct) =>
        RunAsync(async connection =>
        {
            await using var command = Command(connection, "SELECT EXISTS (SELECT 1 FROM Users WHERE TenantId = $tenant)");
            return Convert.ToInt64(await command.ExecuteScalarAsync(ct), System.Globalization.CultureInfo.InvariantCulture) == 1;
        }, ct);

    private ValueTask<ErrorOr<IReadOnlyList<UserProfile>>> ReadUsersAsync(string filter, int? company, CancellationToken ct) =>
        RunAsync<IReadOnlyList<UserProfile>>(async connection =>
        {
            await using var command = Command(connection, $"SELECT {_userColumns} FROM Users WHERE TenantId = $tenant AND {filter} ORDER BY Id");
            if (company is { } id)
            {
                command.Parameters.AddWithValue("$company", id);
            }

            await using var reader = await command.ExecuteReaderAsync(ct);
            var users = new List<UserProfile>();
            while (await reader.ReadAsync(ct))
            {
                users.Add(new UserProfile(
                    UserId.From(reader.GetInt32(0)), reader.IsDBNull(1) ? null : CompanyId.From(reader.GetInt32(1)), reader.GetString(2),
                    AddressesOf(reader, 3)));
            }

            return users;
        }, ct);

    private async ValueTask<Error?> WriteAsync(string sql, Action<SqliteCommand> bind, CancellationToken ct)
    {
        var result = await RunAsync(async connection =>
        {
            await using var command = Command(connection, sql);
            bind(command);
            return await command.ExecuteNonQueryAsync(ct);
        }, ct);
        return result.Error;
    }

    private async ValueTask<ErrorOr<T>> RunAsync<T>(Func<SqliteConnection, Task<T>> work, CancellationToken ct)
    {
        try
        {
            await using var connection = new SqliteConnection(_connectionString);
            await connection.OpenAsync(ct);
            await CreateTablesAsync(connection, ct);
            return await work(connection);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            return new Error(Errors.OperationCancelled);
        }
        catch (SqliteException exception)
        {
            return new Error(Errors.Unavailable, $"SQLite error {exception.SqliteErrorCode}", exception);
        }
    }

    // ponytail: the tables are checked on each open, add a flag for each connection string if the check shows in a profile
    private static async Task CreateTablesAsync(SqliteConnection connection, CancellationToken ct)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = _schema;
        await command.ExecuteNonQueryAsync(ct);
    }

    private SqliteCommand Command(SqliteConnection connection, string sql)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        command.Parameters.AddWithValue("$tenant", _tenant);
        return command;
    }

    private static Dictionary<MsgHatch.Domain.Identity.ChannelId, string> AddressesOf(SqliteDataReader reader, int emailColumn) =>
        reader.IsDBNull(emailColumn) ? [] : new() { [EmailChannel.Id] = reader.GetString(emailColumn) };
}
