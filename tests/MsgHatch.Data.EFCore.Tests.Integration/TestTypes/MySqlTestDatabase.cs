using MsgHatch.Tests.Shared;
using System.Globalization;
using System.Text;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microting.EntityFrameworkCore.MySql.Infrastructure;
using MsgHatch.Data.EFCore.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MySqlConnector;

namespace MsgHatch.Data.EFCore.Tests.Integration.TestTypes;

/// <summary>
/// One database of a test class on the MySQL of the fixture. With the dialect of <c>MsgHatch.Data.EFCore.MySql</c>, the
/// tables have the prefix <c>MsgHatch</c> (D326). With no dialect, the provider ignores the schema of the model, so the
/// tables have no prefix, and the option <c>TextCollation</c> gives the binary collation (D318). The first reset creates
/// the tables with <c>EnsureCreatedAsync</c>, or with the create step of the test, for example the migrations of a provider
/// package (D320). Each reset empties each table that has a row. The raw reads use their own connection.
/// </summary>
/// <param name="connectionString">The connection string of the database.</param>
/// <param name="tablePrefix">The prefix of the table names: <c>MsgHatch</c> with the dialect, or empty with no dialect.</param>
/// <param name="create">The create step of the database, or null for <c>EnsureCreatedAsync</c> with <see cref="Contexts"/>.</param>
public sealed class MySqlTestDatabase(string connectionString, string tablePrefix = "", Func<MySqlTestDatabase, CancellationToken, Task>? create = null)
{
    /// <summary>The binary collation of the option, so an id is case-sensitive and sorts in binary order.</summary>
    public const string TextCollation = "utf8mb4_bin";

    private static readonly string[] _tables = ["Inbox", "Outbox", "ChannelState", "ChannelEntry", "MessageTypeEntry"];

    private bool _created;

    public string ConnectionString { get; } = connectionString;

    /// <summary>The server version of the provider in the tests, MySQL 8.4.</summary>
    public static ServerVersion ServerVersion { get; } = ServerVersion.Create(8, 4, 0, ServerType.MySql);

    /// <summary>A pooled factory of the contexts of the stores from <c>UseEntityFrameworkStores</c> with no dialect.</summary>
    public IDbContextFactory<MsgHatchDbContext> Contexts { get; } =
        ContextsOf<MsgHatchDbContext>(builder => builder.UseEntityFrameworkStores(o => UseMySqlWithNoDialect(o, connectionString)));

    /// <summary>
    /// The provider setup of a host with no MySQL package: the provider ignores the schema of the model, because it refuses a
    /// schema by default. With no dialect, a message id is <c>char(36)</c>, which MySqlConnector reads as a <c>Guid</c>, so
    /// the connection has <c>GuidFormat=None</c>.
    /// </summary>
    public static DbContextOptionsBuilder UseMySqlWithNoDialect(DbContextOptionsBuilder options, string connectionString) =>
        options.UseMySql(
            new MySqlConnectionStringBuilder(connectionString) { GuidFormat = MySqlGuidFormat.None }.ConnectionString, ServerVersion,
            static mysql => mysql.SchemaBehavior(MySqlSchemaBehavior.Ignore));

    /// <summary>A pooled factory of a MsgHatch context from a setup method, with the collation of the option.</summary>
    public static IDbContextFactory<TContext> ContextsOf<TContext>(Action<MsgHatchBuilder> setup)
        where TContext : DbContext
    {
        var services = EntityFrameworkTestHost.CreateServices(
            configuration: new Dictionary<string, string?> { ["MsgHatch:EntityFramework:TextCollation"] = TextCollation });
        setup(services.AddMsgHatch());
        return services.Build().GetRequiredService<IDbContextFactory<TContext>>();
    }

    /// <summary>A quoted name of MySQL.</summary>
    public static string Quote(string name) => $"`{name.Replace("`", "``", StringComparison.Ordinal)}`";

    /// <summary>The quoted name of a table of the stores, with the prefix of the database.</summary>
    public string Table(string name) => Quote(tablePrefix + name);

    /// <summary>The SQL of a text column as a JSON string, or NULL.</summary>
    public static string JsonText(string column) => $"JSON_QUOTE({Quote(column)})";

    /// <summary>The SQL of a time column as a JSON string in ISO 8601 with <c>Z</c>, which <c>MsgHatchJson</c> reads, or NULL.</summary>
    public static string IsoTime(string column) => $"JSON_QUOTE(DATE_FORMAT({Quote(column)}, '%Y-%m-%dT%H:%i:%s.%fZ'))";

