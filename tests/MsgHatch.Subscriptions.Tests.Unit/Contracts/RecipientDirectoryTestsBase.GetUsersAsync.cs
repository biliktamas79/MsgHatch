using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

public abstract partial class RecipientDirectoryTestsBase<TDirectory>
{
    [Fact]
    public async Task GetUsersAsync_Users1And2_GivesTheirTwoProfiles_U1()
    {
        // Act
        var users = await Directory.GetUsersAsync([new UserId("1"), new UserId("2")], Ct);

        // Assert
        Assert.Equal(["1 100 en client1@acme.example", "2 100 hu client2@acme.example"], users.Value.Select(Describe));
    }

    [Fact]
    public async Task GetUsersAsync_Users1And99_GivesOnlyUser1AndNoError_U2()
    {
        // Act
        var users = await Directory.GetUsersAsync([new UserId("1"), new UserId("99")], Ct);

        // Assert
        Assert.Equal(["1 100 en client1@acme.example"], users.Value.Select(Describe));
    }
}
