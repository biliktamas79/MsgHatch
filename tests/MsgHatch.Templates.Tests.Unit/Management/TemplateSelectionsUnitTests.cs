using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Templates.Management;
using MsgHatch.Templates.Placeholders;
using MsgHatch.Templates.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Templates.Tests.Unit.Management;

/// <summary>
/// The selection of the implementation of the template queries and commands (D231), and their logging wrappers (D244):
/// <c>UseTemplateQueries</c> and <c>UseTemplateCommands</c> replace the in-process default of the template setup methods.
/// </summary>
/// <param name="folder">The temporary template folder of the test class.</param>
public sealed class TemplateSelectionsUnitTests(TemplateFolder folder) : IClassFixture<TemplateFolder>
{
    private const string _queries = nameof(ITemplateQueries);
    private const string _commands = nameof(ITemplateCommands);

    private static readonly TemplateLookup _lookup = new(new MessageType("TestEvent"), "en", Scope: null, "subject", TemplatePartKind.PlainText);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, bool> SelectionRows => new()
    {
        { _queries, true },
        { _queries, false },
        { _commands, true },
        { _commands, false },
    };

    public static TheoryData<string, int, int> SuccessRows => new()
    {
        { "GetTemplates", 2017, LogLevelOf(LogLevel.Debug) },
        { "GetTemplate", 2019, LogLevelOf(LogLevel.Debug) },
        { "GetTemplateParts", 2021, LogLevelOf(LogLevel.Debug) },
        { "CheckTemplate", 2023, LogLevelOf(LogLevel.Debug) },
        { "PreviewTemplate", 2029, LogLevelOf(LogLevel.Debug) },
        { "SaveTemplate", 2025, LogLevelOf(LogLevel.Debug) },
        { "DeleteTemplate", 2027, LogLevelOf(LogLevel.Debug) },
    };

    public static TheoryData<string, int, int> FailureRows => new()
    {
        { "GetTemplates", 2018, LogLevelOf(LogLevel.Warning) },
        { "GetTemplate", 2020, LogLevelOf(LogLevel.Warning) },
        { "GetTemplateParts", 2022, LogLevelOf(LogLevel.Warning) },
        { "CheckTemplate", 2024, LogLevelOf(LogLevel.Warning) },
        { "PreviewTemplate", 2030, LogLevelOf(LogLevel.Warning) },
        { "SaveTemplate", 2026, LogLevelOf(LogLevel.Warning) },
        { "DeleteTemplate", 2028, LogLevelOf(LogLevel.Warning) },
    };

    [Fact]
    public async Task TemplateSetup_NoSelection_EachDefaultWorksOnTheStore()
    {
        // Arrange
        await using var provider = HostWith(static _ => { }, selectFirst: false);
        await using var scope = provider.CreateAsyncScope();

        // Act
        var queries = await scope.ServiceProvider.GetRequiredService<ITemplateQueries>().GetTemplatePartsAsync(type: null, Ct);
        var commands = await scope.ServiceProvider.GetRequiredService<ITemplateCommands>().DeleteTemplateAsync(_lookup, Ct);

        // Assert
        Assert.Equal((null, null), (queries.Error, commands));
    }

    [Theory]
    [MemberData(nameof(SelectionRows))]
    public async Task Selection_BeforeOrAfterTheTemplateSetup_ReplacesOnlyItsInterface(string selected, bool selectFirst)
    {
        // Arrange
        var queries = Substitute.For<ITemplateQueries>();
        var commands = Substitute.For<ITemplateCommands>();
        await using var provider = HostWith(builder => Select(builder, selected, queries, commands), selectFirst);
        await using var scope = provider.CreateAsyncScope();

        // Act
        var resolvedQueries = scope.ServiceProvider.GetRequiredService<ITemplateQueries>();
        var resolvedCommands = scope.ServiceProvider.GetRequiredService<ITemplateCommands>();
        var defaultWorks = selected == _queries
            ? await resolvedCommands.DeleteTemplateAsync(_lookup, Ct) is null
            : (await resolvedQueries.GetTemplatePartsAsync(type: null, Ct)).IsSuccess;

        // Assert
        Assert.Equal(
            (selected, selectFirst, selected == _queries, selected == _commands, true),
            (selected, selectFirst, ReferenceEquals(queries, resolvedQueries), ReferenceEquals(commands, resolvedCommands), defaultWorks));
    }