    /// <summary>
    /// The SQL of a JSON object with the members in their order, each as JSON text or as NULL for <c>null</c>. The text
    /// keeps a stored document unchanged: <c>JSON_OBJECT</c> and a cast to <c>json</c> sort the keys.
    /// </summary>
    public static string JsonObject(params (string Name, string Value)[] members) =>
        $"CONCAT('{{', {string.Join(", ',', ", members.Select(static member =>
            $"'\"{member.Name}\":', COALESCE(CONVERT({member.Value} USING utf8mb4) COLLATE utf8mb4_bin, 'null')"))}, '}}')";

    /// <summary>
    /// Empties each table that has a row with <c>TRUNCATE TABLE</c>. After a <c>DELETE</c>, InnoDB keeps a deleted key as a
    /// delete-marked record, and two parallel inserts of that key take shared gap locks and deadlock with the error 1213.
    /// </summary>
    public async ValueTask ResetAsync(CancellationToken ct)
    {
        if (!_created)
        {
            await (create ?? EnsureCreatedAsync)(this, ct);
            _created = true;
        }

        var filled = await RowsAsync(
            string.Join(" UNION ALL ", _tables.Select(table => $"SELECT '{table}' FROM DUAL WHERE EXISTS (SELECT 1 FROM {Table(table)})")), ct);
        if (filled.Count > 0)
        {
            await ExecuteAsync(string.Join(';', filled.Select(row => $"TRUNCATE TABLE {Table(row[0]!)}")), ct);
        }
    }

    /// <summary>Creates the database with no table, for a test of a migration or of a script.</summary>
    public async Task CreateEmptyAsync(CancellationToken ct)
    {
        var builder = new MySqlConnectionStringBuilder(ConnectionString);
        var name = builder.Database;
        builder.Database = string.Empty;
        await using var connection = new MySqlConnection(builder.ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new MySqlCommand($"CREATE DATABASE {Quote(name)}", connection);
        await command.ExecuteNonQueryAsync(ct);
    }

    private static async Task EnsureCreatedAsync(MySqlTestDatabase database, CancellationToken ct)
    {
        await using var context = await database.Contexts.CreateDbContextAsync(ct);
        await context.Database.EnsureCreatedAsync(ct);
    }

    public async Task<int> ExecuteAsync(string sql, CancellationToken ct)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new MySqlCommand(sql, connection);
        return await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>
    /// Runs a script of the <c>mysql</c> client on one connection, for example the idempotent script of the migrations. A
    /// <c>DELIMITER</c> line is a command of the client: it sets the end of the next statements, for example <c>//</c>
    /// around the body of a procedure.
    /// </summary>
    public async Task ExecuteScriptAsync(string script, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(script);
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        var delimiter = ";";
        var statement = new StringBuilder();
        foreach (var line in script.Split('\n').Select(static line => line.TrimEnd('\r')))
        {
            if (line.StartsWith("DELIMITER ", StringComparison.Ordinal))
            {
                delimiter = line["DELIMITER ".Length..].Trim();
                continue;
            }

            statement.AppendLine(line);
            if (line.TrimEnd().EndsWith(delimiter, StringComparison.Ordinal))
            {
                var sql = statement.ToString().TrimEnd();
                await using var command = new MySqlCommand(sql[..^delimiter.Length], connection);
                await command.ExecuteNonQueryAsync(ct);
                statement.Clear();
            }
        }
    }

    /// <summary>The values of each row of a query as text, in the order of the rows. A time is ISO 8601 text with <c>Z</c>, and a NULL value is null.</summary>
    public async Task<List<string?[]>> RowsAsync(string sql, CancellationToken ct)
    {
        await using var connection = new MySqlConnection(ConnectionString);
        await connection.OpenAsync(ct);
        await using var command = new MySqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(ct);
        var rows = new List<string?[]>();
        while (await reader.ReadAsync(ct))
        {
            rows.Add([.. Enumerable.Range(0, reader.FieldCount).Select(column => Text(reader.GetValue(column)))]);
        }

        return rows;
    }

    private static string? Text(object value) => value switch
    {
        DBNull => null,
        DateTime time => time.ToString("yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'", CultureInfo.InvariantCulture),
        bool flag => flag ? "1" : "0",
        byte[] bytes => Convert.ToHexString(bytes),
        _ => Convert.ToString(value, CultureInfo.InvariantCulture),
    };
}
