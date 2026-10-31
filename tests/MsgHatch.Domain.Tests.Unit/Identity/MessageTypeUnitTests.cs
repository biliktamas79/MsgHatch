using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Tests.Unit.Identity;

public sealed class MessageTypeUnitTests
{
    public static TheoryData<string> ValidNames => ["OrderShipped", "Order_Shipped2", "A"];

    public static TheoryData<string?> InvalidNames => new()
    {
        (string?)null,
        "",
        " ",
        "2Order",
        "_Order",
        "Order-Shipped",
        "Order Shipped",
        "Order:Shipped",
        "Ördér",
    };

    [Fact]
    public void Equals_SameName_AreEqual()
    {
        // Arrange
        var first = new MessageType("OrderShipped");
        var second = new MessageType("OrderShipped");

        // Act
        var equal = first == second;

        // Assert
        Assert.True(equal);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void Equals_NamesThatDifferOnlyInCase_AreNotEqual()
    {
        // Arrange
        var first = new MessageType("OrderShipped");
        var second = new MessageType("ordershipped");

        // Act
        var equal = first == second;

        // Assert
        Assert.False(equal);
    }

    [Theory]
    [MemberData(nameof(ValidNames))]
    public void Constructor_ValidName_KeepsTheName(string name)
    {
        // Act
        var type = new MessageType(name);

        // Assert
        Assert.Equal(name, type.Name);
    }

    [Theory]
    [MemberData(nameof(InvalidNames))]
    public void Constructor_InvalidName_ThrowsArgumentException(string? name)
    {
        // Act
        var exception = Record.Exception(() => new MessageType(name!));

        // Assert
        Assert.IsAssignableFrom<ArgumentException>(exception);
    }
}
