using Microsoft.EntityFrameworkCore.Migrations;

namespace MsgHatch.Data.EFCore.Tests.Unit.TestTypes;

/// <summary>
/// The doubled history of the migrations of a context: no migration is applied, and the history reads no table, so the
/// startup check of <c>RequireCurrentSchema</c> opens no connection. Each other member is not part of the check.
/// </summary>
/// <param name="dependencies">The dependencies of the history.</param>
public sealed class NoAppliedMigrations(HistoryRepositoryDependencies dependencies) : HistoryRepository(dependencies)
{
    public override LockReleaseBehavior LockReleaseBehavior => throw new NotSupportedException();

    protected override string ExistsSql => throw new NotSupportedException();

    public override IReadOnlyList<HistoryRow> GetAppliedMigrations() => [];

    public override IMigrationsDatabaseLock AcquireDatabaseLock() => throw new NotSupportedException();

    public override Task<IMigrationsDatabaseLock> AcquireDatabaseLockAsync(CancellationToken cancellationToken = default) =>
        throw new NotSupportedException();

    public override string GetCreateIfNotExistsScript() => throw new NotSupportedException();

    public override string GetBeginIfNotExistsScript(string migrationId) => throw new NotSupportedException();

    public override string GetBeginIfExistsScript(string migrationId) => throw new NotSupportedException();

    public override string GetEndIfScript() => throw new NotSupportedException();

    protected override bool InterpretExistsResult(object? value) => throw new NotSupportedException();
}
