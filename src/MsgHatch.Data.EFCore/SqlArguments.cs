using System.Data;
using System.Data.Common;
using MsgHatch.Domain.Delivery;

namespace MsgHatch.Data.EFCore;

/// <summary>
/// The named parameters of one raw SQL statement, with an explicit type for each value: a time is <c>datetime2</c>, so
/// it keeps its 100 ns ticks, a name is an ANSI text, as its column is, and a message id is a fixed ANSI text, as its
/// <c>char(36)</c> column is. With an <c>nvarchar</c> message id, SQL Server converts the column, and parallel adds of
/// one id got the deadlock 1205 (checked fact after phase 18).
/// </summary>
/// <param name="connection">The connection of the context, which gives the parameter type of its provider.</param>
internal sealed class SqlArguments(DbConnection connection)
{
    private readonly DbProviderFactory _factory = DbProviderFactories.GetFactory(connection)
        ?? throw new InvalidOperationException("The connection of the context has no provider factory.");

    private readonly List<DbParameter> _parameters = [];

    public object[] Values => [.. _parameters];

    public SqlArguments Text(string name, string? value) => Add(name, value, DbType.String);

    public SqlArguments Name(string name, string? value) => Add(name, value, DbType.AnsiString);

    public SqlArguments MessageId(string name, string? value) => Add(name, value, DbType.AnsiStringFixedLength);

    public SqlArguments Time(string name, DateTime? value) => Add(name, value, DbType.DateTime2);

    public SqlArguments Number(string name, int? value) => Add(name, value, DbType.Int32);

    public SqlArguments Milliseconds(string name, TimeSpan? value) => Add(name, (long?)value?.TotalMilliseconds, DbType.Int64);

    /// <summary>The parameters of the delivery columns of a new row, with the names of <see cref="SqlServerStatements"/>.</summary>
    public SqlArguments Delivery(DeliveryState delivery) => this
        .MessageId("@id", MessageQueries.Id(delivery.Id))
        .Name("@status", delivery.Status.ToString())
        .Number("@tryCount", delivery.TryCount)
        .Number("@maxTryCount", delivery.MaxTryCount)
        .Time("@createdAtUtc", delivery.CreatedAtUtc)
        .Time("@lastTryAtUtc", delivery.LastTryAtUtc)
        .Time("@nextTryAtUtc", delivery.NextTryAtUtc)
        .Time("@leaseUntilUtc", delivery.LeaseUntilUtc)
        .Time("@expiresAtUtc", delivery.ExpiresAtUtc)
        .Text("@lastError", delivery.LastError);

    private SqlArguments Add(string name, object? value, DbType type)
    {
        var parameter = _factory.CreateParameter() ?? throw new InvalidOperationException("The provider creates no parameter.");
        parameter.ParameterName = name;
        parameter.DbType = type;
        parameter.Value = value ?? DBNull.Value;
        _parameters.Add(parameter);
        return this;
    }
}
