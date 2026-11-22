using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Templates.AspNetCore.Tests.Unit;

/// <summary>The examples of the README of the package run as tests, and the README check compares them (D311).</summary>
public sealed class ReadmeUnitTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables();

    [Fact]
    public async Task MapMsgHatchTemplates_ReadmeSetup_MapsTheEndpointsUnderTheDefaultPrefix()
    {
        // Arrange
        var builder = WebApplication.CreateSlimBuilder();
        builder.WebHost.UseTestServer();
        builder.Services.AddAuthorization();
        builder.Services.AddMsgHatch().UseInMemoryStores().UseFileTemplates("Templates");
        await using var app = builder.Build();

        // Act
        #region readme:setup
        app.MapMsgHatchTemplates()
            .RequireAuthorization("TemplateAdmin");
        #endregion

        // Assert
        var routes = ((IEndpointRouteBuilder)app).DataSources.SelectMany(source => source.Endpoints).OfType<RouteEndpoint>();
        Assert.All(routes, route => Assert.StartsWith("/templates", route.RoutePattern.RawText, StringComparison.Ordinal));
        Assert.NotEmpty(routes);
    }
}
