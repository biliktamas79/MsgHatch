using System.Collections.Concurrent;
using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;

namespace MsgHatch.Data.EFCore.Tests.Unit.TestTypes;

/// <summary>Records the kind of each time parameter of each command that returns rows, for example an insert with <c>RETURNING</c>.</summary>
public sealed class TimeParameterInterceptor : DbCommandInterceptor
{
    private readonly ConcurrentQueue<DateTimeKind> _kinds = new();

    public IReadOnlyCollection<DateTimeKind> Kinds => _kinds;

    public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
        DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
    {
        foreach (DbParameter parameter in command.Parameters)
        {
            if (parameter.Value is DateTime time)
            {
                _kinds.Enqueue(time.Kind);
            }
        }

        return ValueTask.FromResult(result);
    }
}
