using System.Data;
using System.Data.Common;
using MsgHatch.Domain.Delivery;

namespace MsgHatch.Data.EFCore;

/// <summary>
/// The named parameters of one raw SQL statement, with an explicit type for each value: a time is <c>datetime2</c>, so
/// it keeps its 100 ns ticks, and a name is an ANSI text, as its column is.
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

    public SqlArguments Time(string name, DateTime? value) => Add(name, value, DbType.DateTime2);

    public SqlArguments Number(string name, int? value) => Add(name, value, DbType.Int32);

    /// <summary>The parameters of the delivery columns of a new row, with the names of <see cref="SqlServerStatements"/>.</summary>
    public SqlArguments Delivery(DeliveryState delivery) => this
        .Text("@id", MessageQueries.Id(delivery.Id))
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
