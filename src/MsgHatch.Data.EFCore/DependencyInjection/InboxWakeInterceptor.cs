using System.Data.Common;
using System.Runtime.CompilerServices;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;
using MsgHatch.Data.EFCore.Messages;
using MsgHatch.Domain.Dispatch;

namespace MsgHatch.Data.EFCore.DependencyInjection;

/// <summary>
/// Wakes the inbox loop after the commit of the host (D333). After a save of a context of the host that wrote an inbox row,
/// it notifies <see cref="DispatchTopic.Inbox"/>: at once with no transaction of the host, or else after the commit of
/// that transaction. A failed save, a rollback, or a save with no inbox row notifies nothing. A notify error only logs a
/// Warning, as in the pipeline. A host that commits its <see cref="DbTransaction"/> outside EF Core gets no wake, so the
/// loop finds the row at its next poll. One instance serves all contexts, and keeps the state of each context apart.
/// </summary>
/// <param name="signal">The dispatch signal.</param>
/// <param name="logger">The logger.</param>
internal sealed class InboxWakeInterceptor(IDispatchSignal signal, ILogger<InboxWakeInterceptor> logger) : SaveChangesInterceptor, IDbTransactionInterceptor
{
    private readonly ConditionalWeakTable<DbContext, WakeState> _states = [];

    public override InterceptionResult<int> SavingChanges(DbContextEventData eventData, InterceptionResult<int> result)
    {
        RememberInboxRows(eventData);
        return result;
    }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(
        DbContextEventData eventData, InterceptionResult<int> result, CancellationToken cancellationToken = default)
    {
        RememberInboxRows(eventData);
        return ValueTask.FromResult(result);
    }

    public override int SavedChanges(SaveChangesCompletedEventData eventData, int result)
    {
        if (WakesAfterSave(eventData.Context))
        {
            _ = NotifyAsync(CancellationToken.None);
        }

        return result;
    }

    public override async ValueTask<int> SavedChangesAsync(SaveChangesCompletedEventData eventData, int result, CancellationToken cancellationToken = default)
    {
        if (WakesAfterSave(eventData.Context))
        {
            await NotifyAsync(cancellationToken);
        }

        return result;
    }

    public override void SaveChangesFailed(DbContextErrorEventData eventData) => EndSave(eventData.Context);

    public override Task SaveChangesFailedAsync(DbContextErrorEventData eventData, CancellationToken cancellationToken = default)
    {
        EndSave(eventData.Context);
        return Task.CompletedTask;
    }

    public void TransactionCommitted(DbTransaction transaction, TransactionEndEventData eventData)
    {
        if (WakesAfterCommit(eventData.Context))
        {
            _ = NotifyAsync(CancellationToken.None);
        }
    }

    public async Task TransactionCommittedAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        if (WakesAfterCommit(eventData.Context))
        {
            await NotifyAsync(cancellationToken);
        }
    }

    public void TransactionRolledBack(DbTransaction transaction, TransactionEndEventData eventData) => Forget(eventData.Context);

    public Task TransactionRolledBackAsync(DbTransaction transaction, TransactionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        Forget(eventData.Context);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Reads the added inbox rows with no detection of changes, because a staged row is added explicitly, and the save
    /// detects the changes of the host anyway.
    /// </summary>
    private void RememberInboxRows(DbContextEventData eventData)
    {
        if (eventData.Context is not { } context)
        {
            return;
        }

        var tracker = context.ChangeTracker;
        var detects = tracker.AutoDetectChangesEnabled;
        tracker.AutoDetectChangesEnabled = false;
        try
        {
            if (tracker.Entries<InboxRow>().Any(static entry => entry.State == EntityState.Added))
            {
                _states.GetOrCreateValue(context).Saving = true;
            }
            else
            {
                EndSave(context);
            }
        }
        finally
        {
            tracker.AutoDetectChangesEnabled = detects;
        }
    }

    /// <summary>Gives true when the save wrote an inbox row with no transaction of the host. Inside a transaction, the wake waits for its commit.</summary>
    private bool WakesAfterSave(DbContext? context)
    {
        if (context is null || !_states.TryGetValue(context, out var state) || !state.Saving)
        {
            return false;
        }

        state.Saving = false;
        state.WaitsForCommit = context.Database.CurrentTransaction is not null;
        return !state.WaitsForCommit;
    }

    private bool WakesAfterCommit(DbContext? context)
    {
        if (context is null || !_states.TryGetValue(context, out var state) || !state.WaitsForCommit)
        {
            return false;
        }

        state.WaitsForCommit = false;
        return true;
    }

    /// <summary>Ends the current save with no wake. A wait for the commit of an earlier save stays.</summary>
    private void EndSave(DbContext? context)
    {
        if (context is not null && _states.TryGetValue(context, out var state))
        {
            state.Saving = false;
        }
    }

    private void Forget(DbContext? context)
    {
        if (context is not null)
        {
            _states.Remove(context);
        }
    }

    private async Task NotifyAsync(CancellationToken ct)
    {
        if (await signal.NotifyAsync(DispatchTopic.Inbox, ct) is { } error)
        {
            logger.HostCommitNotifyFailed(DispatchTopic.Inbox.Name, error.Type.Code, error.Type.Name, error.Message);
        }
    }

    private sealed class WakeState
    {
        public bool Saving { get; set; }

        public bool WaitsForCommit { get; set; }
    }
}
