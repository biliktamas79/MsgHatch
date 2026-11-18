using System.Reflection;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Templates;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Templates.Management;
using MsgHatch.Templates.Placeholders;
using MsgHatch.Templates.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Templates.Tests.Unit.Contracts;

/// <summary>
/// The use cases of <see cref="ITemplateQueries"/> (D222, D231): the validation of a request, the read and the list of a
/// store, the store of each tenant key, and a store that cannot list. A file store on a temporary folder and the
/// placeholder engine are the storage and the engine. Each test method uses a message type of its own, with the name of
/// the method. Each implementation gets them with one subclass that only builds it.
/// </summary>
/// <typeparam name="T">The implementation.</typeparam>
/// <param name="folder">The temporary template folder of the test class.</param>
public abstract class TemplateQueriesTestsBase<T>(TemplateFolder folder) : IClassFixture<TemplateFolder>
    where T : class, ITemplateQueries
{
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    private readonly MessageType _type = new(TestContext.Current.TestMethod!.MethodName);

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Theory]
    [MemberData(nameof(TemplateManagementTestRows.InvalidLookupRows), MemberType = typeof(TemplateManagementTestRows))]
    public async Task GetAndCheckTemplateAsync_InvalidLookup_GiveValidationFailedAndCallNoStore(string row, string language, string? scope, string part)
    {
        // Arrange
        var store = Substitute.For<ITemplateStore, ITemplateWriter>();
        await using var host = await StartAsync(ManagerHost.Services(b => b
            .UseTemplateStore(_ => store).AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>, _type.Name)));
        var lookup = new TemplateLookup(_type, language, scope, part, TemplatePartKind.PlainText);

        // Act
        var read = await host.Service(TenantKey.None).GetTemplateAsync(lookup, Ct);
        var check = await host.Service(TenantKey.None).CheckTemplateAsync(lookup, "Text", Ct);

        // Assert
        Assert.Equal((row, 400, 400, 0), (row, read.Error?.Type.Code, check.Error?.Type.Code, store.ReceivedCalls().Count()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("acme\u0001")]
    public async Task GetTemplatesAsync_InvalidCompanyId_GivesValidationFailed(string scope)
    {
        // Arrange
        await using var host = await AHostAsync();

        // Act
        var result = await host.Service(TenantKey.None).GetTemplatesAsync(new TemplateQuery(_type, scope), Ct);

        // Assert
        Assert.Equal(400, result.Error?.Type.Code);
    }

    [Fact]
    public async Task GetTemplateAsync_SavedTemplate_GivesItsTextAndVersion()
    {
        // Arrange
        await using var host = await AHostAsync();
        var entry = await SaveOnTheServerAsync(host, TenantKey.None, ALookup(), "Order of {{Name}}");

        // Act
        var source = await host.Service(TenantKey.None).GetTemplateAsync(ALookup(), Ct);

        // Assert
        Assert.Equal(("Order of {{Name}}", entry.Version), (source.Value.Text, source.Value.Key.Version));
    }

    [Fact]
    public async Task GetTemplateAsync_MissingTemplate_GivesNotFound()
    {
        // Arrange
        await using var host = await AHostAsync();

        // Act
        var source = await host.Service(TenantKey.None).GetTemplateAsync(ALookup(language: "hu"), Ct);

        // Assert
        Assert.Equal(404, source.Error?.Type.Code);
    }

    [Fact]
    public async Task GetTemplateAsync_StoreError_GivesTheErrorOfTheStore()
    {
        // Arrange
        var store = Substitute.For<ITemplateStore>();
        store.FindAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult<ErrorOr<TemplateSource?>>(new Error(Errors.Unavailable)));
        await using var host = await StartAsync(ManagerHost.Services(b => b
            .UseTemplateStore(_ => store).AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>, _type.Name)));

        // Act
        var source = await host.Service(TenantKey.None).GetTemplateAsync(ALookup(), Ct);

        // Assert
        Assert.Equal(503, source.Error?.Type.Code);
    }

    [Fact]
    public async Task GetTemplatesAsync_SavedTemplates_GivesTheirEntries()
    {
        // Arrange
        await using var host = await AHostAsync();
        var subject = await SaveOnTheServerAsync(host, TenantKey.None, ALookup(), "Subject");
        var body = await SaveOnTheServerAsync(host, TenantKey.None, ALookup(part: "body", kind: TemplatePartKind.Html), "<p>Body</p>");

        // Act
        var entries = await host.Service(TenantKey.None).GetTemplatesAsync(new TemplateQuery(_type), Ct);

        // Assert
        Assert.Equal([body, subject], entries.Value);
    }

    [Fact]
    public async Task GetTemplateAndGetTemplatesAsync_EmbeddedStore_ReadsButCannotList()
    {
        // Arrange
        await using var host = await StartAsync(ManagerHost.Services(static b => b
            .UseEmbeddedTemplates(Assembly.GetExecutingAssembly(), EmbeddedTemplateFiles.Prefix)
            .AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>)));
        var queries = host.Service(TenantKey.None);
        var lookup = new TemplateLookup(new MessageType("TestEvent"), "en", Scope: null, "subject", TemplatePartKind.PlainText);

        // Act
        var read = await queries.GetTemplateAsync(lookup, Ct);
        var list = await queries.GetTemplatesAsync(new TemplateQuery(), Ct);

        // Assert
        Assert.Equal(("Order {{OrderNumber}} shipped", 400), (read.Value?.Text, list.Error?.Type.Code));
    }

    [Fact]
    public async Task GetTemplateAsync_QueriesOfATenant_ReadOnlyTheStoreOfTheTenant()
    {
        // Arrange
        await using var host = await AHostAsync();
        await SaveOnTheServerAsync(host, _t1, ALookup(), "Tenant text");

        // Act
        var tenant = await host.Service(_t1).GetTemplateAsync(ALookup(), Ct);
        var shared = await host.Service(TenantKey.None).GetTemplateAsync(ALookup(), Ct);

        // Assert
        Assert.Equal(("Tenant text", 404), (tenant.Value?.Text, shared.Error?.Type.Code));
    }

    [Fact]
    public async Task GetTemplateAndGetTemplatesAsync_TenantWithNoTemplateStore_GivesNotFound()
    {
        // Arrange
        await using var host = await StartAsync(ManagerHost.Services(b => b
            .UseFileTemplates(folder.Root).AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>, _type.Name)));
        var queries = host.Service(_t1);

        // Act
        var list = await queries.GetTemplatesAsync(new TemplateQuery(), Ct);
        var read = await queries.GetTemplateAsync(ALookup(), Ct);

        // Assert
        Assert.Equal((404, 404), (list.Error?.Type.Code, read.Error?.Type.Code));
    }

    [Fact]
    public async Task GetTemplatePartsAsync_TwoRegistrationsOfOneMessageType_GivesEachDeclaredPartWithTheNamesOfItsEngines()
    {
        // Arrange
        await using var host = await StartAsync(ManagerHost.Services(b => b
            .AddMessage(_type.Name, 1, TestJsonContext.Default.TestEvent, m => m
                .WithPayload<OrderEmail>(
                    static (_, _) => Substitute.For<IPayloadLoader<TestEvent, OrderEmail>>(),
                    p => p.AddTemplateEngine("Email:Orders", PlaceholderTemplates.EngineName, TemplateEngineRegistrations.EmailParts, PlaceholderTemplates.Engine<OrderEmail>))
                .WithPayload<string>(
                    static (_, _) => Substitute.For<IPayloadLoader<TestEvent, string>>(),
                    p => p.AddTemplateEngine(
                        "Sms:Notes", "notes", [new TemplatePart("subject", TemplatePartKind.PlainText), new TemplatePart("sms", TemplatePartKind.PlainText)],
                        (_, _) => Substitute.For<ITemplateEngine<string>>())))
            .AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>, "OtherType")));

        // Act
        var parts = await host.Service(TenantKey.None).GetTemplatePartsAsync(_type, Ct);

        // Assert
        Assert.Equal(
            ["subject PlainText: placeholder, notes", "body Html: placeholder", "body PlainText: placeholder", "sms PlainText: notes"],
            parts.Value.Select(part => $"{part.Part.Name} {part.Part.Kind}: {string.Join(", ", part.Engines)}{(part.Type == _type ? "" : " of another type")}"));
    }

    [Fact]
    public async Task GetTemplatePartsAsync_RegistrationsWithAndWithNoSample_GivesHasSampleOfEachPart()
    {
        // Arrange
        await using var host = await StartAsync(ManagerHost.Services(b => b
            .AddMessage(_type.Name, 1, TestJsonContext.Default.TestEvent, m => m
                .WithPayload<OrderEmail>(
                    static (_, _) => Substitute.For<IPayloadLoader<TestEvent, OrderEmail>>(),
                    p => p.AddTemplateEngine(
                        "Email:Orders", PlaceholderTemplates.EngineName, [new TemplatePart("subject", TemplatePartKind.PlainText)], PlaceholderTemplates.Engine<OrderEmail>))
                .WithPayload<string>(
                    static (_, _) => Substitute.For<IPayloadLoader<TestEvent, string>>(),
                    p => p.AddTemplateEngine("Sms:Notes", "notes", [new TemplatePart("sms", TemplatePartKind.PlainText)], (_, _) => Substitute.For<ITemplateEngine<string>>())
                        .WithTemplateSample("A note")))
            .AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>, "OtherType", samples: [TemplateEngineRegistrations.ASample()])));

        // Act
        var parts = await host.Service(TenantKey.None).GetTemplatePartsAsync(_type, Ct);

        // Assert
        Assert.Equal(["subject: False", "sms: True"], parts.Value.Select(static part => $"{part.Part.Name}: {part.HasSample}"));
    }

    [Fact]
    public async Task GetTemplatePartsAsync_NoMessageType_GivesThePartsOfEachMessageType()
    {
        // Arrange
        await using var host = await StartAsync(ManagerHost.Services(b => b
            .AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>, _type.Name)
            .AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>, "OtherType", [new TemplatePart("subject", TemplatePartKind.PlainText)])));

        // Act
        var parts = await host.Service(TenantKey.None).GetTemplatePartsAsync(type: null, Ct);

        // Assert
        Assert.Equal(
            [$"{_type.Name} subject", $"{_type.Name} body", $"{_type.Name} body", "OtherType subject"],
            parts.Value.Select(static part => $"{part.Type.Name} {part.Part.Name}"));
    }

    [Theory]
    [InlineData("Order of {{Name}}", 0)]
    [InlineData("Order of {{Nmae}}", 1)]
    [InlineData("Order of {{Name", 1)]
    public async Task CheckTemplateAsync_Text_GivesTheIssuesOfThePlaceholderEngineAndStoresNothing(string text, int expectedIssues)
    {
        // Arrange
        await using var host = await AHostAsync();

        // Act
        var issues = await host.Service(TenantKey.None).CheckTemplateAsync(ALookup(), text, Ct);

        // Assert
        Assert.Equal((text, expectedIssues, 0), (text, issues.Value.Count, Directory.EnumerateFiles(folder.Root, $"{_type.Name}.*").Count()));
    }

    [Fact]
    public async Task CheckTemplateAsync_TwoEnginesReadThePart_GivesTheIssuesOfEachEngine()
    {
        // Arrange
        var orders = Substitute.For<ITemplateEngine<OrderEmail>>();
        orders.CheckAsync(default, default!, Ct).ReturnsForAnyArgs(Issues(new TemplateIssue("The order is not valid.", 1, 2)));
        var notes = Substitute.For<ITemplateEngine<string>>();
        notes.CheckAsync(default, default!, Ct).ReturnsForAnyArgs(Issues(new TemplateIssue("The note is not valid.", Line: null, Column: null)));
        await using var host = await StartAsync(ManagerHost.Services(b => b.AddMessage(_type.Name, 1, TestJsonContext.Default.TestEvent, m => m
            .WithPayload<OrderEmail>(
                static (_, _) => Substitute.For<IPayloadLoader<TestEvent, OrderEmail>>(),
                p => p.AddTemplateEngine("Email:Orders", "orders", [new TemplatePart("subject", TemplatePartKind.PlainText)], (_, _) => orders))
            .WithPayload<string>(
                static (_, _) => Substitute.For<IPayloadLoader<TestEvent, string>>(),
                p => p.AddTemplateEngine("Sms:Notes", "notes", [new TemplatePart("subject", TemplatePartKind.PlainText)], (_, _) => notes)))));

        // Act
        var issues = await host.Service(TenantKey.None).CheckTemplateAsync(ALookup(), "Text", Ct);

        // Assert
        Assert.Equal(
            [new TemplateIssue("The order is not valid.", 1, 2), new TemplateIssue("The note is not valid.", Line: null, Column: null)],
            issues.Value);
    }

    [Fact]
    public async Task CheckTemplateAsync_ErrorOfAnEngine_GivesThatError()
    {
        // Arrange
        var engine = Substitute.For<ITemplateEngine<OrderEmail>>();
        engine.CheckAsync(default, default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult<ErrorOr<IReadOnlyList<TemplateIssue>>>(new Error(Errors.Unavailable)));
        await using var host = await StartAsync(ManagerHost.Services(b => b.AddOrderEmailEngine((_, _) => engine, _type.Name)));

        // Act
        var issues = await host.Service(TenantKey.None).CheckTemplateAsync(ALookup(), "Text", Ct);

        // Assert
        Assert.Equal(Errors.Unavailable.Code, issues.Error?.Type.Code);
    }

    [Theory]
    [InlineData("subject", TemplatePartKind.Html, 400)]
    [InlineData("footer", TemplatePartKind.PlainText, 400)]
    public async Task CheckTemplateAsync_PartAndKindThatNoRegistrationDeclares_GivesValidationFailed(string part, TemplatePartKind kind, int expectedCode)
    {
        // Arrange
        await using var host = await AHostAsync();

        // Act
        var issues = await host.Service(TenantKey.None).CheckTemplateAsync(ALookup(part: part, kind: kind), "Text", Ct);

        // Assert
        Assert.Equal(expectedCode, issues.Error?.Type.Code);
    }

    [Fact]
    public async Task CheckTemplateAsync_MessageTypeWithNoRegistration_GivesNotFound()
    {
        // Arrange
        await using var host = await AHostAsync();

        // Act
        var issues = await host.Service(TenantKey.None).CheckTemplateAsync(ALookup() with { Type = new("Unregistered") }, "Text", Ct);

        // Assert
        Assert.Equal(404, issues.Error?.Type.Code);
    }

    [Fact]
    public async Task PreviewTemplateAsync_RegistrationsWithAndWithNoSample_GivesAPreviewOfEachRegistrationWithASampleAndStoresNothing()
    {
        // Arrange
        var notes = Substitute.For<ITemplateEngine<string>>();
        await using var host = await StartAsync(ManagerHost.Services(b => b
            .UseFileTemplates(folder.Root)
            .AddMessage(_type.Name, 1, TestJsonContext.Default.TestEvent, m => m
                .WithPayload<OrderEmail>(
                    static (_, _) => Substitute.For<IPayloadLoader<TestEvent, OrderEmail>>(),
                    p => p.AddTemplateEngine("Email:Orders", PlaceholderTemplates.EngineName, TemplateEngineRegistrations.EmailParts, PlaceholderTemplates.Engine<OrderEmail>)
                        .AddTemplateEngine("Push:Orders", PlaceholderTemplates.EngineName, TemplateEngineRegistrations.EmailParts, PlaceholderTemplates.Engine<OrderEmail>)
                        .WithTemplateSample(TemplateEngineRegistrations.ASample("Sample <client>")))
                .WithPayload<string>(
                    static (_, _) => Substitute.For<IPayloadLoader<TestEvent, string>>(),
                    p => p.AddTemplateEngine("Sms:Notes", "notes", TemplateEngineRegistrations.EmailParts, (_, _) => notes)))));

        // Act
        var previews = await host.Service(TenantKey.None).PreviewTemplateAsync(ALookup(part: "body", kind: TemplatePartKind.Html), "<p>Order of {{Name}}</p>", Ct);

        // Assert
        Assert.Equal(
            [new TemplatePreview("Email:Orders", "<p>Order of Sample &lt;client&gt;</p>"), new TemplatePreview("Push:Orders", "<p>Order of Sample &lt;client&gt;</p>")],
            previews.Value);
        Assert.Equal((0, 0), (notes.ReceivedCalls().Count(), Directory.EnumerateFiles(folder.Root, $"{_type.Name}.*").Count()));
    }

    [Fact]
    public async Task PreviewTemplateAsync_PartWithNoSample_GivesAnEmptyList()
    {
        // Arrange
        await using var host = await AHostAsync();

        // Act
        var previews = await host.Service(TenantKey.None).PreviewTemplateAsync(ALookup(), "Order of {{Name}}", Ct);

        // Assert
        Assert.Empty(previews.Value);
    }

    [Theory]
    [InlineData("Order of {{Name")]
    [InlineData("Order of {{Nmae}}")]
    public async Task PreviewTemplateAsync_TextThatDoesNotRender_GivesTemplateInvalid(string text)
    {
        // Arrange
        await using var host = await AHostAsync(TemplateEngineRegistrations.ASample());

        // Act
        var previews = await host.Service(TenantKey.None).PreviewTemplateAsync(ALookup(), text, Ct);

        // Assert
        Assert.Equal((text, 422), (text, previews.Error?.Type.Code));
    }

    [Theory]
    [MemberData(nameof(TemplateManagementTestRows.InvalidLookupRows), MemberType = typeof(TemplateManagementTestRows))]
    public async Task PreviewTemplateAsync_InvalidLookup_GivesValidationFailedAndCallsNoEngine(string row, string language, string? scope, string part)
    {
        // Arrange
        var engine = Substitute.For<ITemplateEngine<OrderEmail>>();
        await using var host = await StartAsync(ManagerHost.Services(b => b
            .UseFileTemplates(folder.Root).AddOrderEmailEngine((_, _) => engine, _type.Name, samples: [TemplateEngineRegistrations.ASample()])));
        var lookup = new TemplateLookup(_type, language, scope, part, TemplatePartKind.PlainText);

        // Act
        var previews = await host.Service(TenantKey.None).PreviewTemplateAsync(lookup, "Text", Ct);

        // Assert
        Assert.Equal((row, 400, 0), (row, previews.Error?.Type.Code, engine.ReceivedCalls().Count()));
    }

    [Theory]
    [InlineData("Unregistered", "subject", 404)]
    [InlineData(null, "footer", 400)]
    public async Task PreviewTemplateAsync_MessageTypeOrPartWithNoRegistration_GivesTheErrorOfTheCheck(string? type, string part, int expectedCode)
    {
        // Arrange
        await using var host = await AHostAsync(TemplateEngineRegistrations.ASample());
        var lookup = ALookup(part: part) with { Type = type is null ? _type : new MessageType(type) };

        // Act
        var previews = await host.Service(TenantKey.None).PreviewTemplateAsync(lookup, "Text", Ct);
        var issues = await host.Service(TenantKey.None).CheckTemplateAsync(lookup, "Text", Ct);

        // Assert
        Assert.Equal((expectedCode, issues.Error), (previews.Error?.Type.Code, previews.Error));
    }

    [Fact]
    public async Task GetAndCheckTemplateAsync_UnknownLanguageAndAPartThatIsNotAName_GiveTheErrorOfThePart()
    {
        // Arrange
        await using var host = await AHostAsync();
        var lookup = ALookup(language: "xx", part: "sub ject");

        // Act
        var read = await host.Service(TenantKey.None).GetTemplateAsync(lookup, Ct);
        var check = await host.Service(TenantKey.None).CheckTemplateAsync(lookup, "Text", Ct);

        // Assert
        var rule = TemplateLookupRules.Check(lookup);
        Assert.Equal((true, rule, rule), (rule is not null, read.Error, check.Error));
    }

    [Fact]
    public async Task GetTemplatesAndCheckTemplateAsync_NullArgument_ThrowArgumentNullException()
    {
        // Arrange
        await using var host = await AHostAsync();

        // Act
        var list = await Record.ExceptionAsync(async () => await host.Service(TenantKey.None).GetTemplatesAsync(null!, Ct));
        var check = await Record.ExceptionAsync(async () => await host.Service(TenantKey.None).CheckTemplateAsync(ALookup(), null!, Ct));
        var preview = await Record.ExceptionAsync(async () => await host.Service(TenantKey.None).PreviewTemplateAsync(ALookup(), null!, Ct));

        // Assert
        Assert.Equal(
            (typeof(ArgumentNullException), typeof(ArgumentNullException), typeof(ArgumentNullException)),
            (list?.GetType(), check?.GetType(), preview?.GetType()));
    }

    /// <summary>Starts a host of the implementation on the services of a test.</summary>
    /// <param name="addServices">Adds the services of the host with the template stores and the engines.</param>
    /// <returns>The started host.</returns>
    protected abstract ValueTask<IManagementTestHost<T>> StartAsync(Action<IServiceCollection> addServices);

    private static async Task<TemplateEntry> SaveOnTheServerAsync(IManagementTestHost<T> host, TenantKey key, TemplateLookup lookup, string text)
    {
        var saved = await host.Server.GetRequiredKeyedService<ITemplateCommands>(key).SaveTemplateAsync(lookup, text, 0, Ct);
        Assert.False(saved.IsError);
        return saved.Value;
    }

    private static ValueTask<ErrorOr<IReadOnlyList<TemplateIssue>>> Issues(params TemplateIssue[] issues) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<TemplateIssue>>.ForValue(issues));

    private ValueTask<IManagementTestHost<T>> AHostAsync(params OrderEmail[] samples) =>
        StartAsync(ManagerHost.Services(b => b
            .UseFileTemplates(folder.Root)
            .UseTenantTemplateStore((_, key) => new FileSystemTemplateStore(Path.Combine(folder.Parent, "tenants", key.Tenant!.Value.Value)))
            .AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>, _type.Name, samples: samples)));

    private TemplateLookup ALookup(
        string language = "en", string? scope = null, string part = "subject", TemplatePartKind kind = TemplatePartKind.PlainText) =>
        new(_type, language, scope, part, kind);
}
