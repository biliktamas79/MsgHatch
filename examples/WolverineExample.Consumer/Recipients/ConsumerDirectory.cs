using System.Collections.ObjectModel;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Options;
using MsgHatch.Data.Sqlite;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Email;
using MsgHatch.Subscriptions;

namespace WolverineExample.Consumer.Recipients;

/// <summary>
/// The recipient directory of the consumer (D76), on the SQLite file of the stores. The table <c>Companies</c> holds the
/// default language and the email address of the administrator of each company. The email address is the company
/// address of the email channel, and the default language selects the language of the email templates for the company.
/// The table <c>Users</c> holds the company of each user. Each row has the tenant of its directory, so the company
/// <c>100</c> of one tenant is not the company <c>100</c> of another tenant (D114). A user profile has no language,
/// because no email goes to a user in this example.
/// </summary>
/// <param name="key">The tenant of this directory (D126).</param>
/// <param name="options">The options of the SQLite stores, with the connection string of the file.</param>
public sealed class ConsumerDirectory(TenantKey key, IOptions<SqliteStoreOptions> options) : IRecipientDirectory
{
    private const string _schema = """
        CREATE TABLE IF NOT EXISTS Companies (
            TenantId TEXT NOT NULL, Id INTEGER NOT NULL, DefaultLanguage TEXT NOT NULL, AdminEmail TEXT, PRIMARY KEY (TenantId, Id));
        CREATE TABLE IF NOT EXISTS Users (
            TenantId TEXT NOT NULL, Id INTEGER NOT NULL, CompanyId INTEGER, PRIMARY KEY (TenantId, Id));
        CREATE INDEX IF NOT EXISTS IX_Users_Company ON Users (TenantId, CompanyId);
        """;

    private readonly string _tenant = key.Tenant?.Value ?? string.Empty;
    private readonly string _connectionString = options?.Value.ConnectionString ?? throw new ArgumentNullException(nameof(options));

    /// <summary>Gives the directory of one tenant, with the connection string of the host.</summary>
    /// <param name="services">The services of the host.</param>
    /// <param name="key">The tenant.</param>
    /// <returns>The directory.</returns>
    public static ConsumerDirectory For(IServiceProvider services, TenantKey key) => ActivatorUtilities.CreateInstance<ConsumerDirectory>(services, key);

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
            await using var command = Command(connection, "SELECT Id, DefaultLanguage, AdminEmail FROM Companies WHERE TenantId = $tenant AND Id = $id");
            command.Parameters.AddWithValue("$id", id);
            await using var reader = await command.ExecuteReaderAsync(ct);
            return await reader.ReadAsync(ct) ? new CompanyProfile(CompanyId.From(reader.GetInt32(0)), reader.GetString(1), AddressesOf(reader, 2)) : null;
        }, ct);
    }

    /// <summary>Gives the ids of all companies of the tenant, in the order of the ids.</summary>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The ids, or the error of the database.</returns>
    public ValueTask<ErrorOr<IReadOnlyList<int>>> GetCompanyIdsAsync(CancellationToken ct) =>
        RunAsync<IReadOnlyList<int>>(async connection =>
        {
            await using var command = Command(connection, "SELECT Id FROM Companies WHERE TenantId = $tenant ORDER BY Id");
            await using var reader = await command.ExecuteReaderAsync(ct);
            List<int> ids = [];
            while (await reader.ReadAsync(ct))
            {
                ids.Add(reader.GetInt32(0));
            }

            return ids;
        }, ct);

    /// <summary>Writes a company, or replaces the company with the same id.</summary>
    /// <param name="company">The company.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>Null, or the error of the database.</returns>
    public ValueTask<Error?> SaveCompanyAsync(ExampleCompany company, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(company);
        return WriteAsync(
            "INSERT OR REPLACE INTO Companies (TenantId, Id, DefaultLanguage, AdminEmail) VALUES ($tenant, $id, $language, $email)",
            command =>
            {
                command.Parameters.AddWithValue("$id", company.Id);
                command.Parameters.AddWithValue("$language", company.DefaultLanguage);
                command.Parameters.AddWithValue("$email", (object?)company.AdminEmail ?? DBNull.Value);
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
            "INSERT OR REPLACE INTO Users (TenantId, Id, CompanyId) VALUES ($tenant, $id, $company)",
            command =>
            {
                command.Parameters.AddWithValue("$id", user.Id);
                command.Parameters.AddWithValue("$company", (object?)user.CompanyId ?? DBNull.Value);
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
            await using var command = Command(connection, $"SELECT Id, CompanyId FROM Users WHERE TenantId = $tenant AND {filter} ORDER BY Id");
            if (company is { } id)
            {
                command.Parameters.AddWithValue("$company", id);
            }

            await using var reader = await command.ExecuteReaderAsync(ct);
            List<UserProfile> users = [];
            while (await reader.ReadAsync(ct))
            {
                var companyId = reader.IsDBNull(1) ? (CompanyId?)null : CompanyId.From(reader.GetInt32(1));
                users.Add(new UserProfile(UserId.From(reader.GetInt32(0)), companyId, UiLanguage: null, ReadOnlyDictionary<ChannelId, string>.Empty));
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

    private static Dictionary<ChannelId, string> AddressesOf(SqliteDataReader reader, int emailColumn) =>
        reader.IsDBNull(emailColumn) ? [] : new() { [EmailChannel.Id] = reader.GetString(emailColumn) };
}
