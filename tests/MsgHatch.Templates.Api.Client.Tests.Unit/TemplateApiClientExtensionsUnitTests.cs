using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.Api.Client.DependencyInjection;
using MsgHatch.Templates.Api.Client.Tests.Unit.TestTypes;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Templates.Management;
using MsgHatch.Templates.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Templates.Api.Client.Tests.Unit;

/// <summary>The setup methods of the template API client (D227, D231): the options, the selections, a host with no role, and the public types.</summary>
public sealed class TemplateApiClientExtensionsUnitTests
{
    private const string _section = MsgHatchOptions.DefaultSectionKey + ":" + TemplateApiClientOptions.SectionKey;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, Dictionary<string, string?>, string> InvalidOptionsRows => new()
    {
        { "no base address", new() { [$"{_section}:Prefix"] = "/templates" }, "BaseAddress" },
        { "a relative base address", new() { [$"{_section}:BaseAddress"] = "api/templates" }, "BaseAddress" },
        { "a timeout under 1 second", new() { [$"{_section}:BaseAddress"] = "https://api.example.com", [$"{_section}:Timeout"] = "00:00:00.500" }, "Timeout" },
        { "a timeout over 10 minutes", new() { [$"{_section}:BaseAddress"] = "https://api.example.com", [$"{_section}:Timeout"] = "00:10:01" }, "Timeout" },
    };

    public static TheoryData<string, string> TwoSelectionRows => new()
    {
        { "UseTemplateApiClient", "UseTemplateApiClient and UseTemplateQueries select an implementation of ITemplateQueries." },
        { "UseTemplateQueriesApiClient", "UseTemplateQueriesApiClient and UseTemplateQueries select an implementation of ITemplateQueries." },
        { "UseTemplateCommandsApiClient", "UseTemplateCommandsApiClient and UseTemplateCommands select an implementation of ITemplateCommands." },
    };

    [Theory]
    [InlineData("UseTemplateApiClient")]
    [InlineData("UseTemplateQueriesApiClient")]
    [InlineData("UseTemplateCommandsApiClient")]
    public async Task Setup_ConfigSectionPath_BindsTheOptionsUnderTheRoot(string method)
    {
        // Arrange
        var builder = TestHost.CreateServices(configuration: new Dictionary<string, string?>
        {
            ["Messaging:TemplateApiClient:BaseAddress"] = "https://api.example.com",
            [$"{_section}:Prefix"] = "/other",
        }).AddMsgHatch(static o => o.Roles = MsgHatchRoles.None, "Messaging");
        _ = method switch
        {
            "UseTemplateApiClient" => builder.UseTemplateApiClient(),
            "UseTemplateQueriesApiClient" => builder.UseTemplateQueriesApiClient(),
            _ => builder.UseTemplateCommandsApiClient(),
        };
        await using var provider = builder.Services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<TemplateApiClientOptions>>().Value;

        // Assert
        Assert.Equal(("https://api.example.com/", "/templates"), (options.BaseAddress?.AbsoluteUri, options.Prefix));
    }

    [Theory]
    [MemberData(nameof(InvalidOptionsRows))]
    public async Task UseTemplateApiClient_InvalidOptions_GivesAnOptionsErrorAtStart(string row, Dictionary<string, string?> configuration, string member)
    {
        // Arrange
        await using var provider = TestHost.CreateServices(configuration: configuration)
            .AddMsgHatch(static o => o.Roles = MsgHatchRoles.None).UseTemplateApiClient().Services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.IsType<OptionsValidationException>(exception);
        Assert.Equal(
            (row, typeof(TemplateApiClientOptions), true),
            (row, failure.OptionsType, failure.Failures.Any(text => text.Contains(member, StringComparison.Ordinal))));
    }

