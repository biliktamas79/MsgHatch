using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class OutboxStoreTestsBase<TStore>
{
    [Theory]
    [InlineData(ChannelDownReason.Errors)]
    [InlineData(ChannelDownReason.Operator)]
    public async Task SetChannelDownAsync_UpChannel_AddsADownRowWithTheVersion1AndNoPreviousDuration(ChannelDownReason reason)
    {
        // Arrange
        var down = ADown(reason);

        // Act
        var added = await Store.SetChannelDownAsync(down, Ct);

        // Assert
        Assert.True(added.Value);
        Assert.Equal(
            [new ChannelState(TestChannel.Id, 1, ChannelStateKind.Down, Now, null, null, reason, down.NextCheckAtUtc, down.ErrorCode, down.ErrorName, down.Note)],
            await RowsAsync());
    }

    [Theory]
    [InlineData(ChannelDownReason.Errors)]
    [InlineData(ChannelDownReason.Operator)]
    public async Task SetChannelDownAsync_ReasonErrorsOnADownChannel_AddsNoRow(ChannelDownReason first)
    {
        // Arrange
        await Store.SetChannelDownAsync(ADown(first), Ct);
        Time.Advance(TimeSpan.FromMinutes(1));

        // Act
        var added = await Store.SetChannelDownAsync(ADown(ChannelDownReason.Errors), Ct);

        // Assert
        Assert.Equal((false, "1"), (added.Value, await VersionsAsync()));
    }

    [Fact]
    public async Task SetChannelDownAsync_PauseOnAPausedChannel_AddsNoRowAndKeepsTheFirstNote()
    {
        // Arrange
        await Store.SetChannelDownAsync(ADown(ChannelDownReason.Operator, note: "first"), Ct);
        Time.Advance(TimeSpan.FromMinutes(1));

        // Act
        var added = await Store.SetChannelDownAsync(ADown(ChannelDownReason.Operator, note: "second"), Ct);

        // Assert
        Assert.False(added.Value);
        Assert.Equal("first", Assert.Single(await RowsAsync()).Note);
    }

    [Fact]
    public async Task SetChannelDownAsync_PauseAfterReasonErrors_AddsASecondDownRowWithTheDurationOfTheFirst()
    {
        // Arrange
        var errors = ADown(ChannelDownReason.Errors);
        await Store.SetChannelDownAsync(errors, Ct);
        Time.Advance(TimeSpan.FromMinutes(1));
        var paused = ADown(ChannelDownReason.Operator, note: "Ősz-űr \"quoted\" <tag> & more");

        // Act
        await Store.SetChannelDownAsync(paused, Ct);

        // Assert
        Assert.Equal(
            [
                new ChannelState(
                    TestChannel.Id, 2, ChannelStateKind.Down, Now, ChannelStateKind.Down, TimeSpan.FromMinutes(1), ChannelDownReason.Operator, null, null, null,
                    paused.Note),
                new ChannelState(
                    TestChannel.Id, 1, ChannelStateKind.Down, Now.AddMinutes(-1), null, null, ChannelDownReason.Errors, errors.NextCheckAtUtc, 503, "Unavailable",
                    null),
            ],
            await RowsAsync());
    }

    [Theory]
    [InlineData(ChannelDownReason.Errors)]
    [InlineData(ChannelDownReason.Operator)]
    public async Task SetChannelUpAsync_DownChannel_AddsAnUpRowWithTheNextVersionAndTheDurationOfTheDownState(ChannelDownReason reason)
    {
        // Arrange
        await Store.SetChannelDownAsync(ADown(reason), Ct);
        var downDuration = TimeSpan.FromMinutes(90) + TimeSpan.FromMilliseconds(250);
        Time.Advance(downDuration);

        // Act
        var added = await Store.SetChannelUpAsync(TestChannel.Id, reason: null, Now, Ct);

        // Assert
        Assert.True(added.Value);
        Assert.Equal(
            new ChannelState(TestChannel.Id, 2, ChannelStateKind.Up, Now, ChannelStateKind.Down, downDuration, null, null, null, null, null),
            (await RowsAsync())[0]);
        Assert.Empty((await Store.GetDownChannelsAsync(Ct)).Value);
    }

    [Fact]
    public async Task SetChannelUpAsync_DurationWithAPartOfAMillisecond_KeepsTheWholeMilliseconds()
    {
        // Arrange
        await Store.SetChannelDownAsync(ADown(ChannelDownReason.Operator), Ct);
        Time.Advance(TimeSpan.FromTicks(10_007_000));

        // Act
        await Store.SetChannelUpAsync(TestChannel.Id, reason: null, Now, Ct);

        // Assert
        Assert.Equal(TimeSpan.FromMilliseconds(1000), (await RowsAsync())[0].PreviousDuration);
    }

    [Fact]
    public async Task SetChannelUpAsync_UpChannel_AddsNoRow()
    {
        // Act
        var added = await Store.SetChannelUpAsync(TestChannel.Id, reason: null, Now, Ct);

        // Assert
        Assert.False(added.Value);
        Assert.Empty(await RowsAsync());
    }

    [Fact]
    public async Task SetChannelUpAsync_ReasonErrorsOnAPauseOfAPerson_AddsNoRow()
    {
        // Arrange
        await Store.SetChannelDownAsync(ADown(ChannelDownReason.Operator), Ct);

        // Act
        await Store.SetChannelUpAsync(TestChannel.Id, ChannelDownReason.Errors, Now, Ct);

        // Assert
        Assert.Equal(ChannelStateKind.Down, Assert.Single(await RowsAsync()).Kind);
    }

    [Fact]
    public async Task SetChannelDownAsync_DownAfterUp_AddsTheThirdRowWhichIsTheCurrentState()
    {
        // Arrange
        await Store.SetChannelDownAsync(ADown(ChannelDownReason.Operator), Ct);
        Time.Advance(TimeSpan.FromMinutes(1));
        await Store.SetChannelUpAsync(TestChannel.Id, reason: null, Now, Ct);
        Time.Advance(TimeSpan.FromMinutes(2));
        var down = ADown(ChannelDownReason.Errors);

        // Act
        await Store.SetChannelDownAsync(down, Ct);

        // Assert
        Assert.Equal(
            ("3,2,1", ChannelStateKind.Up, TimeSpan.FromMinutes(2)),
            (await VersionsAsync(), (await RowsAsync())[0].PreviousKind!.Value, (await RowsAsync())[0].PreviousDuration!.Value));
        Assert.Equal([down], (await Store.GetDownChannelsAsync(Ct)).Value);
    }

    [Fact]
    public async Task GetChannelStatesAsync_FromTime_GivesTheRowsAtOrAfterItAndTheRowOfEachChannelThatWasCurrentAtItNewestFirst()
    {
        // Arrange
        var startUtc = Now;
        await Store.SetChannelDownAsync(ADown(ChannelDownReason.Operator), Ct);
        await Store.SetChannelUpAsync(TestChannel.Id, reason: null, startUtc.AddMinutes(10), Ct);
        await Store.SetChannelDownAsync(ADown(ChannelDownReason.Operator) with { SinceUtc = startUtc.AddMinutes(20) }, Ct);
        await Store.SetChannelUpAsync(TestChannel.Id, reason: null, startUtc.AddHours(1), Ct);
        await Store.SetChannelDownAsync(ADown(ChannelDownReason.Operator, TestChannel.SecondId) with { SinceUtc = startUtc.AddMinutes(30) }, Ct);

        // Act
        var rows = await Store.GetChannelStatesAsync(startUtc.AddMinutes(45), Ct);

        // Assert
        Assert.Equal(
            [(TestChannel.Id, 4), (TestChannel.SecondId, 1), (TestChannel.Id, 3)],
            rows.Value.Select(static row => (row.Channel, row.Version)));
    }

    [Fact]
    public async Task GetChannelStatesAsync_TwoRowsOfAChannelAtTheSameTime_GivesTheHigherVersionFirst()
    {
        // Arrange
        await Store.SetChannelDownAsync(ADown(ChannelDownReason.Operator), Ct);
        await Store.SetChannelUpAsync(TestChannel.Id, reason: null, Now, Ct);

        // Act
        var rows = await RowsAsync();

        // Assert
        Assert.Equal(
            [(2, ChannelStateKind.Up, (ChannelStateKind?)ChannelStateKind.Down, TimeSpan.Zero), (1, ChannelStateKind.Down, null, (TimeSpan?)null)],
            rows.Select(static row => (row.Version, row.Kind, row.PreviousKind, row.PreviousDuration)));
    }

    [Fact]
    public async Task GetChannelStatesAsync_RowsOfTwoChannelsAtTheSameTime_GivesThemInTheOrdinalOrderOfTheChannelNames()
    {
        // Arrange
        await Store.SetChannelDownAsync(ADown(ChannelDownReason.Operator), Ct);
        await Store.SetChannelDownAsync(ADown(ChannelDownReason.Operator, TestChannel.SecondId), Ct);

        // Act
        var rows = await RowsAsync();

        // Assert
        Assert.Equal([TestChannel.SecondId, TestChannel.Id], rows.Select(static row => row.Channel));
    }

    [Fact]
    public async Task GetChannelStatesAsync_RowsOlderThanTheHistoryWindow_KeepsEachRow()
    {
        // Arrange
        await Store.SetChannelDownAsync(ADown(ChannelDownReason.Operator), Ct);
        Time.Advance(TimeSpan.FromDays(40));
        await Store.SetChannelUpAsync(TestChannel.Id, reason: null, Now, Ct);
        Time.Advance(TimeSpan.FromDays(40));

        // Act
        await Store.SetChannelDownAsync(ADown(ChannelDownReason.Errors), Ct);

        // Assert
        Assert.Equal("3,2,1", await VersionsAsync());
    }

    [Fact]
    public async Task TryStartChannelCheckAsync_DueCheck_UpdatesTheNextCheckOfTheLatestRowAndKeepsItsVersion()
    {
        // Arrange
        await Store.SetChannelDownAsync(ADown(ChannelDownReason.Errors), Ct);
        Time.Advance(TimeSpan.FromSeconds(30));
        var nextCheckAtUtc = Now.AddSeconds(30);

        // Act
        await Store.TryStartChannelCheckAsync(TestChannel.Id, Now, nextCheckAtUtc, Ct);

        // Assert
        var row = Assert.Single(await RowsAsync());
        Assert.Equal((1, nextCheckAtUtc), (row.Version, row.NextCheckAtUtc));
    }

    [Fact]
    public async Task SetChannelDownAsync_TwoParallelCallsWithTheReasonErrors_AddOneRowWithNoError()
    {
        // Arrange
        var down = ADown(ChannelDownReason.Errors);

        // Act
        var added = await Task.WhenAll(
            Task.Run(() => Store.SetChannelDownAsync(down, Ct).AsTask(), Ct),
            Task.Run(() => Store.SetChannelDownAsync(down, Ct).AsTask(), Ct));

        // Assert
        Assert.Equal((1, 0, "1"), (added.Count(static result => result.Value), added.Count(static result => result.IsError), await VersionsAsync()));
    }

    [Fact]
    public async Task SetChannelDownAsync_PauseAndErrorsInParallelOnAnUpChannel_KeepThePause()
    {
        // Arrange
        const int rounds = 50;

        // Act
        var outcomes = await RunPauseAndErrorsRoundsAsync(rounds);

        // Assert
        Assert.Equal(Enumerable.Repeat((PauseKept: true, Errors: 0), rounds), outcomes);
    }

    private async Task<List<(bool PauseKept, int Errors)>> RunPauseAndErrorsRoundsAsync(int rounds)
    {
        using var start = new Barrier(2);
        var outcomes = new List<(bool PauseKept, int Errors)>();
        for (var round = 0; round < rounds; round++)
        {
            var added = await Task.WhenAll(
                SetChannelDownWith(start, ADown(ChannelDownReason.Operator)),
                SetChannelDownWith(start, ADown(ChannelDownReason.Errors)));
            var down = (await Store.GetDownChannelsAsync(Ct)).Value;
            outcomes.Add((down is [{ Reason: ChannelDownReason.Operator }], added.Count(static result => result.IsError)));
            await Store.SetChannelUpAsync(TestChannel.Id, reason: null, Now, Ct);
        }

        return outcomes;
    }

    private Task<ErrorOr<bool>> SetChannelDownWith(Barrier start, ChannelDown down) =>
        Task.Run(
            () =>
            {
                start.SignalAndWait(Ct);
                return Store.SetChannelDownAsync(down, Ct).AsTask();
            },
            Ct);

    [Fact]
    public async Task SetChannelUpAsync_TwoParallelCalls_AddOneUpRowWithNoError()
    {
        // Arrange
        await Store.SetChannelDownAsync(ADown(ChannelDownReason.Errors), Ct);
        var upAtUtc = Now.AddMinutes(1);

        // Act
        var added = await Task.WhenAll(
            Task.Run(() => Store.SetChannelUpAsync(TestChannel.Id, reason: null, upAtUtc, Ct).AsTask(), Ct),
            Task.Run(() => Store.SetChannelUpAsync(TestChannel.Id, reason: null, upAtUtc, Ct).AsTask(), Ct));

        // Assert
        Assert.Equal((1, 0, "2,1"), (added.Count(static result => result.Value), added.Count(static result => result.IsError), await VersionsAsync()));
    }

    private async Task<IReadOnlyList<ChannelState>> RowsAsync() => (await Store.GetChannelStatesAsync(DateTime.MinValue, Ct)).Value;

    private async Task<string> VersionsAsync() => string.Join(',', (await RowsAsync()).Select(static row => row.Version));
}
