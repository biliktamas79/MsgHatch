using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Subscriptions.DependencyInjection;
using MsgHatch.Subscriptions.Management;
using MsgHatch.Subscriptions.Tests.Unit.TestTypes;
using MsgHatch.Subscriptions.Verification;
using MsgHatch.Templates.DependencyInjection;
using NSubstitute;

namespace MsgHatch.Subscriptions.Tests.Unit.DependencyInjection;

/// <summary>
/// The selection of the implementation of each management interface (D231): <c>UseSubscriptionQueries</c>,
/// <c>UseSubscriptionCommands</c>, and <c>UseRoutingVerifier</c> replace the in-process default of <c>UseSubscriptions</c>.
/// </summary>
public sealed class SubscriptionSelectionsUnitTests
{
    private const string _queries = nameof(ISubscriptionQueries);
    private const string _commands = nameof(ISubscriptionCommands);
    private const string _verifier = nameof(IRoutingVerifier);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, bool> SelectionRows => new()
    {
        { _queries, true },
        { _queries, false },
        { _commands, true },
        { _commands, false },
        { _verifier, true },
        { _verifier, false },
    };

    public static TheoryData<string> InterfaceRows => new() { _queries, _commands, _verifier };

    [Fact]
    public async Task UseSubscriptions_NoSelection_EachDefaultWorksOnTheStore()
    {
        // Arrange
        var substitutes = new Substitutes();
        await using var provider = HostWith(static _ => { }, selectFirst: false);
        await using var scope = provider.CreateAsyncScope();

        // Act
        var results = await WorkingInterfacesAsync(scope.ServiceProvider, substitutes);

        // Assert
        Assert.Equal([_queries, _commands, _verifier], results);
    }

    [Theory]
    [MemberData(nameof(SelectionRows))]
    public async Task Selection_BeforeOrAfterUseSubscriptions_ReplacesOnlyItsInterface(string selected, bool selectFirst)
    {
        // Arrange
        var substitutes = new Substitutes();
        await using var provider = HostWith(builder => substitutes.Select(builder, selected), selectFirst);
        await using var scope = provider.CreateAsyncScope();

        // Act
        var working = await WorkingInterfacesAsync(scope.ServiceProvider, substitutes);
        var resolved = substitutes.ResolvedSubstitute(scope.ServiceProvider, selected);

        // Assert
        Assert.Equal(
            (selected, selectFirst, true, string.Join(", ", new[] { _queries, _commands, _verifier }.Where(name => name != selected))),
            (selected, selectFirst, resolved, string.Join(", ", working)));
    }

    [Theory]
    [MemberData(nameof(InterfaceRows))]
    public async Task Selection_TwoSelectionsOfOneInterface_GivesTheSetupErrorThatNamesBoth(string selected)
    {
        // Arrange
        var substitutes = new Substitutes();
        await using var provider = HostWith(
            builder =>
            {
                substitutes.Select(builder, selected);
                substitutes.Select(builder, selected);
            },
            selectFirst: false);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var method = $"Use{(selected == _verifier ? "RoutingVerifier" : selected[1..])}";
        Assert.Contains(
            $"{method} and {method} select an implementation of {selected}.",
            Assert.IsType<OptionsValidationException>(exception).Failures);
    }

    [Theory]
    [MemberData(nameof(SelectionRows))]
    public async Task Selection_EnableLogging_AddsTheLoggingWrapperOnlyWithLogging(string selected, bool enableLogging)
    {
        // Arrange
        var substitutes = new Substitutes();
        await using var provider = HostWith(builder => substitutes.Select(builder, selected), selectFirst: false, enableLogging);
        await using var scope = provider.CreateAsyncScope();

        // Act
        var resolved = substitutes.ResolvedSubstitute(scope.ServiceProvider, selected);
        await Substitutes.CallAsync(scope.ServiceProvider, selected);

        // Assert
        Assert.Equal(
            (selected, !enableLogging, 1, enableLogging),
            (selected, resolved, substitutes.Of(selected).ReceivedCalls().Count(), provider.LogRecords().Any(record => record.Id.Id is >= 1120 and <= 1137)));
    }

    private static ServiceProvider HostWith(Action<MsgHatchBuilder> select, bool selectFirst, bool enableLogging = false)
    {
        var services = TestHost.CreateServices();
        var builder = services.AddMsgHatch(o => { o.Roles = MsgHatchRoles.Consumer; o.EnableLogging = enableLogging; });
        if (selectFirst)
        {
            select(builder);
        }

        builder
            .UseSubscriptions()
            .UseInMemoryStores()
            .UseInMemorySubscriptionStore()
            .UseRecipientDirectory(static (_, _) => ScenarioDoubles.AScenarioDirectory())
            .UseEmbeddedTemplates(typeof(ManagerHost).Assembly, SubscriptionHost.TemplatePrefix)
            .AddScenarioMessages();
        if (!selectFirst)
        {
            select(builder);
        }

        return services.Build();
    }

    private static async Task<List<string>> WorkingInterfacesAsync(IServiceProvider services, Substitutes substitutes)
    {
        List<string> working = [];
        foreach (var name in new[] { _queries, _commands, _verifier })
        {
            if (!substitutes.ResolvedSubstitute(services, name) && await Substitutes.CallAsync(services, name) is null)
            {
                working.Add(name);
            }
        }

        return working;
    }

    /// <summary>A substitute of each interface, which an unconfigured call fails with <c>Uninitialized</c>.</summary>
    private sealed class Substitutes
    {
        public ISubscriptionQueries Queries { get; } = Substitute.For<ISubscriptionQueries>();

        public ISubscriptionCommands Commands { get; } = Substitute.For<ISubscriptionCommands>();

        public IRoutingVerifier Verifier { get; } = Substitute.For<IRoutingVerifier>();

        public object Of(string name) =>
            name switch
            {
                _queries => Queries,
                _commands => Commands,
                _ => Verifier,
            };

        public void Select(MsgHatchBuilder builder, string name)
        {
            _ = name switch
            {
                _queries => builder.UseSubscriptionQueries((_, _) => Queries),
                _commands => builder.UseSubscriptionCommands((_, _) => Commands),
                _ => builder.UseRoutingVerifier((_, _) => Verifier),
            };
        }

        public bool ResolvedSubstitute(IServiceProvider services, string name) =>
            name switch
            {
                _queries => ReferenceEquals(Queries, services.GetRequiredService<ISubscriptionQueries>()),
                _commands => ReferenceEquals(Commands, services.GetRequiredService<ISubscriptionCommands>()),
                _ => ReferenceEquals(Verifier, services.GetRequiredService<IRoutingVerifier>()),
            };

        public static async Task<Error?> CallAsync(IServiceProvider services, string name) =>
            name switch
            {
                _queries => (await services.GetRequiredService<ISubscriptionQueries>().GetChannelsAsync(Ct)).Error,
                _commands => await services.GetRequiredService<ISubscriptionCommands>().DeleteAsync(Guid.Empty, Ct),
                _ => (await services.GetRequiredService<IRoutingVerifier>()
                    .VerifyAsync(new RoutingVerificationRequest([new UserId("1")], ["en"], new MessageType("A")), Ct)).Error,
            };
    }
}
