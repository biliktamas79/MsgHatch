using System.Globalization;
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
/// The use cases of <see cref="ITemplateCommands"/> (D219, D220, D222, D231): the validation of a request, the check of a
/// text with the engines of its message type before a save, the store of each tenant key, and a store that cannot write.
/// A file store on a temporary folder and the placeholder engine are the storage and the engine. Each test method uses a
/// message type of its own, with the name of the method. Each implementation gets them with one subclass that only
/// builds it.
/// </summary>
/// <typeparam name="T">The implementation.</typeparam>
/// <param name="folder">The temporary template folder of the test class.</param>
public abstract class TemplateCommandsTestsBase<T>(TemplateFolder folder) : IClassFixture<TemplateFolder>
    where T : class, ITemplateCommands
{
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    private readonly MessageType _type = new(TestContext.Current.TestMethod!.MethodName);

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task SaveTemplateAsync_ValidText_GivesTheEntryAndTheNextPrepareRendersEachNewText()
    {
        // Arrange
        await using var host = await AHostAsync();
        var commands = host.Service(TenantKey.None);

        // Act
        var first = await commands.SaveTemplateAsync(ALookup(), "Order of {{Name}}", 0, Ct);
        var firstText = await RenderAsync(host);
        await commands.SaveTemplateAsync(ALookup(), "Changed order of {{Name}}", first.Value.Version, Ct);
        var secondText = await RenderAsync(host);

        // Assert
        Assert.Equal(("Order of Client 1", "Changed order of Client 1"), (firstText, secondText));
    }

    [Fact]
    public async Task SaveTemplateAsync_UnknownPlaceholder_GivesTemplateInvalidAndWritesNoFile()
    {
        // Arrange
        await using var host = await AHostAsync();

        // Act
        var result = await host.Service(TenantKey.None).SaveTemplateAsync(ALookup(), "Order of {{Nmae}}", 0, Ct);

        // Assert
        Assert.Equal((422, false), (result.Error?.Type.Code, File.Exists(SharedPathOf("subject.txt"))));
    }

    [Theory]
    [InlineData("subjct", TemplatePartKind.PlainText)]
    [InlineData("subject", TemplatePartKind.Html)]
    [InlineData("footer", TemplatePartKind.PlainText)]
    public async Task SaveTemplateAsync_PartAndKindThatNoRegistrationDeclares_GivesValidationFailedAndWritesNoFile(string part, TemplatePartKind kind)
    {
        // Arrange
        await using var host = await AHostAsync();

        // Act
        var result = await host.Service(TenantKey.None).SaveTemplateAsync(ALookup(part: part, kind: kind), "Text", 0, Ct);

        // Assert
        Assert.Equal((400, 0), (result.Error?.Type.Code, Directory.EnumerateFiles(folder.Root, $"{_type.Name}.*").Count()));
    }

    [Fact]
    public async Task DeleteTemplateAsync_PartThatNoRegistrationDeclares_RemovesTheFile()
    {
        // Arrange
        await using var host = await AHostAsync();
        folder.Write($"{_type.Name}.en.footer.txt", "Old footer", DateTime.UnixEpoch);

        // Act
        var error = await host.Service(TenantKey.None).DeleteTemplateAsync(ALookup(part: "footer"), Ct);

        // Assert
        Assert.Equal((null, false), (error, File.Exists(SharedPathOf("footer.txt"))));
    }

    [Fact]
    public async Task SaveTemplateAsync_MessageTypeWithNoRegistration_GivesNotFound()
    {
        // Arrange
        await using var host = await AHostAsync();

        // Act
        var result = await host.Service(TenantKey.None).SaveTemplateAsync(ALookup() with { Type = new("Unregistered") }, "Text", 0, Ct);

        // Assert
        Assert.Equal(404, result.Error?.Type.Code);
    }

    [Theory]
    [MemberData(nameof(TemplateManagementTestRows.InvalidLookupRows), MemberType = typeof(TemplateManagementTestRows))]
    public async Task SaveAndDeleteTemplateAsync_InvalidLookup_GiveValidationFailedAndCallNoStore(string row, string language, string? scope, string part)
    {
        // Arrange
        var store = Substitute.For<ITemplateStore, ITemplateWriter>();
        await using var host = await StartAsync(ManagerHost.Services(b => b
            .UseTemplateStore(_ => store).AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>, _type.Name)));
        var commands = host.Service(TenantKey.None);
        var lookup = new TemplateLookup(_type, language, scope, part, TemplatePartKind.PlainText);

        // Act
        var save = await commands.SaveTemplateAsync(lookup, "Text", 0, Ct);
        var delete = await commands.DeleteTemplateAsync(lookup, Ct);

        // Assert
        Assert.Equal((row, 400, 400, 0), (row, save.Error?.Type.Code, delete?.Type.Code, store.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task SaveTemplateAsync_CompanyIdWhoseFolderNameHas255Characters_SavesTheTemplate()
    {
        // Arrange
        await using var host = await AHostAsync();
        var scope = new string('A', 127) + "a";

        // Act
        var result = await host.Service(TenantKey.None).SaveTemplateAsync(ALookup(scope: scope), "Company", 0, Ct);

        // Assert
        Assert.Equal((null, 255), (result.Error, ScopeFolders.FolderOf(scope)!.Length));
    }

    [Theory]
    [InlineData(1_000_000, null)]
    [InlineData(1_000_001, 400)]
    public async Task SaveTemplateAsync_TextLength_GivesTheRuleOfTheLimit(int length, int? expectedCode)
    {
        // Arrange
        await using var host = await AHostAsync();

        // Act
        var result = await host.Service(TenantKey.None).SaveTemplateAsync(ALookup(scope: $"length{length}"), new string('x', length), 0, Ct);

        // Assert
        Assert.Equal(expectedCode, result.Error?.Type.Code);
    }

    [Fact]
    public async Task SaveTemplateAsync_OldVersion_GivesConflict()
    {
        // Arrange
        await using var host = await AHostAsync();
        var commands = host.Service(TenantKey.None);
        var first = await commands.SaveTemplateAsync(ALookup(), "First", 0, Ct);
        await commands.SaveTemplateAsync(ALookup(), "Second", first.Value.Version, Ct);

        // Act
        var result = await commands.SaveTemplateAsync(ALookup(), "Third", first.Value.Version, Ct);

        // Assert
        Assert.Equal(409, result.Error?.Type.Code);
    }

    [Fact]
    public async Task SaveAndDeleteTemplateAsync_EmbeddedStore_CannotSaveOrDelete()
    {
        // Arrange
        await using var host = await StartAsync(ManagerHost.Services(static b => b
            .UseEmbeddedTemplates(Assembly.GetExecutingAssembly(), EmbeddedTemplateFiles.Prefix)
            .AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>)));
        var commands = host.Service(TenantKey.None);
        var lookup = new TemplateLookup(new MessageType("TestEvent"), "en", Scope: null, "subject", TemplatePartKind.PlainText);

        // Act
        var save = await commands.SaveTemplateAsync(lookup, "Order", 0, Ct);
        var delete = await commands.DeleteTemplateAsync(lookup, Ct);

        // Assert
        Assert.Equal((400, 400), (save.Error?.Type.Code, delete?.Type.Code));
    }

    [Fact]
    public async Task SaveTemplateAsync_CommandsOfATenant_WriteIntoTheStoreOfTheTenantAndNotIntoTheSharedStore()
    {
        // Arrange
        await using var host = await AHostAsync();

        // Act
        await host.Service(_t1).SaveTemplateAsync(ALookup(), "Tenant text", 0, Ct);

        // Assert
        var tenant = await host.Server.GetRequiredKeyedService<ITemplateQueries>(_t1).GetTemplateAsync(ALookup(), Ct);
        var shared = await host.Server.GetRequiredKeyedService<ITemplateQueries>(TenantKey.None).GetTemplateAsync(ALookup(), Ct);
        Assert.Equal(
            ("Tenant text", 404, true),
            (tenant.Value?.Text, shared.Error?.Type.Code, File.Exists(Path.Combine(folder.Parent, "tenants", "t1", $"{_type.Name}.en.subject.txt"))));
    }

    [Fact]
    public async Task SaveAndDeleteTemplateAsync_TenantWithNoTemplateStore_GiveNotFound()
    {
        // Arrange
        await using var host = await StartAsync(ManagerHost.Services(b => b
            .UseFileTemplates(folder.Root).AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>, _type.Name)));
        var commands = host.Service(_t1);

        // Act
        var save = await commands.SaveTemplateAsync(ALookup(), "Text", 0, Ct);
        var delete = await commands.DeleteTemplateAsync(ALookup(), Ct);

        // Assert
        Assert.Equal((404, 404), (save.Error?.Type.Code, delete?.Type.Code));
    }

    [Fact]
    public async Task SaveTemplateAsync_CommandsOfATenant_CheckTheTextWithTheEngineOfThatTenant()
    {
        // Arrange
        var engines = new Dictionary<TenantKey, ITemplateEngine<OrderEmail>>
        {
            [TenantKey.None] = Substitute.For<ITemplateEngine<OrderEmail>>(),
            [_t1] = Substitute.For<ITemplateEngine<OrderEmail>>(),
        };
        foreach (var engine in engines.Values)
        {
            engine.CheckAsync(default, default!, Ct).ReturnsForAnyArgs(Issues());
        }

        await using var host = await StartAsync(ManagerHost.Services(b => b
            .UseTenantTemplateStore((_, key) => new FileSystemTemplateStore(Path.Combine(folder.Parent, "tenants", key.Tenant!.Value.Value)))
            .AddOrderEmailEngine((_, key) => engines[key], _type.Name)));

        // Act
        await host.Service(_t1).SaveTemplateAsync(ALookup(), "Text", 0, Ct);

        // Assert
        await engines[_t1].Received(1).CheckAsync(ALookup(), "Text", Arg.Any<CancellationToken>());
        Assert.Empty(engines[TenantKey.None].ReceivedCalls());
    }

    [Fact]
    public async Task SaveTemplateAsync_TwoPayloadTypesOfOneMessageType_ChecksTheTextOnlyWithTheEngineThatReadsThePart()
    {
        // Arrange
        var orders = Substitute.For<ITemplateEngine<OrderEmail>>();
        orders.CheckAsync(default, default!, Ct).ReturnsForAnyArgs(Issues());
        var notes = Substitute.For<ITemplateEngine<string>>();
        notes.CheckAsync(default, default!, Ct).ReturnsForAnyArgs(Issues(new TemplateIssue("The note is not valid.", Line: null, Column: null)));
        await using var host = await StartAsync(ManagerHost.Services(b => b.UseFileTemplates(folder.Root).AddMessage(_type.Name, 1, TestJsonContext.Default.TestEvent, m => m
            .WithPayload<OrderEmail>(
                static (_, _) => Substitute.For<IPayloadLoader<TestEvent, OrderEmail>>(),
                p => p.AddTemplateEngine("Email:Orders", "orders", [new TemplatePart("subject", TemplatePartKind.PlainText)], (_, _) => orders))
            .WithPayload<string>(
                static (_, _) => Substitute.For<IPayloadLoader<TestEvent, string>>(),
                p => p.AddTemplateEngine("Sms:Notes", "notes", [new TemplatePart("sms", TemplatePartKind.PlainText)], (_, _) => notes)))));
        var commands = host.Service(TenantKey.None);

        // Act
        var subject = await commands.SaveTemplateAsync(ALookup(), "Subject", 0, Ct);
        var sms = await commands.SaveTemplateAsync(ALookup(part: "sms"), "Note", 0, Ct);

        // Assert
        Assert.Equal((null, 422), (subject.Error, sms.Error?.Type.Code));
        await orders.Received(1).CheckAsync(Arg.Any<TemplateLookup>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await notes.Received(1).CheckAsync(Arg.Any<TemplateLookup>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SaveTemplateAsync_UnknownLanguageAndATextThatIsTooLong_GivesTheErrorOfTheText()
    {
        // Arrange
        await using var host = await AHostAsync();
        var text = new string('x', 1_000_001);

        // Act
        var save = await host.Service(TenantKey.None).SaveTemplateAsync(ALookup(language: "xx"), text, 0, Ct);

        // Assert
        var rule = TemplateLookupRules.CheckText(text);
        Assert.Equal((true, rule), (rule is not null, save.Error));
    }

    [Fact]
    public async Task SaveAndDeleteTemplateAsync_UnknownLanguageAndAPartThatIsNotAName_GiveTheErrorOfThePart()
    {
        // Arrange
        await using var host = await AHostAsync();
        var lookup = ALookup(language: "xx", part: "sub ject");

        // Act
        var save = await host.Service(TenantKey.None).SaveTemplateAsync(lookup, "Text", 0, Ct);
        var delete = await host.Service(TenantKey.None).DeleteTemplateAsync(lookup, Ct);

        // Assert
        var rule = TemplateLookupRules.Check(lookup);
        Assert.Equal((true, rule, rule), (rule is not null, save.Error, delete));
    }

    [Fact]
    public async Task SaveTemplateAsync_NullText_ThrowsArgumentNullException()
    {
        // Arrange
        await using var host = await AHostAsync();

        // Act
        var exception = await Record.ExceptionAsync(async () => await host.Service(TenantKey.None).SaveTemplateAsync(ALookup(), null!, 0, Ct));

        // Assert
        Assert.IsType<ArgumentNullException>(exception);
    }

    /// <summary>Starts a host of the implementation on the services of a test.</summary>
    /// <param name="addServices">Adds the services of the host with the template stores and the engines.</param>
    /// <returns>The started host.</returns>
    protected abstract ValueTask<IManagementTestHost<T>> StartAsync(Action<IServiceCollection> addServices);

    private static ValueTask<ErrorOr<IReadOnlyList<TemplateIssue>>> Issues(params TemplateIssue[] issues) =>
        ValueTask.FromResult(ErrorOr<IReadOnlyList<TemplateIssue>>.ForValue(issues));

    private ValueTask<IManagementTestHost<T>> AHostAsync() =>
        StartAsync(ManagerHost.Services(b => b
            .UseFileTemplates(folder.Root)
            .UseTenantTemplateStore((_, key) => new FileSystemTemplateStore(Path.Combine(folder.Parent, "tenants", key.Tenant!.Value.Value)))
            .AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>, _type.Name)));

    private TemplateLookup ALookup(
        string language = "en", string? scope = null, string part = "subject", TemplatePartKind kind = TemplatePartKind.PlainText) =>
        new(_type, language, scope, part, kind);

    private async Task<string> RenderAsync(IManagementTestHost<T> host)
    {
        var engine = host.Server.GetRequiredKeyedService<ITemplateEngine<OrderEmail>>(TenantKey.None);
        var template = await engine.PrepareAsync(new TemplateRequest(_type, "en", Scope: null, "subject", TemplatePartKind.PlainText), Ct);
        var rendered = await template.Value.RenderAsync(
            new OrderEmail("Client 1", 3, 1234.5m, DateTime.UnixEpoch), CultureInfo.GetCultureInfo("en"), Ct);
        return rendered.Value;
    }

    private string SharedPathOf(string partFile) => Path.Combine(folder.Root, $"{_type.Name}.en.{partFile}");
}
