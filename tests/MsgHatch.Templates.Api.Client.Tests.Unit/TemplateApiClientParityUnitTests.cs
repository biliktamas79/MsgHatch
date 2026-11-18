using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Templates.Api.Client.DependencyInjection;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.Api.Client.Tests.Unit.TestTypes;
using MsgHatch.Templates.Management;
using MsgHatch.Templates.Placeholders;
using MsgHatch.Templates.Tests.Unit.TestTypes;

namespace MsgHatch.Templates.Api.Client.Tests.Unit;

/// <summary>
/// The parity of the HTTP client and the in-process implementation (D233, D234): the same failing input gives an error
/// equal to the error of the in-process implementation with no exception, also with the same message.
/// </summary>
public sealed class TemplateApiClientParityUnitTests
{
    private static readonly MessageType _type = new("Parity");

    private static readonly Dictionary<string, Func<ITemplateQueries, ITemplateCommands, Task<Error?>>> _operations = new()
    {
        ["a read of a missing template"] = static async (queries, _) => (await queries.GetTemplateAsync(ALookup("hu"), Ct)).Error,
        ["a read with an unknown language"] = static async (queries, _) => (await queries.GetTemplateAsync(ALookup("xx"), Ct)).Error,
        ["a read with an empty language"] = static async (queries, _) => (await queries.GetTemplateAsync(ALookup(""), Ct)).Error,
        ["a read with a part that is not a name"] = static async (queries, _) => (await queries.GetTemplateAsync(ALookup(part: "sub ject"), Ct)).Error,
        ["a list with an empty company id"] = static async (queries, _) => (await queries.GetTemplatesAsync(new TemplateQuery(_type, ""), Ct)).Error,
        ["a check of a message type with no registration"] = static async (queries, _) =>
            (await queries.CheckTemplateAsync(ALookup() with { Type = new("Unregistered") }, "Text", Ct)).Error,
        ["a check of a part that no registration declares"] = static async (queries, _) => (await queries.CheckTemplateAsync(ALookup(part: "footer"), "Text", Ct)).Error,
        ["a save of a text with an unknown placeholder"] = static async (_, commands) =>
            (await commands.SaveTemplateAsync(ALookup(), "Order of {{Nmae}}", 0, Ct)).Error,
        ["a save of a text that is too long"] = static async (_, commands) =>
            (await commands.SaveTemplateAsync(ALookup(), new string('x', 1_000_001), 0, Ct)).Error,
        ["a save of a new template that is stored"] = static async (_, commands) => (await commands.SaveTemplateAsync(ALookup(), "Second", 0, Ct)).Error,
        ["a delete with a company id with a control character"] = static async (_, commands) =>
            await commands.DeleteTemplateAsync(ALookup(scope: "acme\u0001"), Ct),
        ["a read of the tenant t1 with no template store"] = static async (queries, _) => (await queries.GetTemplateAsync(ALookup(), Ct)).Error,
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> OperationRows => [.. _operations.Keys];

    [Theory]
    [MemberData(nameof(OperationRows))]
    public async Task Operation_FailingInput_GivesTheErrorOfTheInProcessImplementationWithNoException(string row)
    {
        // Arrange
        using var localFolder = new TemplateFolder();
        using var serverFolder = new TemplateFolder();
        localFolder.Write($"{_type.Name}.en.subject.txt", "Stored", DateTime.UnixEpoch);
        serverFolder.Write($"{_type.Name}.en.subject.txt", "Stored", DateTime.UnixEpoch);
        var key = row.Contains("t1", StringComparison.Ordinal) ? TenantKey.For(new TenantId("t1")) : TenantKey.None;
        await using var local = await InProcessManagementHost.StartAsync<ITemplateQueries>(ServicesOn(localFolder));
        await using var http = await HttpManagementHost.StartAsync<ITemplateQueries>(
            ServicesOn(serverFolder), static (services, configureHttp) => services.AddClientHost().UseTemplateApiClient(HttpManagementHost.BaseAddress, configureHttp));

        // Act
        var localError = await _operations[row](local.Service(key), local.Server.GetRequiredKeyedService<ITemplateCommands>(key));
        var clientError = await _operations[row](http.Service(key), http.Client.GetRequiredKeyedService<ITemplateCommands>(key));

        // Assert
        Assert.Equal((row, true, localError! with { Exception = null }), (row, localError is not null, clientError));
    }

    private static Action<IServiceCollection> ServicesOn(TemplateFolder folder) =>
        ManagerHost.Services(b => b.UseFileTemplates(folder.Root).AddOrderEmailEngine(PlaceholderTemplates.Engine<OrderEmail>, _type.Name));

    private static TemplateLookup ALookup(string language = "en", string? scope = null, string part = "subject") =>
        new(_type, language, scope, part, TemplatePartKind.PlainText);
}
