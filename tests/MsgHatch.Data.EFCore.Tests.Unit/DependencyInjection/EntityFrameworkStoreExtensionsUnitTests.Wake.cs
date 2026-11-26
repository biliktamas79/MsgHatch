using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Inbox;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Tests.Shared;
using NSubstitute;

namespace MsgHatch.Data.EFCore.Tests.Unit.DependencyInjection;

/// <summary>
/// The wake of the inbox loop after the commit of the host (D333), on the EF Core SQLite provider: a save of the context of
/// the host with an inbox row notifies <see cref="DispatchTopic.Inbox"/> after the commit, and else nothing. Each test
/// stages the row with the inbox tenant store, so the notify of the pipeline does not count.
/// </summary>
public sealed partial class EntityFrameworkStoreExtensionsUnitTests
{
    private readonly IDispatchSignal _signal = Substitute.For<IDispatchSignal>();

    [Fact]
    public async Task SaveChangesAsync_InboxRowWithNoTransactionOfTheHost_NotifiesTheInboxTopicOneTime()
    {
        // Arrange
        await using var provider = await BuildWithHostContextAsync(signal: _signal);
        await using var scope = provider.CreateAsyncScope();
        await StageAsync(scope);

        // Act
        await HostOf(scope).SaveChangesAsync(Ct);

        // Assert
        _ = _signal.Received(1).NotifyAsync(DispatchTopic.Inbox, Ct);
    }

    [Fact]
    public async Task SaveChanges_InboxRowWithNoTransactionOfTheHost_NotifiesTheInboxTopicOneTime()
    {
        // Arrange
        await using var provider = await BuildWithHostContextAsync(signal: _signal);
        await using var scope = provider.CreateAsyncScope();
        await StageAsync(scope);

        // Act
        HostOf(scope).SaveChanges();

        // Assert
        _ = _signal.Received(1).NotifyAsync(DispatchTopic.Inbox, CancellationToken.None);
    }

