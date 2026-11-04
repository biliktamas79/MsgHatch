using MsgHatch.Domain.Identity;

namespace MsgHatch.Subscriptions.Tests.Unit;

public sealed class NotificationScopeUnitTests
{
    [Fact]
    public void ForUsers_DuplicateUser_KeepsTheThreeUsersInTheirOrderAndNoCompany_V2()
    {
        // Arrange
        string[] users = ["1", "1", "2"];

        // Act
        var scope = NotificationScope.ForUsers(users);

        // Assert
        Assert.Equal(("1,1,2", (CompanyId?)null), (string.Join(',', scope.Users.ToArray().Select(static user => user.Value)), scope.Company));
    }

    [Fact]
    public void ForUsers_UserIds_KeepsThemInTheirOrder()
    {
        // Arrange
        UserId[] users = [new("2"), new("1")];

        // Act
        var scope = NotificationScope.ForUsers(users);

        // Assert
        Assert.Equal(users, scope.Users.ToArray());
    }

    [Fact]
    public void ForUsers_InvalidStringId_ThrowsArgumentException()
    {
        // Arrange
        string[] users = ["1", ""];

        // Act
        var exception = Record.Exception(() => NotificationScope.ForUsers(users));

        // Assert
        Assert.IsType<ArgumentException>(exception);
    }

    [Fact]
    public void ForCompany_Company_HasTheCompanyAndNoUsers_V3()
    {
        // Arrange
        var company = new CompanyId("100");

        // Act
        var scope = NotificationScope.ForCompany(company);

        // Assert
        Assert.Equal(((CompanyId?)company, 0), (scope.Company, scope.Users.Length));
    }
}
