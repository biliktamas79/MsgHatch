using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SignalRDemo.Tenants;
using SignalRDemo.Tests.Unit.TestTypes;

namespace SignalRDemo.Tests.Unit;

/// <summary>The demo data of <c>demo-data.json</c>, which the owner check of the dashboard needs (roadmap, phase 46).</summary>
public sealed class DemoDataOptionsUnitTests
{
    [Fact]
    public async Task Build_DemoDataFile_HasTheTenantsCompaniesAndUsersOfThePlan()
    {
        // Arrange
        await using var demo = DemoWebApp.Build();

        // Act
        var data = demo.App.Services.GetRequiredService<IOptions<DemoDataOptions>>().Value;

        // Assert
        DemoUser[] users = [.. data.Tenants.SelectMany(static tenant => tenant.Users)];
        Assert.Equal(
            ("eu us testing", true, true, true, true),
            (string.Join(' ', data.Tenants.Select(static tenant => tenant.Id)),
                data.Tenants.Exists(static tenant => tenant.Companies.Count == 1),
                data.Tenants.Exists(static tenant => tenant.Companies.Count >= 2),
                users.Any(static user => user.Accounts.Count >= 2),
                users.GroupBy(static user => user.Id).Any(static ids => ids.Count() >= 2)));
    }

    [Theory]
    [InlineData("acme", "acme", "globex", "globex", false)]
    [InlineData("us", "us", "globex", "globex", true)]
    [InlineData("acme", "acme", "acme", "acme", true)]
    [InlineData("acme", "globex", "globex", "globex", true)]
    public void Validate_HostLabelsAndAccountsOfTenantsEuAndUs_FailsForALabelOfTwoNamesOrAnAccountOfAnotherTenant(
        string euCompany, string euAccount, string usCompany, string usAccount, bool failed)
    {
        // Arrange
        var data = new DemoDataOptions { Tenants = [TenantOf("eu", euCompany, euAccount), TenantOf("us", usCompany, usAccount)] };

        // Act
        var result = new DemoDataOptionsValidator().Validate(name: null, data);

        // Assert
        Assert.Equal(failed, result.Failed);
    }

    private static DemoTenant TenantOf(string id, string company, string account) =>
        new() { Id = id, Companies = [company], Users = [new DemoUser { Id = "anna", Accounts = [account] }] };
}
