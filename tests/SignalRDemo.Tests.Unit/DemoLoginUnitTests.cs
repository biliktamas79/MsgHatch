using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.TestHost;
using SignalRDemo.Sessions;
using SignalRDemo.Tests.Unit.TestTypes;

namespace SignalRDemo.Tests.Unit;

/// <summary>The demo login with no password (D316), with the demo data of <c>demo-data.json</c>.</summary>
public sealed class DemoLoginUnitTests
{
    [Fact]
    public async Task Login_UserWithOneAccountInTheTenant_GivesATokenOfThatAccount()
    {
        // Arrange
        await using var demo = DemoWebApp.Build();
        await demo.App.StartAsync(TestContext.Current.CancellationToken);
        using var client = demo.App.GetTestClient();

        // Act
        using var response = await client.PostAsJsonAsync(DemoLogin.Route, new LoginRequest("eu", "bela"), TestContext.Current.CancellationToken);

        // Assert
        var login = await response.Content.ReadFromJsonAsync<LoginResponse>(TestContext.Current.CancellationToken);
        Assert.Equal((HttpStatusCode.OK, "eu", "acme", "bela", true), (response.StatusCode, login!.Tenant, login.Company, login.User, login.Token.Length > 0));
    }

    [Theory]
    [InlineData("anna", null, HttpStatusCode.BadRequest, "ValidationFailed")]
    [InlineData("anna", "globex", HttpStatusCode.Forbidden, "Rejected")]
    public async Task Login_TwoAccountsWithNoChoiceOrAnAccountOfAnotherTenant_GivesTheErrorType(
        string user, string? account, HttpStatusCode status, string errorType)
    {
        // Arrange
        await using var demo = DemoWebApp.Build();
        await demo.App.StartAsync(TestContext.Current.CancellationToken);
        using var client = demo.App.GetTestClient();

        // Act
        using var response = await client.PostAsJsonAsync(DemoLogin.Route, new LoginRequest("eu", user, account), TestContext.Current.CancellationToken);

        // Assert
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(TestContext.Current.CancellationToken));
        Assert.Equal((status, errorType), (response.StatusCode, problem.RootElement.GetProperty("errorType").GetString()));
    }
}
