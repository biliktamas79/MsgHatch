using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Dispatch;

/// <summary>The change of the insert-only state rows of a channel when other callers add the same version first (D277).</summary>
public sealed class ChannelStateUnitTests
{
    private static readonly DateTime _nowUtc = new(2030, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private static readonly ChannelDown _down =
        new(TestChannel.Id, ChannelDownReason.Errors, _nowUtc, _nowUtc.AddSeconds(30), 503, "Unavailable", Note: null);

    private static readonly ChannelState _otherDown = ChannelState.AfterDown(null, _down with { ErrorCode = 429 })!;

    [Fact]
    public void Change_InsertThatLosesToTheSameChange_ReadsTheLatestRowAgainAndAddsNoRow()
    {
        // Arrange
        var reads = new Queue<ChannelState?>([null, _otherDown]);

        // Act
        var changed = ChannelState.Change(reads.Dequeue, latest => ChannelState.AfterDown(latest, _down), static _ => false);

        // Assert
        Assert.Equal((false, 0), (changed.Value, reads.Count));
    }

    [Fact]
    public void Change_InsertThatLosesOneTry_AddsTheRowAfterTheNewLatestRow()
    {
        // Arrange
        var reads = new Queue<ChannelState?>([null, _otherDown]);
        var inserted = new List<ChannelState>();

        // Act
        var changed = ChannelState.Change(
            reads.Dequeue, latest => ChannelState.AfterUp(latest, TestChannel.Id, reason: null, _nowUtc.AddMinutes(1)) ?? ChannelState.AfterDown(latest, _down),
            row =>
            {
                inserted.Add(row);
                return inserted.Count > 1;
            });

        // Assert
        Assert.Equal((true, "1,2"), (changed.Value, string.Join(',', inserted.Select(static row => row.Version))));
    }

    [Fact]
    public void Change_InsertThatLosesEachTry_GivesConflictAfterThreeTries()
    {
        // Arrange
        var reads = 0;

        // Act
        var changed = ChannelState.Change(() => ++reads > 0 ? null : null, latest => ChannelState.AfterDown(latest, _down), static _ => false);

        // Assert
        Assert.Equal((Errors.Conflict, ChannelState.ChangeTries), (changed.Error?.Type, reads));
    }

    [Fact]
    public async Task ChangeAsync_InsertThatLosesEachTry_GivesConflictAfterThreeTries()
    {
        // Arrange
        var reads = 0;

        // Act
        var changed = await ChannelState.ChangeAsync(
            () =>
            {
                reads++;
                return ValueTask.FromResult<ChannelState?>(null);
            },
            latest => ChannelState.AfterDown(latest, _down), static _ => ValueTask.FromResult(false));

        // Assert
        Assert.Equal((Errors.Conflict, ChannelState.ChangeTries), (changed.Error?.Type, reads));
    }

    [Fact]
    public async Task ChangeAsync_InsertThatLosesToTheSameChange_AddsNoRow()
    {
        // Arrange
        var reads = new Queue<ChannelState?>([null, _otherDown]);

        // Act
        var changed = await ChannelState.ChangeAsync(
            () => ValueTask.FromResult(reads.Dequeue()), latest => ChannelState.AfterDown(latest, _down), static _ => ValueTask.FromResult(false));

        // Assert
        Assert.Equal((false, 0), (changed.Value, reads.Count));
    }

    [Fact]
    public async Task ChangeAsync_InsertThatLosesOneTry_AddsTheRowAfterTheNewLatestRow()
    {
        // Arrange
        var reads = new Queue<ChannelState?>([null, _otherDown]);
        var inserted = new List<ChannelState>();

        // Act
        var changed = await ChannelState.ChangeAsync(
            () => ValueTask.FromResult(reads.Dequeue()),
            latest => ChannelState.AfterUp(latest, TestChannel.Id, reason: null, _nowUtc.AddMinutes(1)) ?? ChannelState.AfterDown(latest, _down),
            row =>
            {
                inserted.Add(row);
                return ValueTask.FromResult(inserted.Count > 1);
            });

        // Assert
        Assert.Equal((true, "1,2"), (changed.Value, string.Join(',', inserted.Select(static row => row.Version))));
    }

    [Fact]
    public void AfterUp_ClockOfAnotherInstanceBeforeTheDownState_GivesTheDuration0()
    {
        // Act
        var up = ChannelState.AfterUp(_otherDown, TestChannel.Id, reason: null, _nowUtc.AddSeconds(-2));

        // Assert
        Assert.Equal(TimeSpan.Zero, up?.PreviousDuration);
    }

    [Fact]
    public void ToDown_UpRow_GivesNull()
    {
        // Act
        var down = ChannelState.AfterUp(_otherDown, TestChannel.Id, reason: null, _nowUtc)!.ToDown();

        // Assert
        Assert.Null(down);
    }
}