    [Theory]
    [InlineData(_queries)]
    [InlineData(_commands)]
    public async Task Selection_TwoSelectionsOfOneInterface_GivesTheSetupErrorThatNamesBoth(string selected)
    {
        // Arrange
        var queries = Substitute.For<ITemplateQueries>();
        var commands = Substitute.For<ITemplateCommands>();
        await using var provider = HostWith(
            builder =>
            {
                Select(builder, selected, queries, commands);
                Select(builder, selected, queries, commands);
            },
            selectFirst: false);

        // Act
        var exception = Record.Exception(provider.GetRequiredService<IStartupValidator>().Validate);

        // Assert
        var method = $"Use{selected[1..]}";
        Assert.Contains($"{method} and {method} select an implementation of {selected}.", Assert.IsType<OptionsValidationException>(exception).Failures);
    }

    [Theory]
    [MemberData(nameof(SelectionRows))]
    public async Task Selection_EnableLogging_AddsTheLoggingWrapperOnlyWithLogging(string selected, bool enableLogging)
    {
        // Arrange
        var queries = Substitute.For<ITemplateQueries>();
        var commands = Substitute.For<ITemplateCommands>();
        await using var provider = HostWith(builder => Select(builder, selected, queries, commands), selectFirst: false, enableLogging);
        await using var scope = provider.CreateAsyncScope();

        // Act
        var resolved = selected == _queries
            ? ReferenceEquals(queries, scope.ServiceProvider.GetRequiredService<ITemplateQueries>())
            : ReferenceEquals(commands, scope.ServiceProvider.GetRequiredService<ITemplateCommands>());

        // Assert
        Assert.Equal((selected, !enableLogging), (selected, resolved));
    }

    [Theory]
    [MemberData(nameof(SuccessRows))]
    public async Task Operation_Success_LogsItsEventIdAndLevelWithTheTenant(string operation, int eventId, int level)
    {
        // Arrange
        var queries = SucceedingQueries();
        var commands = SucceedingCommands();
        await using var provider = HostWith(b => b.UseTemplateQueries((_, _) => queries).UseTemplateCommands((_, _) => commands), false, true);
        await using var scope = provider.CreateAsyncScope();

        // Act
        await CallAsync(scope.ServiceProvider, TenantKey.For(new TenantId("t1")), operation);

        // Assert
        var record = Assert.Single(ManagementRecords(provider));
        Assert.Equal((operation, eventId, level, "t1"), (operation, record.Id.Id, (int)record.Level, record.GetStructuredStateValue("Tenant")));
        await AssertForwardedOnlyTheCallOfAsync(queries, commands, operation);
    }

    [Theory]
    [MemberData(nameof(FailureRows))]
    public async Task Operation_UnavailableError_ReturnsItAndLogsItsEventIdAtTheWarningLevel(string operation, int eventId, int level)
    {
        // Arrange
        var error = new Error(Errors.Unavailable);
        var queries = FailingQueries(error);
        var commands = FailingCommands(error);
        await using var provider = HostWith(b => b.UseTemplateQueries((_, _) => queries).UseTemplateCommands((_, _) => commands), false, true);
        await using var scope = provider.CreateAsyncScope();

        // Act
        var returned = await CallAsync(scope.ServiceProvider, TenantKey.None, operation);

        // Assert
        var record = Assert.Single(ManagementRecords(provider));
        Assert.Equal((operation, true, eventId, level), (operation, ReferenceEquals(error, returned), record.Id.Id, (int)record.Level));
        await AssertForwardedOnlyTheCallOfAsync(queries, commands, operation);
    }

