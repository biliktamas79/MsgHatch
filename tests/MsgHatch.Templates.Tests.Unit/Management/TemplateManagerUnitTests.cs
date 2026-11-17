using System.Globalization;
using System.Reflection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using MsgHatch.Templates;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Templates.Placeholders;
using MsgHatch.Templates.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Templates.Tests.Unit.Management;

/// <summary>
/// The template manager (D219, D220, D222): the validation of a request, the check of a text with the engines of its
/// message type before a save, the store of each tenant key, and a store that cannot write. A file store on a temporary
/// folder and the placeholder engine are the storage and the engine. Each test method uses a message type of its own,
/// with the name of the method.
/// </summary>
public sealed class TemplateManagerUnitTests(TemplateFolder folder) : IClassFixture<TemplateFolder>
{
    private static readonly TenantKey _t1 = TenantKey.For(new TenantId("t1"));

    private readonly MessageType _type = new(TestContext.Current.TestMethod!.MethodName);

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string, string, string?, string> InvalidLookupRows => new()
    {
        { "an unknown language", "xx", null, "subject" },
        { "an empty language", "", null, "subject" },
        { "a language in capital letters", "HU", null, "subject" },
        { "a language that is not the name of its culture", "hu-hu", null, "subject" },
        { "a language with a private part", "en-US-x-test", null, "subject" },
        { "an empty company id", "en", "", "subject" },
        { "a company id with a control character", "en", "acme\u0001", "subject" },
        { "a company id whose folder name has 256 characters", "en", new string('A', 128), "subject" },
        { "an empty part", "en", null, "" },
        { "a part with a space", "en", null, "sub ject" },
        { "a part that starts with a digit", "en", null, "1subject" },
        { "a part with a dot", "en", null, "sub.ject" },
    };

    [Fact]
    public async Task SaveTemplateAsync_ValidText_GivesTheEntryAndTheNextPrepareRendersEachNewText()
    {
        // Arrange
        await using var host = AHost();
        var manager = host.Manager(TenantKey.None);

        // Act
        var first = await manager.SaveTemplateAsync(ALookup(), "Order of {{Name}}", 0, Ct);
        var firstText = await RenderAsync(host.Engine(TenantKey.None));
        await manager.SaveTemplateAsync(ALookup(), "Changed order of {{Name}}", first.Value.Version, Ct);
        var secondText = await RenderAsync(host.Engine(TenantKey.None));

        // Assert
        Assert.Equal(("Order of Client 1", "Changed order of Client 1"), (firstText, secondText));
    }

    [Fact]
    public async Task SaveTemplateAsync_UnknownPlaceholder_GivesTemplateInvalidAndWritesNoFile()
    {
        // Arrange
        await using var host = AHost();

        // Act
        var result = await host.Manager(TenantKey.None).SaveTemplateAsync(ALookup(), "Order of {{Nmae}}", 0, Ct);

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
        await using var host = AHost();

        // Act
        var result = await host.Manager(TenantKey.None).SaveTemplateAsync(ALookup(part: part, kind: kind), "Text", 0, Ct);

        // Assert
        Assert.Equal((400, 0), (result.Error?.Type.Code, Directory.EnumerateFiles(folder.Root, $"{_type.Name}.*").Count()));
    }

    [Fact]
    public async Task DeleteTemplateAsync_PartThatNoRegistrationDeclares_RemovesTheFile()
    {
        // Arrange
        await using var host = AHost();
        folder.Write($"{_type.Name}.en.footer.txt", "Old footer", DateTime.UnixEpoch);

        // Act
        var error = await host.Manager(TenantKey.None).DeleteTemplateAsync(ALookup(part: "footer"), Ct);

        // Assert
        Assert.Equal((null, false), (error, File.Exists(SharedPathOf("footer.txt"))));
    }

