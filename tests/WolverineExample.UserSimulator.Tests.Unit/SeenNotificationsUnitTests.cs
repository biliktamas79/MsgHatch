namespace WolverineExample.UserSimulator.Tests.Unit;

/// <summary>The notifications that one simulated user already got: each one counts once, and the last new one is the cursor of the catch-up.</summary>
public sealed class SeenNotificationsUnitTests
{
    private static readonly Guid _first = Guid.Parse("00000000-0000-0000-0000-000000000001");
    private static readonly Guid _second = Guid.Parse("00000000-0000-0000-0000-000000000002");

    [Fact]
    public void TryAdd_IdThatTheUserAlreadyGot_GivesFalse()
    {
        // Arrange
        var seen = new SeenNotifications();
        seen.TryAdd(_first);

        // Act
        var added = seen.TryAdd(_first);

        // Assert
        Assert.False(added);
    }

    [Fact]
    public void TryAdd_NewIdAndThenAnOldId_KeepsTheNewIdAsTheLastId()
    {
        // Arrange
        var seen = new SeenNotifications();
        seen.TryAdd(_first);
        seen.TryAdd(_second);

        // Act
        seen.TryAdd(_first);

        // Assert
        Assert.Equal(_second, seen.LastId);
    }

    [Fact]
    public void LastId_NoNotification_GivesNull()
    {
        // Arrange
        var seen = new SeenNotifications();

        // Act
        var last = seen.LastId;

        // Assert
        Assert.Null(last);
    }
}
