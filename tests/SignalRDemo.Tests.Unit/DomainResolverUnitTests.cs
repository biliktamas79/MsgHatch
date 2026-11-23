using System.Net.Http.Json;
using Microsoft.AspNetCore.TestHost;
using SignalRDemo.Tenants;
using SignalRDemo.Tests.Unit.TestTypes;

namespace SignalRDemo.Tests.Unit;

/// <summary>The tenant and the company of the host of a page, through the endpoint of the dashboard (D316).</summary>
public sealed class DomainResolverUnitTests
{
    [Theory]
    [InlineData("acme.localhost:5091", "eu", "acme")]
    [InlineData("eu.localhost", "eu", null)]
    [InlineData("localhost:5091", null, null)]
    [InlineData("nobody.localhost", null, null)]
    public async Task Resolve_Host_GivesItsTenantAndItsCompany(string host, string? tenant, string? company)
    {
        // Arrange
        await using var demo = DemoWebApp.Build();
        await demo.App.StartAsync(TestContext.Current.CancellationToken);
        using var client = demo.App.GetTestClient();

        // Act
        var resolved = await client.GetFromJsonAsync<ResolvedDomain>(
            $"{DomainResolver.Route}?host={Uri.EscapeDataString(host)}", TestContext.Current.CancellationToken);

        // Assert
        Assert.Equal(new ResolvedDomain(tenant, company), resolved);
    }
}