    [Fact]
    public async Task SaveTemplateAsync_MessageTypeWithNoRegistration_GivesNotFound()
    {
        // Arrange
        await using var host = AHost();

        // Act
        var result = await host.Manager(TenantKey.None).SaveTemplateAsync(ALookup() with { Type = new("Unregistered") }, "Text", 0, Ct);

        // Assert
        Assert.Equal(404, result.Error?.Type.Code);
    }

    [Theory]
    [MemberData(nameof(InvalidLookupRows))]
    public async Task EachMethod_InvalidLookup_GivesValidationFailedAndCallsNoStore(string row, string language, string? scope, string part)
    {
        // Arrange
        var store = Substitute.For<ITemplateStore, ITemplateWriter>();
        await using var host = new ManagerHost(b => b.UseTemplateStore(_ => store).AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>, _type.Name));
        var manager = host.Manager(TenantKey.None);
        var lookup = new TemplateLookup(_type, language, scope, part, TemplatePartKind.PlainText);

        // Act
        var save = await manager.SaveTemplateAsync(lookup, "Text", 0, Ct);
        var read = await manager.GetTemplateAsync(lookup, Ct);
        var delete = await manager.DeleteTemplateAsync(lookup, Ct);

        // Assert
        Assert.Equal((row, 400, 400, 400, 0), (row, save.Error?.Type.Code, read.Error?.Type.Code, delete?.Type.Code, store.ReceivedCalls().Count()));
    }

    [Theory]
    [InlineData("")]
    [InlineData("acme\u0001")]
    public async Task GetTemplatesAsync_InvalidCompanyId_GivesValidationFailed(string scope)
    {
        // Arrange
        await using var host = AHost();

        // Act
        var result = await host.Manager(TenantKey.None).GetTemplatesAsync(new TemplateQuery(_type, scope), Ct);

        // Assert
        Assert.Equal(400, result.Error?.Type.Code);
    }

    [Fact]
    public async Task SaveTemplateAsync_CompanyIdWhoseFolderNameHas255Characters_SavesTheTemplate()
    {
        // Arrange
        await using var host = AHost();
        var scope = new string('A', 127) + "a";

        // Act
        var result = await host.Manager(TenantKey.None).SaveTemplateAsync(ALookup(scope: scope), "Company", 0, Ct);

        // Assert
        Assert.Equal((null, 255), (result.Error, ScopeFolders.FolderOf(scope)!.Length));
    }

    [Theory]
    [InlineData(1_000_000, null)]
    [InlineData(1_000_001, 400)]
    public async Task SaveTemplateAsync_TextLength_GivesTheRuleOfTheLimit(int length, int? expectedCode)
    {
        // Arrange
        await using var host = AHost();

        // Act
        var result = await host.Manager(TenantKey.None).SaveTemplateAsync(ALookup(scope: $"length{length}"), new string('x', length), 0, Ct);

        // Assert
        Assert.Equal(expectedCode, result.Error?.Type.Code);
    }

    [Fact]
    public async Task GetTemplateAsync_SavedTemplate_GivesItsTextAndVersion()
    {
        // Arrange
        await using var host = AHost();
        var entry = await host.Manager(TenantKey.None).SaveTemplateAsync(ALookup(), "Order of {{Name}}", 0, Ct);

        // Act
        var source = await host.Manager(TenantKey.None).GetTemplateAsync(ALookup(), Ct);

        // Assert
        Assert.Equal(("Order of {{Name}}", entry.Value.Version), (source.Value.Text, source.Value.Key.Version));
    }

    [Fact]
    public async Task GetTemplateAsync_MissingTemplate_GivesNotFound()
    {
        // Arrange
        await using var host = AHost();

        // Act
        var source = await host.Manager(TenantKey.None).GetTemplateAsync(ALookup(language: "hu"), Ct);

        // Assert
        Assert.Equal(404, source.Error?.Type.Code);
    }

    [Fact]
    public async Task GetTemplatesAsync_SavedTemplates_GivesTheirEntries()
    {
        // Arrange
        await using var host = AHost();
        var manager = host.Manager(TenantKey.None);
        var subject = await manager.SaveTemplateAsync(ALookup(), "Subject", 0, Ct);
        var body = await manager.SaveTemplateAsync(ALookup(part: "body", kind: TemplatePartKind.Html), "<p>Body</p>", 0, Ct);

        // Act
        var entries = await manager.GetTemplatesAsync(new TemplateQuery(_type), Ct);

        // Assert
        Assert.Equal([body.Value, subject.Value], entries.Value);
    }

    [Fact]
    public async Task SaveTemplateAsync_OldVersion_GivesConflict()
    {
        // Arrange
        await using var host = AHost();
        var manager = host.Manager(TenantKey.None);
        var first = await manager.SaveTemplateAsync(ALookup(), "First", 0, Ct);
        await manager.SaveTemplateAsync(ALookup(), "Second", first.Value.Version, Ct);

        // Act
        var result = await manager.SaveTemplateAsync(ALookup(), "Third", first.Value.Version, Ct);

        // Assert
        Assert.Equal(409, result.Error?.Type.Code);
    }

    [Fact]
    public async Task EachMethod_EmbeddedStore_ReadsButCannotListSaveOrDelete()
    {
        // Arrange
        await using var host = new ManagerHost(static b => b
            .UseEmbeddedTemplates(Assembly.GetExecutingAssembly(), EmbeddedTemplateFiles.Prefix)
            .AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>));
        var manager = host.Manager(TenantKey.None);
        var lookup = new TemplateLookup(new MessageType("TestEvent"), "en", Scope: null, "subject", TemplatePartKind.PlainText);

        // Act
        var read = await manager.GetTemplateAsync(lookup, Ct);
        var list = await manager.GetTemplatesAsync(new TemplateQuery(), Ct);
        var save = await manager.SaveTemplateAsync(lookup, "Order", 0, Ct);
        var delete = await manager.DeleteTemplateAsync(lookup, Ct);

        // Assert
        Assert.Equal(
            ("Order {{OrderNumber}} shipped", 400, 400, 400),
            (read.Value?.Text, list.Error?.Type.Code, save.Error?.Type.Code, delete?.Type.Code));
    }

    [Fact]
    public async Task SaveTemplateAsync_ManagerOfATenant_WritesIntoTheStoreOfTheTenantAndNotIntoTheSharedStore()
    {
        // Arrange
        await using var host = AHost();

        // Act
        await host.Manager(_t1).SaveTemplateAsync(ALookup(), "Tenant text", 0, Ct);

        // Assert
        var tenant = await host.Manager(_t1).GetTemplateAsync(ALookup(), Ct);
        var shared = await host.Manager(TenantKey.None).GetTemplateAsync(ALookup(), Ct);
        Assert.Equal(
            ("Tenant text", 404, true),
            (tenant.Value?.Text, shared.Error?.Type.Code, File.Exists(Path.Combine(folder.Parent, "tenants", "t1", $"{_type.Name}.en.subject.txt"))));
    }

    [Fact]
    public async Task EachMethod_TenantWithNoTemplateStore_GivesNotFound()
    {
        // Arrange
        await using var host = new ManagerHost(b => b.UseFileTemplates(folder.Root).AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>, _type.Name));
        var manager = host.Manager(_t1);

        // Act
        var list = await manager.GetTemplatesAsync(new TemplateQuery(), Ct);
        var read = await manager.GetTemplateAsync(ALookup(), Ct);
        var save = await manager.SaveTemplateAsync(ALookup(), "Text", 0, Ct);
        var delete = await manager.DeleteTemplateAsync(ALookup(), Ct);

        // Assert
        Assert.Equal((404, 404, 404, 404), (list.Error?.Type.Code, read.Error?.Type.Code, save.Error?.Type.Code, delete?.Type.Code));
    }

    [Fact]
    public async Task SaveTemplateAsync_ManagerOfATenant_ChecksTheTextWithTheEngineOfThatTenant()
    {
        // Arrange
        var engines = new Dictionary<TenantKey, ITemplateEngine<OrderEmail>>
        {
            [TenantKey.None] = Substitute.For<ITemplateEngine<OrderEmail>>(),
            [_t1] = Substitute.For<ITemplateEngine<OrderEmail>>(),
        };
        await using var host = new ManagerHost(b => b
            .UseTenantTemplateStore((_, key) => new FileSystemTemplateStore(Path.Combine(folder.Parent, "tenants", key.Tenant!.Value.Value)))
            .AddOrderEmailEngine((_, key) => engines[key], _type.Name));

        // Act
        await host.Manager(_t1).SaveTemplateAsync(ALookup(), "Text", 0, Ct);

        // Assert
        await engines[_t1].Received(1).CheckAsync(ALookup(), "Text", Arg.Any<CancellationToken>());
        Assert.Empty(engines[TenantKey.None].ReceivedCalls());
    }

    [Fact]
    public async Task SaveTemplateAsync_TwoPayloadTypesOfOneMessageType_ChecksTheTextOnlyWithTheEngineThatReadsThePart()
    {
        // Arrange
        var orders = Substitute.For<ITemplateEngine<OrderEmail>>();
        var notes = Substitute.For<ITemplateEngine<string>>();
        notes.CheckAsync(default, default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(new Error(Errors.TemplateInvalid)));
        await using var host = new ManagerHost(b => b.UseFileTemplates(folder.Root).AddMessage(_type.Name, 1, TestJsonContext.Default.TestEvent, m => m
            .WithPayload<OrderEmail>(
                static (_, _) => Substitute.For<IPayloadLoader<TestEvent, OrderEmail>>(),
                p => p.AddTemplateEngine("Email:Orders", [new TemplatePart("subject", TemplatePartKind.PlainText)], (_, _) => orders))
            .WithPayload<string>(
                static (_, _) => Substitute.For<IPayloadLoader<TestEvent, string>>(),
                p => p.AddTemplateEngine("Sms:Notes", [new TemplatePart("sms", TemplatePartKind.PlainText)], (_, _) => notes))));
        var manager = host.Manager(TenantKey.None);

        // Act
        var subject = await manager.SaveTemplateAsync(ALookup(), "Subject", 0, Ct);
        var sms = await manager.SaveTemplateAsync(ALookup(part: "sms"), "Note", 0, Ct);

        // Assert
        Assert.Equal((null, 422), (subject.Error, sms.Error?.Type.Code));
        await orders.Received(1).CheckAsync(Arg.Any<TemplateLookup>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        await notes.Received(1).CheckAsync(Arg.Any<TemplateLookup>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
    }

    private ManagerHost AHost() =>
        new(b => b
            .UseFileTemplates(folder.Root)
            .UseTenantTemplateStore((_, key) => new FileSystemTemplateStore(Path.Combine(folder.Parent, "tenants", key.Tenant!.Value.Value)))
            .AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>, _type.Name));

    private TemplateLookup ALookup(
        string language = "en", string? scope = null, string part = "subject", TemplatePartKind kind = TemplatePartKind.PlainText) =>
        new(_type, language, scope, part, kind);

    private async Task<string> RenderAsync(ITemplateEngine<OrderEmail> engine)
    {
        var template = await engine.PrepareAsync(new TemplateRequest(_type, "en", Scope: null, "subject", TemplatePartKind.PlainText), Ct);
        var rendered = await template.Value.RenderAsync(
            new OrderEmail("Client 1", 3, 1234.5m, DateTime.UnixEpoch), CultureInfo.GetCultureInfo("en"), Ct);
        return rendered.Value;
    }

    private string SharedPathOf(string partFile) => Path.Combine(folder.Root, $"{_type.Name}.en.{partFile}");
}