    [Fact]
    public async Task UseTemplateApiClient_OptionsFromTheConfiguration_SendsEachRequestToTheBaseAddressAndThePrefix()
    {
        // Arrange
        var handler = new StubHandler(HttpStatusCode.OK, "[]");
        var configuration = new Dictionary<string, string?>
        {
            [$"{_section}:BaseAddress"] = "https://api.example.com/api",
            [$"{_section}:Prefix"] = "/admin/templates/",
        };
        await using var provider = TestHost.CreateServices(configuration: configuration).AddMsgHatch(static o => o.Roles = MsgHatchRoles.None)
            .UseTemplateApiClient(configureHttp: http => http.ConfigurePrimaryHttpMessageHandler(() => handler)).Services.Build();
        await using var scope = provider.CreateAsyncScope();
        var queries = scope.ServiceProvider.GetRequiredService<ITemplateQueries>();

        // Act
        var parts = await queries.GetTemplatePartsAsync(type: null, Ct);
        var entries = await queries.GetTemplatesAsync(new TemplateQuery(), Ct);

        // Assert
        Assert.Equal(
            (0, 0, "https://api.example.com/api/admin/templates/parts, https://api.example.com/api/admin/templates"),
            (parts.Value.Count, entries.Value.Count, string.Join(", ", handler.Uris.Select(static uri => uri.AbsoluteUri))));
    }

    [Fact]
    public async Task UseTemplateApiClient_BaseAddressOnly_GivesTheDefaultsOfMapMsgHatchTemplates()
    {
        // Arrange
        await using var provider = TestHost.CreateServices().AddMsgHatch(static o => o.Roles = MsgHatchRoles.None)
            .UseTemplateApiClient(new Uri("https://api.example.com")).Services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<TemplateApiClientOptions>>().Value;

        // Assert
        Assert.Equal(("https://api.example.com/", "/templates", TimeSpan.FromSeconds(30)), (options.BaseAddress?.AbsoluteUri, options.Prefix, options.Timeout));
    }

    [Theory]
    [MemberData(nameof(TwoSelectionRows))]
    public async Task Selection_ClientAndAnotherSelectionOfOneInterface_GivesTheSetupErrorThatNamesBoth(string method, string expected)
    {
        // Arrange
        var builder = TestHost.CreateServices().AddMsgHatch(static o => o.Roles = MsgHatchRoles.None);
        var baseAddress = new Uri("https://api.example.com");
        _ = method switch
        {
            "UseTemplateApiClient" => builder.UseTemplateApiClient(baseAddress),
            "UseTemplateQueriesApiClient" => builder.UseTemplateQueriesApiClient(baseAddress),
            _ => builder.UseTemplateCommandsApiClient(baseAddress),
        };
        _ = method == "UseTemplateCommandsApiClient"
            ? builder.UseTemplateCommands(static (_, _) => Substitute.For<ITemplateCommands>())
            : builder.UseTemplateQueries(static (_, _) => Substitute.For<ITemplateQueries>());
        await using var provider = builder.Services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Contains(expected, Assert.IsType<OptionsValidationException>(exception).Failures);
    }

    [Fact]
    public async Task HostWithNoRole_OnlyTheClients_StartsAndResolvesBothInterfacesOfEachKey()
    {
        // Arrange
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.None).UseTemplateApiClient(new Uri("https://api.example.com"));
        using var host = builder.Build();
        var tenant = TenantKey.For(new TenantId("t1"));

        // Act
        await host.StartAsync(Ct);
        await using var scope = host.Services.CreateAsyncScope();
        object[] services =
        [
            scope.ServiceProvider.GetRequiredService<ITemplateQueries>(), scope.ServiceProvider.GetRequiredService<ITemplateCommands>(),
            scope.ServiceProvider.GetRequiredKeyedService<ITemplateQueries>(tenant), scope.ServiceProvider.GetRequiredKeyedService<ITemplateCommands>(tenant),
        ];
        await host.StopAsync(Ct);

        // Assert
        Assert.Equal(4, services.Distinct().Count());
    }

    [Fact]
    public void PublicTypes_ClientPackage_AreTheOptionsTheirValidatorAndTheSetupClassWithNoTypeOfSystemNetHttp()
    {
        // Arrange
        var assembly = typeof(TemplateApiClientOptions).Assembly;

        // Act
        var types = assembly.GetExportedTypes();
        var httpTypes = types
            .SelectMany(static type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .SelectMany(TypesOf)
            .Where(static type => type.Namespace == "System.Net.Http");

        // Assert
        Assert.Equal(
            ("TemplateApiClientExtensions, TemplateApiClientOptions, TemplateApiClientOptionsValidator", ""),
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
