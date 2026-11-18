using System.Net;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.Api.Client.DependencyInjection;
using MsgHatch.Templates.Api.Client.Tests.Unit.TestTypes;
using MsgHatch.Templates.DependencyInjection;
using MsgHatch.Templates.Management;
using MsgHatch.Templates.Placeholders;
using MsgHatch.Templates.Tests.Unit.TestTypes;

namespace MsgHatch.Templates.Api.Client.Tests.Unit;

/// <summary>
/// The requests of the template API client: the rules before a request (D234), the tenant of a key, the errors of a wrong
/// prefix and of a response that the contract does not allow (D233), and the log of the client host.
/// </summary>
/// <param name="folder">The temporary template folder of the test class.</param>
public sealed class TemplateApiClientRequestsUnitTests(TemplateFolder folder) : IClassFixture<TemplateFolder>
{
    private static readonly MessageType _type = new("Requests");

    private static readonly Dictionary<string, Func<ITemplateQueries, ITemplateCommands, Task<Error?>>> _brokenRules = new()
    {
        ["a list with an empty company id"] = static async (queries, _) => (await queries.GetTemplatesAsync(new TemplateQuery(Scope: ""), Ct)).Error,
        ["a read with a part with a space"] = static async (queries, _) => (await queries.GetTemplateAsync(ALookup(part: "sub ject"), Ct)).Error,
        ["a read with an empty language"] = static async (queries, _) => (await queries.GetTemplateAsync(ALookup(language: ""), Ct)).Error,
        ["a check with a company id with a control character"] = static async (queries, _) =>
            (await queries.CheckTemplateAsync(ALookup(scope: "acme\u0001"), "Text", Ct)).Error,
        ["a check of a text that is too long"] = static async (queries, _) => (await queries.CheckTemplateAsync(ALookup(), new string('x', 1_000_001), Ct)).Error,
        ["a preview with a part with a space"] = static async (queries, _) => (await queries.PreviewTemplateAsync(ALookup(part: "sub ject"), "Text", Ct)).Error,
        ["a preview of a text that is too long"] = static async (queries, _) =>
            (await queries.PreviewTemplateAsync(ALookup(), new string('x', 1_000_001), Ct)).Error,
        ["a save of a text that is too long"] = static async (_, commands) => (await commands.SaveTemplateAsync(ALookup(), new string('x', 1_000_001), 0, Ct)).Error,
        ["a save with a company id whose folder name has 256 characters"] = static async (_, commands) =>
            (await commands.SaveTemplateAsync(ALookup(scope: new string('A', 128)), "Text", 0, Ct)).Error,
        ["a delete with a part that starts with a digit"] = static async (_, commands) => await commands.DeleteTemplateAsync(ALookup(part: "1subject"), Ct),
    };

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public static TheoryData<string> BrokenRuleRows => [.. _brokenRules.Keys];

    [Theory]
    [MemberData(nameof(BrokenRuleRows))]
    public async Task Operation_InputThatBreaksARule_GivesValidationFailedWithNoRequest(string row)
    {
        // Arrange
        var handler = new StubHandler(HttpStatusCode.OK, "[]");
        await using var provider = ClientOn(handler);
        await using var scope = provider.CreateAsyncScope();

        // Act
        var error = await _brokenRules[row](scope.ServiceProvider.GetRequiredService<ITemplateQueries>(), scope.ServiceProvider.GetRequiredService<ITemplateCommands>());

        // Assert
        Assert.Equal((row, Errors.ValidationFailed, 0), (row, error?.Type, handler.Uris.Count));
    }

    [Fact]
    public async Task Service_TenantKey_SendsTheTenantIdOnlyForATenant()
    {
        // Arrange
        var handler = new StubHandler(HttpStatusCode.OK, "[]");
        await using var provider = ClientOn(handler);
        await using var scope = provider.CreateAsyncScope();

        // Act
        await scope.ServiceProvider.GetRequiredKeyedService<ITemplateQueries>(TenantKey.None).GetTemplatePartsAsync(type: null, Ct);
        await scope.ServiceProvider.GetRequiredKeyedService<ITemplateQueries>(TenantKey.For(new TenantId("t 1"))).GetTemplatePartsAsync(type: null, Ct);

        // Assert
        Assert.Equal("/templates/parts, /templates/parts?tenantId=t%201", string.Join(", ", handler.Uris.Select(static uri => uri.PathAndQuery)));
    }

    [Theory]
    [InlineData("[null]", "a null item")]
    [InlineData("""[{"messageType":"1A","language":"en","companyId":null,"part":"subject","kind":"PlainText","version":1}]""", "a message type that is not a name")]
    [InlineData("""{"messageType":"A"}""", "an object in place of a list")]
    public async Task GetTemplatesAsync_ResponseThatTheContractDoesNotAllow_GivesSerializationFailed(string json, string row)
    {
        // Arrange
        await using var provider = ClientOn(new StubHandler(HttpStatusCode.OK, json));
        await using var scope = provider.CreateAsyncScope();

        // Act
        var entries = await scope.ServiceProvider.GetRequiredService<ITemplateQueries>().GetTemplatesAsync(new TemplateQuery(), Ct);

        // Assert
        Assert.Equal((row, Errors.SerializationFailed), (row, entries.Error?.Type));
    }