    [Fact]
    public async Task SaveChangesAsync_InboxRowInATransactionOfTheHost_NotifiesNothingBeforeTheCommit()
    {
        // Arrange
        await using var provider = await BuildWithHostContextAsync(signal: _signal);
        await using var scope = provider.CreateAsyncScope();
        await using var transaction = await HostOf(scope).Database.BeginTransactionAsync(Ct);
        await StageAsync(scope);

        // Act
        await HostOf(scope).SaveChangesAsync(Ct);

        // Assert
        _ = _signal.DidNotReceive().NotifyAsync(Arg.Any<DispatchTopic>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitAsync_TransactionOfTheHostWithAnInboxRow_NotifiesTheInboxTopicOneTime()
    {
        // Arrange
        await using var provider = await BuildWithHostContextAsync(signal: _signal);
        await using var scope = provider.CreateAsyncScope();
        await using var transaction = await HostOf(scope).Database.BeginTransactionAsync(Ct);
        await StageAsync(scope);
        await HostOf(scope).SaveChangesAsync(Ct);

        // Act
        await transaction.CommitAsync(Ct);

        // Assert
        _ = _signal.Received(1).NotifyAsync(DispatchTopic.Inbox, Ct);
    }

    [Fact]
    public async Task Commit_TransactionOfTheHostWithAnInboxRow_NotifiesTheInboxTopicOneTime()
    {
        // Arrange
        await using var provider = await BuildWithHostContextAsync(signal: _signal);
        await using var scope = provider.CreateAsyncScope();
        using var transaction = HostOf(scope).Database.BeginTransaction();
        await StageAsync(scope);
        HostOf(scope).SaveChanges();

        // Act
        transaction.Commit();

        // Assert
        _ = _signal.Received(1).NotifyAsync(DispatchTopic.Inbox, CancellationToken.None);
    }

    [Fact]
    public async Task CommitAsync_TransactionOfTheHostWithASaveAfterTheInboxRow_NotifiesTheInboxTopicOneTime()
    {
        // Arrange
        await using var provider = await BuildWithHostContextAsync(signal: _signal);
        await using var scope = provider.CreateAsyncScope();
        await using var transaction = await HostOf(scope).Database.BeginTransactionAsync(Ct);
        await StageAsync(scope);
        await HostOf(scope).SaveChangesAsync(Ct);
        HostOf(scope).Orders.Add(new TestOrder { Text = "order", CreatedAtUtc = Now });
        await HostOf(scope).SaveChangesAsync(Ct);

        // Act
        await transaction.CommitAsync(Ct);

        // Assert
        _ = _signal.Received(1).NotifyAsync(DispatchTopic.Inbox, Ct);
    }

    [Fact]
    public async Task RollbackAsync_TransactionOfTheHostWithAnInboxRow_NotifiesNothing()
    {
        // Arrange
        await using var provider = await BuildWithHostContextAsync(signal: _signal);
        await using var scope = provider.CreateAsyncScope();
        await using var transaction = await HostOf(scope).Database.BeginTransactionAsync(Ct);
        await StageAsync(scope);
        await HostOf(scope).SaveChangesAsync(Ct);

        // Act
        await transaction.RollbackAsync(Ct);

        // Assert
        _ = _signal.DidNotReceive().NotifyAsync(Arg.Any<DispatchTopic>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveChangesAsync_NoInboxRow_NotifiesNothing()
    {
        // Arrange
        await using var provider = await BuildWithHostContextAsync(signal: _signal);
        await using var scope = provider.CreateAsyncScope();
        HostOf(scope).Orders.Add(new TestOrder { Text = "order", CreatedAtUtc = Now });

        // Act
        await HostOf(scope).SaveChangesAsync(Ct);

        // Assert
        _ = _signal.DidNotReceive().NotifyAsync(Arg.Any<DispatchTopic>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveChangesAsync_SaveThatFails_NotifiesNothing()
    {
        // Arrange
        await using var provider = await BuildWithHostContextAsync(signal: _signal);
        var message = TestMessages.AReceivedMessage(new TestEvent("text"));
        await provider.GetRequiredService<EntityFrameworkInboxStore>().For(TenantKey.None).AddAsync(message, Ct);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IInboxStore>().AddAsync(message, Ct);

        // Act
        var exception = await Record.ExceptionAsync(() => HostOf(scope).SaveChangesAsync(Ct));

        // Assert
        Assert.NotNull(exception);
        _ = _signal.DidNotReceive().NotifyAsync(Arg.Any<DispatchTopic>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveChangesAsync_SaveWithNoInboxRowAfterAFailedSaveWithAnInboxRow_NotifiesNothing()
    {
        // Arrange
        await using var provider = await BuildWithHostContextAsync(signal: _signal);
        var message = TestMessages.AReceivedMessage(new TestEvent("text"));
        await provider.GetRequiredService<EntityFrameworkInboxStore>().For(TenantKey.None).AddAsync(message, Ct);
        await using var scope = provider.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IInboxStore>().AddAsync(message, Ct);
        await Record.ExceptionAsync(() => HostOf(scope).SaveChangesAsync(Ct));
        HostOf(scope).ChangeTracker.Clear();
        HostOf(scope).Orders.Add(new TestOrder { Text = "order", CreatedAtUtc = Now });

        // Act
        await HostOf(scope).SaveChangesAsync(Ct);

        // Assert
        _ = _signal.DidNotReceive().NotifyAsync(Arg.Any<DispatchTopic>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CommitAsync_TransactionAfterARollbackWithAnInboxRow_NotifiesNothing()
    {
        // Arrange
        await using var provider = await BuildWithHostContextAsync(signal: _signal);
        await using var scope = provider.CreateAsyncScope();
        await using (var rolledBack = await HostOf(scope).Database.BeginTransactionAsync(Ct))
        {
            await StageAsync(scope);
            await HostOf(scope).SaveChangesAsync(Ct);
            await rolledBack.RollbackAsync(Ct);
        }

        HostOf(scope).ChangeTracker.Clear();
        await using var transaction = await HostOf(scope).Database.BeginTransactionAsync(Ct);
        HostOf(scope).Orders.Add(new TestOrder { Text = "order", CreatedAtUtc = Now });
        await HostOf(scope).SaveChangesAsync(Ct);

        // Act
        await transaction.CommitAsync(Ct);

        // Assert
        _ = _signal.DidNotReceive().NotifyAsync(Arg.Any<DispatchTopic>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Commit_TransactionAfterARollbackWithAnInboxRow_NotifiesNothing()
    {
        // Arrange
        await using var provider = await BuildWithHostContextAsync(signal: _signal);
        await using var scope = provider.CreateAsyncScope();
        using (var rolledBack = HostOf(scope).Database.BeginTransaction())
        {
            await StageAsync(scope);
            HostOf(scope).SaveChanges();
            rolledBack.Rollback();
        }

        HostOf(scope).ChangeTracker.Clear();
        using var transaction = HostOf(scope).Database.BeginTransaction();
        HostOf(scope).Orders.Add(new TestOrder { Text = "order", CreatedAtUtc = Now });
        HostOf(scope).SaveChanges();

        // Act
        transaction.Commit();

        // Assert
        _ = _signal.DidNotReceive().NotifyAsync(Arg.Any<DispatchTopic>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveChangesAsync_NotifyError_SavesTheRowAndLogsAWarningWithItsEventId()
    {
        // Arrange
        _signal.NotifyAsync(DispatchTopic.Inbox, Ct).Returns(ValueTask.FromResult<Error?>(new Error(Errors.Unavailable)));
        await using var provider = await BuildWithHostContextAsync(signal: _signal);
        await using var scope = provider.CreateAsyncScope();
        await StageAsync(scope);

        // Act
        await HostOf(scope).SaveChangesAsync(Ct);

        // Assert
        Assert.Equal([["1"]], _database.Rows("SELECT COUNT(*) FROM Inbox"));
        Assert.Equal(
            [(2700, LogLevel.Warning)],
            provider.LogRecords().Where(static record => record.Id.Id == 2700).Select(static record => (record.Id.Id, record.Level)));
    }

    private static TestHostContext HostOf(AsyncServiceScope scope) => scope.ServiceProvider.GetRequiredService<TestHostContext>();

    private static async Task StageAsync(AsyncServiceScope scope) =>
        await scope.ServiceProvider.GetRequiredService<IInboxStore>().AddAsync(TestMessages.AReceivedMessage(new TestEvent("text")), Ct);
}
