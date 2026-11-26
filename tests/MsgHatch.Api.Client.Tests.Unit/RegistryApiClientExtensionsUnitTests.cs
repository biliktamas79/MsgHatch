using System.Net;
using System.Reflection;
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

/// <summary>The setup methods of the registry API client (D269, D271): the options, the selection, a host with no role, and the public types.</summary>
public sealed class RegistryApiClientExtensionsUnitTests
{
    private const string _section = MsgHatchOptions.DefaultSectionKey + ":" + RegistryApiClientOptions.SectionKey;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, Dictionary<string, string?>, string> InvalidOptionsRows => new()
    {
        { "no base address", new() { [$"{_section}:Prefix"] = "/registry" }, "BaseAddress" },
        { "a relative base address", new() { [$"{_section}:BaseAddress"] = "api/registry" }, "BaseAddress" },
        { "a timeout under 1 second", new() { [$"{_section}:BaseAddress"] = "https://api.example.com", [$"{_section}:Timeout"] = "00:00:00.500" }, "Timeout" },
        { "a timeout over 10 minutes", new() { [$"{_section}:BaseAddress"] = "https://api.example.com", [$"{_section}:Timeout"] = "00:10:01" }, "Timeout" },
    };

    [Fact]
    public async Task UseRegistryApiClient_ConfigSectionPath_BindsTheOptionsUnderTheRoot()
    {
        // Arrange
        var configuration = new Dictionary<string, string?>
        {
            ["Messaging:RegistryApiClient:BaseAddress"] = "https://api.example.com",
            [$"{_section}:Prefix"] = "/other",
        };
        await using var provider = TestHost.CreateServices(configuration: configuration)
            .AddMsgHatch(static o => o.Roles = MsgHatchRoles.None, "Messaging").UseRegistryApiClient().Services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<RegistryApiClientOptions>>().Value;

        // Assert
        Assert.Equal(("https://api.example.com/", "/registry"), (options.BaseAddress?.AbsoluteUri, options.Prefix));
    }

    [Theory]
    [MemberData(nameof(InvalidOptionsRows))]
    public async Task UseRegistryApiClient_InvalidOptions_GivesAnOptionsErrorAtStart(string row, Dictionary<string, string?> configuration, string member)
    {
        // Arrange
        await using var provider = TestHost.CreateServices(configuration: configuration)
            .AddMsgHatch(static o => o.Roles = MsgHatchRoles.None).UseRegistryApiClient().Services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.IsType<OptionsValidationException>(exception);
        Assert.Equal(
            (row, typeof(RegistryApiClientOptions), true),
            (row, failure.OptionsType, failure.Failures.Any(text => text.Contains(member, StringComparison.Ordinal))));
    }

    [Fact]
    public async Task UseRegistryApiClient_OptionsFromTheConfiguration_SendsEachRequestToTheBaseAddressAndThePrefix()
    {
        // Arrange
        var handler = new StubHandler(HttpStatusCode.OK, "[]");
        var configuration = new Dictionary<string, string?>
        {
            [$"{_section}:BaseAddress"] = "https://api.example.com/api",
            [$"{_section}:Prefix"] = "/admin/registry/",
        };
        await using var provider = TestHost.CreateServices(configuration: configuration).AddMsgHatch(static o => o.Roles = MsgHatchRoles.None)
            .UseRegistryApiClient(configureHttp: http => http.ConfigurePrimaryHttpMessageHandler(() => handler)).Services.Build();
        await using var scope = provider.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<IRegistryQueries>();

        // Act
        var channels = await queries.GetChannelsAsync(Ct);
        var types = await queries.GetMessageTypesAsync(Ct);

        // Assert
        Assert.Equal(
            (0, 0, "https://api.example.com/api/admin/registry/channels, https://api.example.com/api/admin/registry/message-types"),
            (channels.Value.Count, types.Value.Count, string.Join(", ", handler.Uris.Select(static uri => uri.AbsoluteUri))));
    }

    [Fact]
    public async Task UseRegistryApiClient_BaseAddressOnly_GivesTheDefaultsOfMapMsgHatchRegistry()
    {
        // Arrange
        await using var provider = TestHost.CreateServices().AddMsgHatch(static o => o.Roles = MsgHatchRoles.None)
            .UseRegistryApiClient(new Uri("https://api.example.com")).Services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<RegistryApiClientOptions>>().Value;

        // Assert
        Assert.Equal(("https://api.example.com/", "/registry", TimeSpan.FromSeconds(30)), (options.BaseAddress?.AbsoluteUri, options.Prefix, options.Timeout));
    }

    [Fact]
    public async Task UseRegistryApiClient_AndAnotherSelection_GivesTheSetupErrorThatNamesBoth()
    {
        // Arrange
        var builder = TestHost.CreateServices().AddMsgHatch(static o => o.Roles = MsgHatchRoles.None)
            .UseRegistryApiClient(new Uri("https://api.example.com"))
            .UseRegistryQueries(static (_, _) => Substitute.For<IRegistryQueries>());
        await using var provider = builder.Services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Contains(
            "UseRegistryApiClient and UseRegistryQueries select an implementation of IRegistryQueries.",
            Assert.IsType<OptionsValidationException>(exception).Failures);
    }

    [Fact]
    public async Task HostWithNoRole_OnlyTheClient_StartsAndResolvesTheQueriesOfEachKey()
    {
        // Arrange
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.None).UseRegistryApiClient(new Uri("https://api.example.com"));
        using var host = builder.Build();

        // Act
        await host.StartAsync(Ct);
        await using var scope = host.Services.CreateAsyncScope();
        object[] services =
        [
            scope.ServiceProvider.GetRequiredService<IRegistryQueries>(),
            scope.ServiceProvider.GetRequiredKeyedService<IRegistryQueries>(TenantKey.For(new TenantId("t1"))),
        ];
        await host.StopAsync(Ct);

        // Assert
        Assert.Equal(2, services.Distinct().Count());
    }

    [Fact]
    public void PublicTypes_ClientPackage_AreTheOptionsTheirValidatorAndTheSetupClassWithNoTypeOfSystemNetHttp()
    {
        // Arrange
        var assembly = typeof(RegistryApiClientOptions).Assembly;

        // Act
        var types = assembly.GetExportedTypes();
        var httpTypes = types
            .SelectMany(static type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .SelectMany(TypesOf)
            .Where(static type => type.Namespace == "System.Net.Http");

        // Assert
        Assert.Equal(
            ("DeliveryApiClientExtensions, DeliveryApiClientOptions, DeliveryApiClientOptionsValidator, RegistryApiClientExtensions, RegistryApiClientOptions, RegistryApiClientOptionsValidator", ""),
            (string.Join(", ", types.Select(static type => type.Name).Order(StringComparer.Ordinal)), string.Join(", ", httpTypes)));
    }

    private static IEnumerable<Type> TypesOf(MemberInfo member) => member switch
    {
        MethodInfo method => [method.ReturnType, .. method.GetParameters().Select(static parameter => parameter.ParameterType)],
        PropertyInfo property => [property.PropertyType],
        FieldInfo field => [field.FieldType],
        ConstructorInfo constructor => [.. constructor.GetParameters().Select(static parameter => parameter.ParameterType)],
        _ => [],
    };
}
