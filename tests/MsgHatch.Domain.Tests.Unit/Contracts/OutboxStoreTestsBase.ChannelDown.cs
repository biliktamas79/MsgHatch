using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Tests.Unit.TestTypes;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

public abstract partial class OutboxStoreTestsBase<TStore>
{
    [Fact]
    public async Task SetChannelDownAsync_ReasonErrorsTwoTimes_WritesOneTimeAndKeepsTheFirstState()
    {
        // Arrange
        var first = ADown(ChannelDownReason.Errors, errorCode: 429);
        var second = ADown(ChannelDownReason.Errors, errorCode: 503);

        // Act
        var wrote = (await Store.SetChannelDownAsync(first, Ct)).Value;
        var wroteAgain = (await Store.SetChannelDownAsync(second, Ct)).Value;

        // Assert
        Assert.Equal((true, false), (wrote, wroteAgain));
        Assert.Equal([first], (await Store.GetDownChannelsAsync(Ct)).Value);
    }

    [Fact]
    public async Task SetChannelDownAsync_ReasonOperatorAfterReasonErrors_ReplacesTheState()
    {
        // Arrange
        await Store.SetChannelDownAsync(ADown(ChannelDownReason.Errors), Ct);
        var paused = ADown(ChannelDownReason.Operator, note: "Ősz-űr \"quoted\" <tag> & more");

        // Act
        var wrote = await Store.SetChannelDownAsync(paused, Ct);

        // Assert
        Assert.True(wrote.Value);
        Assert.Equal([paused], (await Store.GetDownChannelsAsync(Ct)).Value);
    }

    [Fact]
    public async Task SetChannelUpAsync_ReasonErrorsOnAPauseOfAPerson_KeepsThePause()
    {
        // Arrange
        var paused = ADown(ChannelDownReason.Operator);
        await Store.SetChannelDownAsync(paused, Ct);

        // Act
        var ended = await Store.SetChannelUpAsync(TestChannel.Id, ChannelDownReason.Errors, Ct);

        // Assert
        Assert.False(ended.Value);
        Assert.Equal([paused], (await Store.GetDownChannelsAsync(Ct)).Value);
    }

    [Theory]
    [InlineData(ChannelDownReason.Errors)]
    [InlineData(ChannelDownReason.Operator)]
    public async Task SetChannelUpAsync_NoReason_EndsEachReason(ChannelDownReason reason)
    {
        // Arrange
        await Store.SetChannelDownAsync(ADown(reason), Ct);

        // Act
        var ended = await Store.SetChannelUpAsync(TestChannel.Id, reason: null, Ct);

        // Assert
        Assert.True(ended.Value);
        Assert.Empty((await Store.GetDownChannelsAsync(Ct)).Value);
    }

    [Fact]
    public async Task SetChannelUpAsync_ChannelThatIsUp_ReturnsFalse()
    {
        // Act
        var ended = await Store.SetChannelUpAsync(TestChannel.Id, reason: null, Ct);

        // Assert
        Assert.False(ended.Value);
    }

    [Fact]
    public async Task GetDownChannelsAsync_TwoDownChannels_ReturnsTheStateOfEach()
    {
        // Arrange
        var first = ADown(ChannelDownReason.Errors);
        var second = ADown(ChannelDownReason.Operator, TestChannel.SecondId);
        await Store.SetChannelDownAsync(first, Ct);
        await Store.SetChannelDownAsync(second, Ct);

        // Act
        var down = await Store.GetDownChannelsAsync(Ct);

        // Assert
        Assert.Equal([first, second], down.Value.OrderByDescending(static d => d.Channel.Name, StringComparer.Ordinal));
    }

