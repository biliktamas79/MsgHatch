using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Tests.Shared;

namespace MsgHatch.AspNetCore.Tests.Unit;

/// <summary>The examples of the README of the package run as tests, and the README check compares them (D311).</summary>
public sealed class ReadmeUnitTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables();

    [Fact]
    public async Task MapMsgHatchDelivery_ReadmeSetup_MapsTheEndpointsOfTheThreeGroups()
    {
        // Arrange
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorization();
        builder.Services.AddMsgHatch().UseInMemoryStores();
        await using var app = builder.Build();

        // Act
        #region readme:setup
        app.MapMsgHatchDelivery()
            .RequireAuthorization("DeliveryAdmin");
        app.MapMsgHatchDeliveryContent()
            .RequireAuthorization("DeliveryContent");
        app.MapMsgHatchRegistry()
            .RequireAuthorization("RegistryAdmin");
        #endregion

        // Assert
        var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>();
        Assert.Contains(routes, route => route.RoutePattern.RawText!.StartsWith("/delivery", StringComparison.Ordinal));
        Assert.Contains(routes, route => route.RoutePattern.RawText!.StartsWith("/registry", StringComparison.Ordinal));
    }
}
