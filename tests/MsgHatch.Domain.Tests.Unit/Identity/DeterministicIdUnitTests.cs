using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;

namespace MsgHatch.Domain.Tests.Unit.Identity;

public sealed class DeterministicIdUnitTests
{
    private static readonly Guid _messageId = Guid.Parse("0b6c1f7e-2a44-4d8e-9a3b-61c0f5e2d7a9");
    private static readonly DeliveryTarget _user1 = new(DeliveryTargetKind.User, "1");
    private static readonly ChannelId _email = new("Email");

    [Fact]
    public void Create_GoldenCaseWithNoTenant_GivesTheFixedValue()
    {
        // Act
        var id = DeterministicId.Create(_messageId, null, _user1, _email);

        // Assert
        Assert.Equal(Guid.Parse("e7efe368-e947-5e2a-bd19-f3f4a4f3f1ed"), id);
    }

    [Fact]
    public void Create_GoldenCaseWithATenant_GivesTheFixedValue()
    {
        // Act
        var id = DeterministicId.Create(_messageId, new TenantId("t1"), _user1, _email);

        // Assert
        Assert.Equal(Guid.Parse("2e025f09-6278-543d-923e-71807d399fe9"), id);
    }

    [Fact]
    public void Create_SameInputs_GivesTheSameValue()
    {
        // Arrange
        var tenant = new TenantId("t1");
        var first = DeterministicId.Create(_messageId, tenant, _user1, _email);

        // Act
        var second = DeterministicId.Create(_messageId, tenant, _user1, _email);

        // Assert
        Assert.Equal(first, second);
    }

    [Fact]
    public void Create_AnyInput_GivesAUuidOfVersion5AndTheRfcVariant()
    {
        // Act
        var id = DeterministicId.Create(_messageId, null, _user1, _email);

        // Assert
        Assert.Equal(5, id.Version);
        Assert.Equal(0b1000, id.Variant & 0b1100);
    }

    [Fact]
    public void Create_DifferentChannel_GivesADifferentValue()
    {
        // Arrange
        var email = DeterministicId.Create(_messageId, null, _user1, _email);

        // Act
        var signalR = DeterministicId.Create(_messageId, null, _user1, new ChannelId("SignalR"));

        // Assert
        Assert.NotEqual(email, signalR);
    }

    [Fact]
    public void Create_TenantAndNoTenant_GiveDifferentValues()
    {
        // Arrange
        var noTenant = DeterministicId.Create(_messageId, null, _user1, _email);

        // Act
        var tenant = DeterministicId.Create(_messageId, new TenantId("t1"), _user1, _email);

        // Assert
        Assert.NotEqual(noTenant, tenant);
    }

    [Fact]
    public void Create_TwoTenants_GiveDifferentValues()
    {
        // Arrange
        var first = DeterministicId.Create(_messageId, new TenantId("t1"), _user1, _email);

        // Act
        var second = DeterministicId.Create(_messageId, new TenantId("t2"), _user1, _email);

        // Assert
        Assert.NotEqual(first, second);
    }

    [Fact]
    public void Create_UserAndCompanyWithTheSameId_GiveDifferentValues()
    {
        // Arrange
        var user = DeterministicId.Create(_messageId, null, _user1, _email);

        // Act
        var company = DeterministicId.Create(_messageId, null, new DeliveryTarget(DeliveryTargetKind.Company, "1"), _email);

        // Assert
        Assert.NotEqual(user, company);
    }

    [Fact]
    public void Create_LongTargetId_GivesTheSameValueEachTime()
    {
        // Arrange
        var target = new DeliveryTarget(DeliveryTargetKind.User, new string('ő', 300)); // above the stack buffer
        var first = DeterministicId.Create(_messageId, new TenantId("t1"), target, _email);

        // Act
        var second = DeterministicId.Create(_messageId, new TenantId("t1"), target, _email);

        // Assert
        Assert.Equal(first, second);
        Assert.NotEqual(DeterministicId.Create(_messageId, new TenantId("t1"), _user1, _email), first);
    }

    [Fact]
    public void Create_AnyInput_AllocatesZeroBytes()
    {
        // Arrange
        var tenant = new TenantId("t1");
        _ = DeterministicId.Create(_messageId, tenant, _user1, _email);

        // Act
        var before = GC.GetAllocatedBytesForCurrentThread();
        _ = DeterministicId.Create(_messageId, tenant, _user1, _email);
        var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        // Assert
        Assert.Equal(0, allocated);
    }
}