    [Fact]
    public async Task PreviewTemplateAsync_TemplateInvalid_LogsItsEventIdAtTheInformationLevel()
    {
        // Arrange
        var error = new Error(Errors.TemplateInvalid, "The template has an unclosed placeholder.");
        var queries = FailingQueries(error);
        await using var provider = HostWith(b => b.UseTemplateQueries((_, _) => queries), false, true);
        await using var scope = provider.CreateAsyncScope();

        // Act
        await CallAsync(scope.ServiceProvider, TenantKey.None, "PreviewTemplate");

        // Assert
        var record = Assert.Single(ManagementRecords(provider));
        Assert.Equal((2030, LogLevel.Information), (record.Id.Id, record.Level));
        await queries.Received(1).PreviewTemplateAsync(_lookup, "Text", Ct);
    }

    [Fact]
    public async Task Operations_TemplateTextAndCompanyId_NoLogRecordContainsThem()
    {
        // Arrange
        var queries = SucceedingQueries();
        var commands = SucceedingCommands();
        await using var provider = HostWith(b => b.UseTemplateQueries((_, _) => queries).UseTemplateCommands((_, _) => commands), false, true);
        await using var scope = provider.CreateAsyncScope();
        var lookup = _lookup with { Scope = "acme-company" };

        // Act
        await scope.ServiceProvider.GetRequiredService<ITemplateQueries>().CheckTemplateAsync(lookup, "Secret text", Ct);
        await scope.ServiceProvider.GetRequiredService<ITemplateQueries>().PreviewTemplateAsync(lookup, "Secret text", Ct);
        await scope.ServiceProvider.GetRequiredService<ITemplateCommands>().SaveTemplateAsync(lookup, "Secret text", 0, Ct);

        // Assert
        Assert.Equal(
            (3, false),
            (ManagementRecords(provider).Count, provider.LogRecords().Any(static record => record.ContainsText("Secret") || record.ContainsText("acme"))));
        await queries.Received(1).CheckTemplateAsync(lookup, "Secret text", Ct);
        await queries.Received(1).PreviewTemplateAsync(lookup, "Secret text", Ct);
        await commands.Received(1).SaveTemplateAsync(lookup, "Secret text", 0, Ct);
    }

    private static int LogLevelOf(LogLevel level) => (int)level;

    private static Task AssertForwardedOnlyTheCallOfAsync(ITemplateQueries queries, ITemplateCommands commands, string operation)
    {
        Assert.Single(queries.ReceivedCalls().Concat(commands.ReceivedCalls()));
        return operation switch
        {
            "GetTemplates" => queries.Received(1).GetTemplatesAsync(new TemplateQuery(), Ct).AsTask(),
            "GetTemplate" => queries.Received(1).GetTemplateAsync(_lookup, Ct).AsTask(),
            "GetTemplateParts" => queries.Received(1).GetTemplatePartsAsync(type: null, Ct).AsTask(),
            "CheckTemplate" => queries.Received(1).CheckTemplateAsync(_lookup, "Text", Ct).AsTask(),
            "PreviewTemplate" => queries.Received(1).PreviewTemplateAsync(_lookup, "Text", Ct).AsTask(),
            "SaveTemplate" => commands.Received(1).SaveTemplateAsync(_lookup, "Text", 0, Ct).AsTask(),
            _ => commands.Received(1).DeleteTemplateAsync(_lookup, Ct).AsTask(),
        };
    }

    private static void Select(MsgHatchBuilder builder, string selected, ITemplateQueries queries, ITemplateCommands commands)
    {
        _ = selected == _queries ? builder.UseTemplateQueries((_, _) => queries) : builder.UseTemplateCommands((_, _) => commands);
    }

    private static List<FakeLogRecord> ManagementRecords(IServiceProvider provider) =>
        [.. provider.LogRecords().Where(static record => record.Id.Id is >= 2017 and <= 2030)];

