using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MsgHatch.Data.EFCore.Tests.Unit.TestTypes;

/// <summary>
/// Plays another dispatcher: right before the first claim update of the store, it runs a change of the due rows on its
/// own connection, for example a lease of the first candidates. Each later command runs unchanged.
/// </summary>
public sealed class FirstClaimUpdateInterceptor(SqliteTestDatabase database, string otherDispatcherChange) : DbCommandInterceptor
{
    private int _done;

    public override ValueTask<InterceptionResult<int>> NonQueryExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        if (command.CommandText.Contains("ClaimToken", StringComparison.Ordinal) && Interlocked.Exchange(ref _done, 1) == 0)
        {
            database.Execute(otherDispatcherChange);
        }

        return ValueTask.FromResult(result);
    }
}