    [Theory]
    [InlineData(29, ChannelDownReason.Errors, false)]
    [InlineData(30, ChannelDownReason.Errors, true)]
    [InlineData(31, ChannelDownReason.Errors, true)]
    [InlineData(31, ChannelDownReason.Operator, false)]
    public async Task TryStartChannelCheckAsync_DownChannel_StartsTheCheckOnlyWhenItIsDueForTheReasonErrors(
        int secondsLater, ChannelDownReason reason, bool expected)
    {
        // Arrange
        await Store.SetChannelDownAsync(ADown(reason), Ct);
        Time.Advance(TimeSpan.FromSeconds(secondsLater));
        var nextCheckAtUtc = Now.AddSeconds(30);

        // Act
        var started = await Store.TryStartChannelCheckAsync(TestChannel.Id, Now, nextCheckAtUtc, Ct);

        // Assert
        Assert.Equal(expected, started.Value);
    }

    [Fact]
    public async Task TryStartChannelCheckAsync_DueCheck_MovesTheNextCheckTime()
    {
        // Arrange
        await Store.SetChannelDownAsync(ADown(ChannelDownReason.Errors), Ct);
        Time.Advance(TimeSpan.FromSeconds(30));
        var nextCheckAtUtc = Now.AddSeconds(30);

        // Act
        await Store.TryStartChannelCheckAsync(TestChannel.Id, Now, nextCheckAtUtc, Ct);

        // Assert
        Assert.Equal(nextCheckAtUtc, Assert.Single((await Store.GetDownChannelsAsync(Ct)).Value).NextCheckAtUtc);
    }

    [Fact]
    public async Task TryStartChannelCheckAsync_ChannelThatIsUp_ReturnsFalse()
    {
        // Act
        var started = await Store.TryStartChannelCheckAsync(TestChannel.Id, Now, Now.AddSeconds(30), Ct);

        // Assert
        Assert.False(started.Value);
    }

    [Fact]
    public async Task TryStartChannelCheckAsync_TwoParallelCalls_ExactlyOneReturnsTrue()
    {
        // Arrange
        await Store.SetChannelDownAsync(ADown(ChannelDownReason.Errors), Ct);
        Time.Advance(TimeSpan.FromSeconds(30));
        var (nowUtc, nextCheckAtUtc) = (Now, Now.AddSeconds(30));

        // Act
        var started = await Task.WhenAll(
            Task.Run(() => Store.TryStartChannelCheckAsync(TestChannel.Id, nowUtc, nextCheckAtUtc, Ct).AsTask(), Ct),
            Task.Run(() => Store.TryStartChannelCheckAsync(TestChannel.Id, nowUtc, nextCheckAtUtc, Ct).AsTask(), Ct));

        // Assert
        Assert.Equal(1, started.Count(static result => result.Value));
    }

    [Fact]
    public async Task ClaimDueAsync_DownChannel_ReturnsNoMessageAndTheDownState()
    {
        // Arrange
        await AddAsync(AMessage(T1));
        var down = ADown(ChannelDownReason.Operator);
        await Store.SetChannelDownAsync(down, Ct);

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal((0, down), (result.Value.Messages.Count, result.Value.Down));
    }

    [Fact]
    public async Task ClaimDueAsync_AnotherChannelIsDown_ClaimsTheMessageWithNoDownState()
    {
        // Arrange
        await AddAsync(AMessage(T1));
        await Store.SetChannelDownAsync(ADown(ChannelDownReason.Operator, TestChannel.SecondId), Ct);

        // Act
        var result = await ClaimAsync();

        // Assert
        Assert.Equal((1, (ChannelDown?)null), (result.Value.Messages.Count, result.Value.Down));
    }

    private ChannelDown ADown(ChannelDownReason reason, ChannelId? channel = null, int? errorCode = null, string? note = null) =>
        reason == ChannelDownReason.Errors
            ? new ChannelDown(channel ?? TestChannel.Id, reason, Now, Now.AddSeconds(30), errorCode ?? 503, "Unavailable", Note: null)
            : new ChannelDown(channel ?? TestChannel.Id, reason, Now, NextCheckAtUtc: null, ErrorCode: null, ErrorName: null, note ?? "maintenance");
}
