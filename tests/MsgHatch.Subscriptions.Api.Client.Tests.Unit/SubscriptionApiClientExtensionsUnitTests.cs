using System.Net;
using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.Api.Client.DependencyInjection;
using MsgHatch.Subscriptions.Api.Client.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Verification;
using NSubstitute;

namespace MsgHatch.Subscriptions.Api.Client.Tests.Unit;

/// <summary>The setup methods of the subscription API client (D227, D231): the options, the selections, a host with no role, and the public types.</summary>
public sealed class SubscriptionApiClientExtensionsUnitTests
{
    private const string _section = MsgHatchOptions.DefaultSectionKey + ":" + SubscriptionApiClientOptions.SectionKey;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, Dictionary<string, string?>, string> InvalidOptionsRows => new()
    {
        { "no base address", new() { [$"{_section}:Prefix"] = "/subscriptions" }, "BaseAddress" },
        { "a relative base address", new() { [$"{_section}:BaseAddress"] = "api" }, "BaseAddress" },
        { "a timeout under 1 second", new() { [$"{_section}:BaseAddress"] = "https://api.example.com", [$"{_section}:Timeout"] = "00:00:00.999" }, "Timeout" },
    };

    public static TheoryData<string, string> TwoSelectionRows => new()
    {
        { "UseSubscriptionApiClient", "UseSubscriptionApiClient and UseSubscriptionCommands select an implementation of ISubscriptionCommands." },
        { "UseSubscriptionQueriesApiClient", "UseSubscriptionQueriesApiClient and UseRoutingVerifier select an implementation of IRoutingVerifier." },
        { "UseSubscriptionCommandsApiClient", "UseSubscriptionCommandsApiClient and UseSubscriptionCommands select an implementation of ISubscriptionCommands." },
    };

    [Theory]
    [InlineData("UseSubscriptionApiClient")]
    [InlineData("UseSubscriptionQueriesApiClient")]
    [InlineData("UseSubscriptionCommandsApiClient")]
    public async Task Setup_ConfigSectionPath_BindsTheOptionsUnderTheRoot(string method)
    {
        // Arrange
        var builder = TestHost.CreateServices(configuration: new Dictionary<string, string?>
        {
            ["Messaging:SubscriptionApiClient:BaseAddress"] = "https://api.example.com",
            [$"{_section}:Prefix"] = "/other",
        }).AddMsgHatch(static o => o.Roles = MsgHatchRoles.None, "Messaging");
        _ = method switch
        {
            "UseSubscriptionApiClient" => builder.UseSubscriptionApiClient(),
            "UseSubscriptionQueriesApiClient" => builder.UseSubscriptionQueriesApiClient(),
            _ => builder.UseSubscriptionCommandsApiClient(),
        };
        await using var provider = builder.Services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<SubscriptionApiClientOptions>>().Value;

        // Assert
        Assert.Equal(("https://api.example.com/", "/subscriptions"), (options.BaseAddress?.AbsoluteUri, options.Prefix));
    }

    [Theory]
    [MemberData(nameof(InvalidOptionsRows))]
    public async Task UseSubscriptionApiClient_InvalidOptions_GivesAnOptionsErrorAtStart(string row, Dictionary<string, string?> configuration, string member)
    {
        // Arrange
        await using var provider = TestHost.CreateServices(configuration: configuration)
            .AddMsgHatch(static o => o.Roles = MsgHatchRoles.None).UseSubscriptionApiClient().Services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var failure = Assert.IsType<OptionsValidationException>(exception);
        Assert.Equal(
            (row, typeof(SubscriptionApiClientOptions), true),
            (row, failure.OptionsType, failure.Failures.Any(text => text.Contains(member, StringComparison.Ordinal))));
    }

