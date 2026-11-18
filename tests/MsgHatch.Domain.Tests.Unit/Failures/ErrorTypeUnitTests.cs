using MsgHatch.Domain.Failures;

namespace MsgHatch.Domain.Tests.Unit.Failures;

/// <summary>The identity of an error type: its code (D238).</summary>
public sealed class ErrorTypeUnitTests
{
    [Fact]
    public void Equals_OneCodeAndOtherNamesAndFlags_GivesTrueAndOneHash()
    {
        // Arrange
        var local = new ErrorType(600, "OrderLocked", IsTransient: false);
        var remote = new ErrorType(600, "OrderIsLocked", IsTransient: true);

        // Act
        var equal = local == remote && local.Equals(remote) && Equals(local, remote);

        // Assert
        Assert.Equal((true, local.GetHashCode()), (equal, remote.GetHashCode()));
    }

    [Fact]
    public void Equals_OtherCodeAndOneName_GivesFalse()
    {
        // Arrange
        var first = new ErrorType(600, "OrderLocked", IsTransient: false);
        var second = new ErrorType(601, "OrderLocked", IsTransient: false);

        // Act
        var equal = first == second || first.Equals(second);

        // Assert
        Assert.False(equal);
    }

    [Fact]
    public void Equals_Null_GivesFalse()
    {
        // Act
        var equal = Errors.NotFound.Equals(null);

        // Assert
        Assert.False(equal);
    }

    [Fact]
    public void Equals_TypeOfTheWireWithAKnownCode_EqualsTheTypeOfErrors()
    {
        // Arrange
        var fromTheWire = new ErrorType(404, "NotFoundOfAnOldVersion", IsTransient: true);

        // Act
        var equal = fromTheWire == Errors.NotFound;

        // Assert
        Assert.True(equal);
    }
}
