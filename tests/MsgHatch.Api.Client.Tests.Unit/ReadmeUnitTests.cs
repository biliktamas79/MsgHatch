using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Api.Client.DependencyInjection;
using MsgHatch.Domain.Management;
using MsgHatch.Tests.Shared;

namespace MsgHatch.Api.Client.Tests.Unit;

/// <summary>The examples of the README of the package run as tests, and the README check compares them (D311).</summary>
public sealed class ReadmeUnitTests
{
    [Fact]
    public void Readme_Snippets_EqualTheirSources() => ReadmeCheck.AssertSnippets();

    [Fact]
    public void Readme_OptionsTables_NameEachOption() => ReadmeCheck.AssertOptionsTables(typeof(RegistryApiClientOptions), typeof(DeliveryApiClientOptions));

    [Fact]
    public async Task Setup_SettingsOfTheReadme_ResolvesTheClient()
    {
        // Arrange
        var services = TestHost.CreateServices(configuration: ReadmeCheck.ReadSettings("options.json"));
        services.AddTransient<TokenHandler>();

        // Act
        #region readme:setup
        services.AddMsgHatch(o => o.Roles = MsgHatchRoles.None)
            .UseRegistryApiClient(configureHttp: http => http.AddHttpMessageHandler<TokenHandler>())
            .UseDeliveryApiClient(configureHttp: http => http.AddHttpMessageHandler<TokenHandler>());
        #endregion
        await using var provider = services.Build();

        // Assert
        await using var scope = provider.CreateAsyncScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<IRegistryQueries>());
        Assert.Equal(TimeSpan.FromMinutes(1), provider.GetRequiredService<IOptions<DeliveryApiClientOptions>>().Value.Timeout);
    }

    private sealed class TokenHandler : DelegatingHandler;
}