    [Theory]
    [InlineData("[null]", "a null item")]
    [InlineData("""[{"registration":"","text":"Order"}]""", "an empty registration")]
    [InlineData("""[{"registration":"Email:A","text":null}]""", "a null text")]
    public async Task PreviewTemplateAsync_ResponseThatTheContractDoesNotAllow_GivesSerializationFailed(string json, string row)
    {
        // Arrange
        await using var provider = ClientOn(new StubHandler(HttpStatusCode.OK, json));
        await using var scope = provider.CreateAsyncScope();

        // Act
        var previews = await scope.ServiceProvider.GetRequiredService<ITemplateQueries>().PreviewTemplateAsync(ALookup(), "Text", Ct);

        // Assert
        Assert.Equal((row, Errors.SerializationFailed), (row, previews.Error?.Type));
    }

    [Fact]
    public async Task PreviewTemplateAsync_LookupAndText_PostsTheTextToThePreviewRouteOfTheLookup()
    {
        // Arrange
        var handler = new StubHandler(HttpStatusCode.OK, """[{"registration":"Email:Requests","text":"Order of Sample client"}]""");
        await using var provider = ClientOn(handler);
        await using var scope = provider.CreateAsyncScope();

        // Act
        var previews = await scope.ServiceProvider.GetRequiredService<ITemplateQueries>().PreviewTemplateAsync(ALookup(scope: "acme-1"), "Order of {{Name}}", Ct);

        // Assert
        Assert.Equal(
            ("/templates/Requests/en/subject/PlainText/preview?companyId=acme-1", new TemplatePreview("Email:Requests", "Order of Sample client")),
            (Assert.Single(handler.Uris).PathAndQuery, Assert.Single(previews.Value)));
    }

    [Fact]
    public async Task Operation_PrefixThatTheHostDoesNotMap_GivesExceptionAndNotNotFound()
    {
        // Arrange
        await using var host = await HttpManagementHost.StartAsync<ITemplateQueries>(
            ServerServices(),
            static (services, configureHttp) => services.AddClientHost().UseTemplateApiClient(
                static o => { o.BaseAddress = HttpManagementHost.BaseAddress; o.Prefix = "/wrong"; }, configureHttp));

        // Act
        var parts = await host.Service(TenantKey.None).GetTemplatePartsAsync(type: null, Ct);

        // Assert
        Assert.Equal(Errors.Exception, parts.Error?.Type);
    }

    [Fact]
    public async Task Operations_CompanyIdAndTemplateText_NoLogRecordOfTheClientHostHoldsThem()
    {
        // Arrange
        await using var host = await HttpManagementHost.StartAsync<ITemplateQueries>(
            ServerServices(), static (services, configureHttp) => services.AddClientHost().UseTemplateApiClient(HttpManagementHost.BaseAddress, configureHttp));
        var queries = host.Service(TenantKey.None);
        var commands = host.Client.GetRequiredService<ITemplateCommands>();
        var lookup = ALookup(scope: "acme-secret");

        // Act
        await commands.SaveTemplateAsync(lookup, "Secret text", 0, Ct);
        await queries.GetTemplatesAsync(new TemplateQuery(_type, "acme-secret"), Ct);
        await queries.GetTemplateAsync(lookup, Ct);
        await queries.CheckTemplateAsync(lookup, "Secret {{Nmae}}", Ct);
        await queries.PreviewTemplateAsync(lookup, "Secret {{Name}}", Ct);
        await commands.DeleteTemplateAsync(lookup, Ct);

        // Assert
        var records = host.Client.LogRecords();
        Assert.Equal(
            (true, true, false),
            (records.Any(static record => record.Category?.StartsWith("System.Net.Http.HttpClient.MsgHatch.TemplateApi", StringComparison.Ordinal) == true),
                records.Any(static record => record.Id.Id is >= 2017 and <= 2030),
                records.Any(static record => record.ContainsText("acme-secret") || record.ContainsText("Secret"))));
    }

    private static ServiceProvider ClientOn(StubHandler handler) =>
        new ServiceCollection().AddClientHost()
            .UseTemplateApiClient(HttpManagementHost.BaseAddress, http => http.ConfigurePrimaryHttpMessageHandler(() => handler))
            .Services.Build();

    private Action<IServiceCollection> ServerServices() =>
        ManagerHost.Services(b => b.UseFileTemplates(folder.Root).AddOrderEmailEngine(
            PlaceholderTemplates.Engine<OrderEmail>, _type.Name, samples: [TemplateEngineRegistrations.ASample("Secret sample")]));

    private static TemplateLookup ALookup(string language = "en", string? scope = null, string part = "subject") =>
        new(_type, language, scope, part, TemplatePartKind.PlainText);
}
