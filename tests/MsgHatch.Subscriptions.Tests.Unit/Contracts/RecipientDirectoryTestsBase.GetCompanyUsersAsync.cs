using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

public abstract partial class RecipientDirectoryTestsBase<TDirectory>
{
    [Fact]
    public async Task GetCompanyUsersAsync_Acme_GivesUsers1And2And5_U3()
    {
        // Act
        var users = await Directory.GetCompanyUsersAsync(Scenario.Acme, Ct);

        // Assert
        Assert.Equal(
            ["1 100 en client1@acme.example", "2 100 hu client2@acme.example", "5 100 hu -"],
            users.Value.Select(Describe).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task GetCompanyUsersAsync_UnknownCompany_GivesAnEmptyList_U3()
    {
        // Act
        var users = await Directory.GetCompanyUsersAsync(new CompanyId("999"), Ct);

        // Assert
        Assert.Equal((false, 0), (users.IsError, users.Value.Count));
    }
}
