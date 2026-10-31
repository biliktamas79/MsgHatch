using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Tests.Unit.Identity;

public sealed class ChannelIdUnitTests
{
    [Fact]
    public void Equals_SameName_AreEqual()
    {
        // Arrange
        var first = new ChannelId("Email");
        var second = new ChannelId("Email");

        // Act
        var equal = first == second;

        // Assert
        Assert.True(equal);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_DifferentNames_AreNotEqual()
    {
        // Arrange
        var first = new ChannelId("Email");
        var second = new ChannelId("SignalR");

        // Act
        var equal = first == second;

        // Assert
        Assert.False(equal);
    }

    [Theory]
    [MemberData(nameof(MessageTypeUnitTests.InvalidNames), MemberType = typeof(MessageTypeUnitTests))]
    public void Constructor_InvalidName_ThrowsArgumentException(string? name)
    {
        // Act
        var exception = Record.Exception(() => new ChannelId(name!));

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
    }
}
