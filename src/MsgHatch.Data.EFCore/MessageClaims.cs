using System.Data.Common;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Storage;

namespace MsgHatch.Data.EFCore;

/// <summary>The values of one claim, with a new claim token.</summary>
/// <param name="MaxCount">The maximum count of claimed rows.</param>
/// <param name="NowUtc">The time of the claim.</param>
/// <param name="LeaseUntilUtc">The end of the lease, which is the next due time of a claimed row (D103).</param>
internal sealed record ClaimArguments(int MaxCount, DateTime NowUtc, DateTime LeaseUntilUtc)
{
    public string Token { get; } = Guid.NewGuid().ToString("D");
}

/// <summary>
/// The two claims of a message table (D318). A dialect with a claim statement claims in that one statement, with the
/// quoted names of the model and the parameters in the types of their columns. Else a LINQ query reads the due candidates
/// in the claim order (D144), and one <c>ExecuteUpdate</c> claims those that are still pending and due. When another
/// dispatcher took a candidate first, the claim reads the next candidates, until it has the maximum count or no due row is
/// left. The claimed rows are the candidates with the claim token.
/// </summary>
internal static class MessageClaims
{
    /// <summary>Claims the due rows, and gives them with their values after the claim, in no order.</summary>
    /// <param name="context">The context.</param>
    /// <param name="table">The message table.</param>
    /// <param name="rows">The rows that the claim can take: each row of the inbox, or the rows of one channel of the outbox.</param>
    /// <param name="channel">The channel of the outbox claim, or null for the inbox.</param>
    /// <param name="claim">The values of the claim.</param>
    /// <param name="ct">The cancel token.</param>
    public static Task<List<TRow>> ClaimAsync<TRow>(
        DbContext context, DbSet<TRow> table, IQueryable<TRow> rows, string? channel, ClaimArguments claim, CancellationToken ct)
        where TRow : MessageRow =>
        StatementOf(context, table, channel, claim) is { } statement
            ? statement.AsNoTracking().ToListAsync(ct)
            : ClaimWithLinqAsync(rows, claim, ct);

    private static async Task<List<TRow>> ClaimWithLinqAsync<TRow>(IQueryable<TRow> rows, ClaimArguments claim, CancellationToken ct)
        where TRow : MessageRow
    {
        var (nowUtc, leaseUntilUtc, token) = (claim.NowUtc, claim.LeaseUntilUtc, claim.Token);
        var due = rows.Where(row => row.Status == MessageQueries.Pending && row.NextTryAtUtc <= nowUtc);
        List<long> candidates = [];
        var claimed = 0;
        while (claimed < claim.MaxCount)
        {
            var wanted = claim.MaxCount - claimed;
            var next = await due.OrderBy(static row => row.NextTryAtUtc).ThenBy(static row => row.Sequence).Select(static row => row.Sequence)
                .Take(wanted).ToListAsync(ct);
            if (next.Count == 0)
            {
                break;
            }

            var updated = await due.Where(row => next.Contains(row.Sequence)).ExecuteUpdateAsync(
                set => set
                    .SetProperty(static row => row.ClaimedDueAtUtc, static row => (DateTime?)row.NextTryAtUtc)
                    .SetProperty(static row => row.ClaimToken, token)
                    .SetProperty(static row => row.NextTryAtUtc, leaseUntilUtc)
                    .SetProperty(static row => row.LeaseUntilUtc, (DateTime?)leaseUntilUtc),
                ct);
            candidates.AddRange(next);
            claimed += updated;
            if (next.Count < wanted && updated == next.Count)
            {
                break;
            }
        }

        return candidates.Count == 0
            ? []
            : await rows.AsNoTracking().Where(row => candidates.Contains(row.Sequence) && row.ClaimToken == token).ToListAsync(ct);
    }

    /// <summary>The claim statement of the dialect of the context, or null when the dialect gives none.</summary>
    private static IQueryable<TRow>? StatementOf<TRow>(DbContext context, DbSet<TRow> table, string? channel, ClaimArguments claim)
        where TRow : MessageRow
    {
        var dialect = StoreOptionsExtension.Of(context).Dialect;
        if (dialect is null && !SqlServerClaim.IsFor(context))
        {
            return null;
        }

        var entity = context.Model.FindEntityType(typeof(TRow))!;
        var storeObject = StoreObjectIdentifier.Table(entity.GetTableName()!, entity.GetSchema());
        var sql = context.GetService<ISqlGenerationHelper>();
        using var command = context.Database.GetDbConnection().CreateCommand();
        var parameters = new List<DbParameter>();

        string Column(string property) => sql.DelimitIdentifier(entity.FindProperty(property)!.GetColumnName(storeObject)!);

        string Parameter(string name, RelationalTypeMapping mapping, object value)
        {
            parameters.Add(mapping.CreateParameter(command, sql.GenerateParameterName(name), value));
            return sql.GenerateParameterNamePlaceholder(name);
        }

        RelationalTypeMapping MappingOf(string property) => entity.FindProperty(property)!.GetRelationalTypeMapping();

        var names = new ClaimStatementNames
        {
            Table = sql.DelimitIdentifier(storeObject.Name, storeObject.Schema),
            Sequence = Column(nameof(MessageRow.Sequence)),
            Status = Column(nameof(MessageRow.Status)),
            NextTryAtUtc = Column(nameof(MessageRow.NextTryAtUtc)),
            LeaseUntilUtc = Column(nameof(MessageRow.LeaseUntilUtc)),
            ClaimToken = Column(nameof(MessageRow.ClaimToken)),
            ClaimedDueAtUtc = Column(nameof(MessageRow.ClaimedDueAtUtc)),
            Channel = channel is null ? null : Column(nameof(OutboxRow.Channel)),
            MaxCountParameter = Parameter("maxCount", context.GetService<IRelationalTypeMappingSource>().FindMapping(typeof(int))!, claim.MaxCount),
            NowUtcParameter = Parameter("nowUtc", MappingOf(nameof(MessageRow.NextTryAtUtc)), claim.NowUtc),
            LeaseUntilUtcParameter = Parameter("leaseUntilUtc", MappingOf(nameof(MessageRow.NextTryAtUtc)), claim.LeaseUntilUtc),
            ClaimTokenParameter = Parameter("claimToken", MappingOf(nameof(MessageRow.ClaimToken)), claim.Token),
            PendingParameter = Parameter("pending", MappingOf(nameof(MessageRow.Status)), MessageQueries.Pending),
            ChannelParameter = channel is null ? null : Parameter("channel", MappingOf(nameof(OutboxRow.Channel)), channel),
        };
        var statement = dialect is null ? SqlServerClaim.Statement(names) : dialect.ClaimStatement(names);
        return statement is null ? null : table.FromSqlRaw(statement, [.. parameters]);
    }
}
