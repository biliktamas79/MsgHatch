using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Tests.Unit.Identity;

public sealed class TenantKeyUnitTests
{
    [Fact]
    public void For_NullTenant_IsEqualToNone()
    {
        // Act
        var key = TenantKey.For(null);

        // Assert
        Assert.Equal(TenantKey.None, key);
        Assert.Null(key.Tenant);
    }

    [Fact]
    public void For_EqualTenants_GivesEqualKeys()
    {
        // Arrange
        var first = TenantKey.For(new TenantId("t1"));

        // Act
        var second = TenantKey.For(new TenantId("t1"));

        // Assert
        Assert.Equal(first, second);
        Assert.Equal(first.GetHashCode(), second.GetHashCode());
    }

    [Fact]
    public void For_TwoTenants_GivesKeysThatAreNotEqual()
    {
        // Arrange
        var first = TenantKey.For(new TenantId("t1"));

        // Act
        var second = TenantKey.For(new TenantId("t2"));

        // Assert
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void For_Tenant_IsNotEqualToNone()
    {
        // Act
        var key = TenantKey.For(new TenantId("t1"));

        // Assert
        Assert.NotEqual(TenantKey.None, key);
    }
}