    [Fact]
    public async Task UseSubscriptionApiClient_OptionsFromTheConfiguration_SendsEachRequestToTheBaseAddressAndThePrefix()
    {
        // Arrange
        var handler = new StubHandler(HttpStatusCode.OK, "[]");
        var configuration = new Dictionary<string, string?>
        {
            [$"{_section}:BaseAddress"] = "https://api.example.com/api/",
            [$"{_section}:Prefix"] = "admin/subscriptions",
        };
        await using var provider = TestHost.CreateServices(configuration: configuration).AddMsgHatch(static o => o.Roles = MsgHatchRoles.None)
            .UseSubscriptionApiClient(configureHttp: http => http.ConfigurePrimaryHttpMessageHandler(() => handler)).Services.Build();
        await using var scope = provider.CreateAsyncScope();

        // Act
        var subscriptions = await scope.ServiceProvider.GetRequiredService<ISubscriptionQueries>().GetSubscriptionsAsync(new SubscriptionQuery(), Ct);

        // Assert
        Assert.Equal((0, "https://api.example.com/api/admin/subscriptions"), (subscriptions.Value.Count, Assert.Single(handler.Uris).AbsoluteUri));
    }

    [Fact]
    public async Task UseSubscriptionApiClient_BaseAddressOnly_GivesTheDefaultsOfMapMsgHatchSubscriptions()
    {
        // Arrange
        await using var provider = TestHost.CreateServices().AddMsgHatch(static o => o.Roles = MsgHatchRoles.None)
            .UseSubscriptionApiClient(new Uri("https://api.example.com/api")).Services.Build();

        // Act
        var options = provider.GetRequiredService<IOptions<SubscriptionApiClientOptions>>().Value;

        // Assert
        Assert.Equal(("https://api.example.com/api", "/subscriptions", TimeSpan.FromSeconds(30)), (options.BaseAddress?.AbsoluteUri, options.Prefix, options.Timeout));
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
            "UseSubscriptionApiClient" => builder.UseSubscriptionApiClient(baseAddress),
            "UseSubscriptionQueriesApiClient" => builder.UseSubscriptionQueriesApiClient(baseAddress),
            _ => builder.UseSubscriptionCommandsApiClient(baseAddress),
        };
        _ = method == "UseSubscriptionQueriesApiClient"
            ? builder.UseRoutingVerifier(static (_, _) => Substitute.For<IRoutingVerifier>())
            : builder.UseSubscriptionCommands(static (_, _) => Substitute.For<ISubscriptionCommands>());
        await using var provider = builder.Services.Build();

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        Assert.Contains(expected, Assert.IsType<OptionsValidationException>(exception).Failures);
    }

    [Fact]
    public async Task HostWithNoRole_OnlyTheClients_StartsAndResolvesTheThreeInterfacesOfEachKey()
    {
        // Arrange
        var builder = Host.CreateApplicationBuilder();
        builder.Services.AddMsgHatch(static o => o.Roles = MsgHatchRoles.None).UseSubscriptionApiClient(new Uri("https://api.example.com"));
        using var host = builder.Build();
        var tenant = TenantKey.For(new TenantId("t1"));

        // Act
        await host.StartAsync(Ct);
        await using var scope = host.Services.CreateAsyncScope();
        object[] services =
        [
            scope.ServiceProvider.GetRequiredService<ISubscriptionQueries>(), scope.ServiceProvider.GetRequiredService<ISubscriptionCommands>(),
            scope.ServiceProvider.GetRequiredService<IRoutingVerifier>(), scope.ServiceProvider.GetRequiredKeyedService<ISubscriptionQueries>(tenant),
            scope.ServiceProvider.GetRequiredKeyedService<ISubscriptionCommands>(tenant), scope.ServiceProvider.GetRequiredKeyedService<IRoutingVerifier>(tenant),
        ];
        await host.StopAsync(Ct);

        // Assert
        Assert.Equal(6, services.Distinct().Count());
    }

    [Fact]
    public void PublicTypes_ClientPackage_AreTheOptionsTheirValidatorAndTheSetupClassWithNoTypeOfSystemNetHttp()
    {
        // Arrange
        var assembly = typeof(SubscriptionApiClientOptions).Assembly;

        // Act
        var types = assembly.GetExportedTypes();
        var httpTypes = types
            .SelectMany(static type => type.GetMembers(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly))
            .SelectMany(TypesOf)
            .Where(static type => type.Namespace == "System.Net.Http");

        // Assert
        Assert.Equal(
            ("SubscriptionApiClientExtensions, SubscriptionApiClientOptions, SubscriptionApiClientOptionsValidator", ""),
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
