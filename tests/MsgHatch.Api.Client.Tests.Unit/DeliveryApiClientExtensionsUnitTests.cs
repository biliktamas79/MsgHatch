using System.Net;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MsgHatch.Api.Client.DependencyInjection;
using MsgHatch.Api.Client.Tests.Unit.TestTypes;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Management;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Api.Client.Tests.Unit;

/// <summary>The setup methods of the delivery API client (D289, D294): the options, the selection, and a host with no role.</summary>
public sealed class DeliveryApiClientExtensionsUnitTests
{
    private const string _section = DeliveryApiClientOptions.SectionName;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, Dictionary<string, string?>, string> InvalidOptionsRows => new()
    {
        { "no base address", new() { [$"{_section}:Prefix"] = "/delivery" }, "BaseAddress" },
        { "a relative base address", new() { [$"{_section}:BaseAddress"] = "api/delivery" }, "BaseAddress" },
        { "a timeout under 1 second", new() { [$"{_section}:BaseAddress"] = "https://api.example.com", [$"{_section}:Timeout"] = "00:00:00.500" }, "Timeout" },
        { "a timeout over 10 minutes", new() { [$"{_section}:BaseAddress"] = "https://api.example.com", [$"{_section}:Timeout"] = "00:10:01" }, "Timeout" },
    };

    [Theory]
    [MemberData(nameof(InvalidOptionsRows))]
    public async Task UseDeliveryApiClient_InvalidOptions_GivesAnOptionsErrorAtStart(string row, Dictionary<string, string?> configuration, string member)
    {
        // Arrange
        await using var provider = TestHost.CreateServices(configuration: configuration)
            .AddMsgHatch(static o => o.Roles = MsgHatchRoles.None).UseDeliveryApiClient().Services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.IsType<OptionsValidationException>(exception);
        Assert.Equal(
            (row, typeof(DeliveryApiClientOptions), true),
            (row, failure.OptionsType, failure.Failures.Any(text => text.Contains(member, StringComparison.Ordinal))));
    }

    [Fact]
    public async Task UseDeliveryApiClient_OptionsFromTheConfiguration_SendsTheQueriesAndTheContentToTheBaseAddressAndThePrefix()
    {
        // Arrange
        var handler = new StubHandler(HttpStatusCode.OK, "[]");
        var configuration = new Dictionary<string, string?>
        {
            [$"{_section}:BaseAddress"] = "https://api.example.com/api",
            [$"{_section}:Prefix"] = "/admin/delivery/",
        };
        await using var provider = TestHost.CreateServices(configuration: configuration).AddMsgHatch(static o => o.Roles = MsgHatchRoles.None)
            .UseDeliveryApiClient(configureHttp: http => http.ConfigurePrimaryHttpMessageHandler(() => handler)).Services.Build();
        await using var scope = provider.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<IDeliveryQueries>();
        var id = Guid.NewGuid();

        // Act
        var channels = await queries.GetChannelsAsync(Ct);
        await queries.GetReceivedContentAsync(id, Ct);

        // Assert
        Assert.Equal(
            (0, $"https://api.example.com/api/admin/delivery/channels, https://api.example.com/api/admin/delivery/inbox/{id}/content"),
            (channels.Value.Count, string.Join(", ", handler.Uris.Select(static uri => uri.AbsoluteUri))));
    }

    [Fact]
    public async Task UseDeliveryApiClient_BaseAddressOnly_GivesTheDefaultsOfMapMsgHatchDelivery()
    {
        // Arrange
        await using var provider = TestHost.CreateServices().AddMsgHatch(static o => o.Roles = MsgHatchRoles.None)
            .UseDeliveryApiClient(new Uri("https://api.example.com")).Services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<DeliveryApiClientOptions>>().Value;

        // Assert
        Assert.Equal(("https://api.example.com/", "/delivery", TimeSpan.FromSeconds(30)), (options.BaseAddress?.AbsoluteUri, options.Prefix, options.Timeout));
    }

    [Theory]
    [InlineData(nameof(IDeliveryQueries))]
    [InlineData(nameof(IDeliveryCommands))]
    public async Task UseDeliveryApiClient_AndAnotherSelection_GivesTheSetupErrorThatNamesBoth(string service)
    {
        // Arrange
        var builder = TestHost.CreateServices().AddMsgHatch(static o => o.Roles = MsgHatchRoles.None)
            .UseDeliveryApiClient(new Uri("https://api.example.com"));
        _ = service == nameof(IDeliveryQueries)
            ? builder.UseDeliveryQueries(static (_, _) => Substitute.For<IDeliveryQueries>())
            : builder.UseDeliveryCommands(static (_, _) => Substitute.For<IDeliveryCommands>());
        await using var provider = builder.Services.Build();
        var second = service == nameof(IDeliveryQueries) ? nameof(MsgHatchBuilder.UseDeliveryQueries) : nameof(MsgHatchBuilder.UseDeliveryCommands);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Contains(
            $"UseDeliveryApiClient and {second} select an implementation of {service}.",
            Assert.IsType<OptionsValidationException>(exception).Failures);
    }

    [Fact]
    public async Task HostWithNoRole_OnlyTheClient_StartsAndResolvesTheQueriesAndTheCommandsOfEachKey()
    {
        // Arrange
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.None).UseDeliveryApiClient(new Uri("https://api.example.com"));
        using var host = builder.Build();
        var t1 = TenantKey.For(new TenantId("t1"));

        // Act
        await host.StartAsync(Ct);
        await using var scope = host.Services.CreateAsyncScope();
        object[] services =
        [
            scope.ServiceProvider.GetRequiredService<IDeliveryQueries>(),
            scope.ServiceProvider.GetRequiredKeyedService<IDeliveryQueries>(t1),
            scope.ServiceProvider.GetRequiredService<IDeliveryCommands>(),
            scope.ServiceProvider.GetRequiredKeyedService<IDeliveryCommands>(t1),
        ];
        await host.StopAsync(Ct);

        // Assert
        Assert.Equal(4, services.Distinct().Count());
    }
}
