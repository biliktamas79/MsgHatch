using MsgHatch.Domain.Identity;

namespace MsgHatch.SignalR.Tests.Unit;

/// <summary>
/// The SignalR group names of the companies and the tenants (D114, D315): unique for each tenant and company, also when an
/// id contains <c>:</c> or <c>%</c>.
/// </summary>
public sealed class SignalRGroupsUnitTests
{
    public static TheoryData<string, string?, string, string> NameRows => new()
    {
        { "no tenant", null, "c1", "msghatch:company:c1" },
        { "a tenant", "t1", "c1", "msghatch:t1:company:c1" },
        { "a company id with ':' and no tenant", null, "company:c1", "msghatch:company:company%3Ac1" },
        { "a tenant id with ':company:'", "a:company:b", "c", "msghatch:a%3Acompany%3Ab:company:c" },
        { "a company id with ':company:'", "a", "b:company:c", "msghatch:a:company:b%3Acompany%3Ac" },
        { "ids with '%'", "50%", "c%3A1", "msghatch:50%25:company:c%253A1" },
    };

    public static TheoryData<string, string?, string> TenantRows => new()
    {
        { "no tenant", null, "msghatch:all" },
        { "a tenant", "t1", "msghatch:tenant:t1" },
        { "a tenant id with ':'", "a:b", "msghatch:tenant:a%3Ab" },
        { "a tenant id with '%'", "50%3A", "msghatch:tenant:50%253A" },
    };

    [Theory]
    [MemberData(nameof(NameRows))]
    public void ForCompany_TenantAndCompany_GivesTheNameOfTheRow(string row, string? tenant, string company, string expectedName)
    {
        // Act
        var name = SignalRGroups.ForCompany(tenant is null ? null : new TenantId(tenant), new CompanyId(company));

        // Assert
        Assert.Equal((row, expectedName), (row, name));
    }

    [Theory]
    [MemberData(nameof(TenantRows))]
    public void ForTenant_Tenant_GivesTheNameOfTheRow(string row, string? tenant, string expectedName)
    {
        // Act
        var name = SignalRGroups.ForTenant(tenant is null ? null : new TenantId(tenant));

        // Assert
        Assert.Equal((row, expectedName), (row, name));
    }

    [Fact]
    public void ForTenant_TenantCompany_IsNotTheGroupOfTheCompanyTenantWithNoTenant()
    {
        // Act
        var tenantGroup = SignalRGroups.ForTenant(new TenantId("company"));

        // Assert
        Assert.NotEqual(SignalRGroups.ForCompany(null, new CompanyId("tenant")), tenantGroup);
    }
}
