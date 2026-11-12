using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;

namespace MsgHatch.Subscriptions.Tests.Unit.Contracts;

public abstract partial class RecipientDirectoryTestsBase<TDirectory>
{
    [Fact]
    public async Task GetCompanyAsync_AcmeThenGlobex_GivesTheDefaultLanguageAndTheAddressOfEach_U4()
    {
        // Act
        var acme = await Directory.GetCompanyAsync(Scenario.Acme, Ct);
        var globex = await Directory.GetCompanyAsync(Scenario.Globex, Ct);

        // Assert
        Assert.Equal(("100 hu admin@acme.example", "200 en -"), (Describe(acme.Value), Describe(globex.Value)));
    }

    [Fact]
    public async Task GetCompanyAsync_UnknownCompany_GivesNull_U4()
    {
        // Act
        var company = await Directory.GetCompanyAsync(new CompanyId("999"), Ct);

        // Assert
        Assert.Equal((false, "null"), (company.IsError, Describe(company.Value)));
    }
}