    private static ITemplateQueries SucceedingQueries()
    {
        var queries = Substitute.For<ITemplateQueries>();
        queries.GetTemplatesAsync(default!, default).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<TemplateEntry>>.ForValue([])));
        queries.GetTemplateAsync(default, default).ReturnsForAnyArgs(
            ValueTask.FromResult(ErrorOr<TemplateSource>.ForValue(new TemplateSource(new TemplateKey("id", 1), "Secret text"))));
        queries.GetTemplatePartsAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<TemplatePartInfo>>.ForValue([])));
        queries.CheckTemplateAsync(default, default!, default).ReturnsForAnyArgs(
            ValueTask.FromResult(ErrorOr<IReadOnlyList<TemplateIssue>>.ForValue([new TemplateIssue("Secret issue", 1, 1)])));
        queries.PreviewTemplateAsync(default, default!, default).ReturnsForAnyArgs(
            ValueTask.FromResult(ErrorOr<IReadOnlyList<TemplatePreview>>.ForValue([new TemplatePreview("Email:Secret", "Secret preview")])));
        return queries;
    }

    private static ITemplateCommands SucceedingCommands()
    {
        var commands = Substitute.For<ITemplateCommands>();
        commands.SaveTemplateAsync(default, default!, default, default).ReturnsForAnyArgs(
            static call => ValueTask.FromResult(ErrorOr<TemplateEntry>.ForValue(new TemplateEntry(call.ArgAt<TemplateLookup>(0), 2))));
        commands.DeleteTemplateAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(null));
        return commands;
    }

    private static ITemplateQueries FailingQueries(Error error)
    {
        var queries = Substitute.For<ITemplateQueries>();
        queries.GetTemplatesAsync(default!, default).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<TemplateEntry>>.ForError(error)));
        queries.GetTemplateAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<TemplateSource>.ForError(error)));
        queries.GetTemplatePartsAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<TemplatePartInfo>>.ForError(error)));
        queries.CheckTemplateAsync(default, default!, default).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<TemplateIssue>>.ForError(error)));
        queries.PreviewTemplateAsync(default, default!, default).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<TemplatePreview>>.ForError(error)));
        return queries;
    }

    private static ITemplateCommands FailingCommands(Error error)
    {
        var commands = Substitute.For<ITemplateCommands>();
        commands.SaveTemplateAsync(default, default!, default, default).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<TemplateEntry>.ForError(error)));
        commands.DeleteTemplateAsync(default, default).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(error));
        return commands;
    }

    private static async Task<Error?> CallAsync(IServiceProvider services, TenantKey key, string operation)
    {
        var queries = services.GetRequiredKeyedService<ITemplateQueries>(key);
        var commands = services.GetRequiredKeyedService<ITemplateCommands>(key);
        return operation switch
        {
            "GetTemplates" => (await queries.GetTemplatesAsync(new TemplateQuery(), Ct)).Error,
            "GetTemplate" => (await queries.GetTemplateAsync(_lookup, Ct)).Error,
            "GetTemplateParts" => (await queries.GetTemplatePartsAsync(type: null, Ct)).Error,
            "CheckTemplate" => (await queries.CheckTemplateAsync(_lookup, "Text", Ct)).Error,
            "PreviewTemplate" => (await queries.PreviewTemplateAsync(_lookup, "Text", Ct)).Error,
            "SaveTemplate" => (await commands.SaveTemplateAsync(_lookup, "Text", 0, Ct)).Error,
            _ => await commands.DeleteTemplateAsync(_lookup, Ct),
        };
    }

    private ServiceProvider HostWith(Action<MsgHatchBuilder> select, bool selectFirst, bool enableLogging = false)
    {
        var services = TestHost.CreateServices();
        var builder = services.AddMsgHatch(o => { o.DefaultLanguage = "en"; o.EnableLogging = enableLogging; });
        if (selectFirst)
        {
            select(builder);
        }

        builder.UseFileTemplates(folder.Root).AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>);
        if (!selectFirst)
        {
            select(builder);
        }

        return services.Build();
    }
}
