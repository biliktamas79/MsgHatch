using MsgHatch.Domain.Identity;

namespace MsgHatch.SignalR.Tests.Unit;

/// <summary>The SignalR user names (D114): unique for each tenant and user, also when an id contains <c>:</c> or <c>%</c>.</summary>
public sealed class SignalRUsersUnitTests
{
    public static TheoryData<string, string?, string, string> NameRows => new()
    {
        { "no tenant", null, "u1", "u1" },
        { "a tenant", "t1", "u1", "t1:u1" },
        { "a user id with ':' and no tenant", null, "t1:u1", "t1%3Au1" },
        { "a tenant id with ':'", "a:b", "c", "a%3Ab:c" },
        { "a user id with ':'", "a", "b:c", "a:b%3Ac" },
        { "ids with '%'", "50%", "u%3A1", "50%25:u%253A1" },
        { "a user id with ':' and '%'", null, "a:%", "a%3A%25" },
        { "ids with other characters", "tenant|1", "auth0|abc", "tenant|1:auth0|abc" },
    };

    [Theory]
    [MemberData(nameof(NameRows))]
    public void ForUser_TenantAndUser_GivesTheNameOfTheRow(string row, string? tenant, string user, string expectedName)
    {
        // Act
        var name = SignalRUsers.ForUser(tenant is null ? null : new TenantId(tenant), new UserId(user));

        // Assert
        Assert.Equal((row, expectedName), (row, name));
    }
}
