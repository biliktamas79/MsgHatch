using MsgHatch.Domain.Identity;

namespace MsgHatch.SignalR.Tests.Unit;

/// <summary>The SignalR group names of the companies (D114): unique for each tenant and company, also when an id contains <c>:</c> or <c>%</c>.</summary>
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

    [Theory]
    [MemberData(nameof(NameRows))]
    public void ForCompany_TenantAndCompany_GivesTheNameOfTheRow(string row, string? tenant, string company, string expectedName)
    {
        // Act
        var name = SignalRGroups.ForCompany(tenant is null ? null : new TenantId(tenant), new CompanyId(company));

        // Assert
        Assert.Equal((row, expectedName), (row, name));
    }
}
