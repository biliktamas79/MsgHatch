using System.Net;
using System.Text;
using System.Text.Json;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.AspNetCore.Tests.Unit.TestTypes;
using MsgHatch.Templates.Management;
using NSubstitute;
using NSubstitute.Extensions;

namespace MsgHatch.Templates.AspNetCore.Tests.Unit;

/// <summary>The template endpoints (section 4.22, D224) on a <c>TestServer</c> with an <see cref="ITemplateManager"/> substitute for each key.</summary>
public sealed class TemplateEndpointsUnitTests : IAsyncLifetime
{
    private const string _path = "/templates/OrderShipped/hu/subject/PlainText";

    private TemplateEndpointHost _host = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _host = await TemplateEndpointHost.StartAsync();

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    public static TheoryData<string, string, string?, string> RouteRows => new()
    {
        { "GET", "/templates", null, nameof(ITemplateManager.GetTemplatesAsync) },
        { "GET", _path, null, nameof(ITemplateManager.GetTemplateAsync) },
        { "PUT", _path, """{"text":"Rendelés","version":0}""", nameof(ITemplateManager.SaveTemplateAsync) },
        { "DELETE", _path, null, nameof(ITemplateManager.DeleteTemplateAsync) },
    };

    public static TheoryData<string, string, string> InvalidValueRows => new()
    {
        { "a message type that is not a name", "GET", "/templates/1Order/hu/subject/PlainText" },
        { "a kind in small letters", "GET", "/templates/OrderShipped/hu/subject/plaintext" },
        { "an unknown kind", "PUT", "/templates/OrderShipped/hu/subject/Text" },
        { "an unknown kind of a delete", "DELETE", "/templates/OrderShipped/hu/subject/Markdown" },
        { "an empty tenant id", "GET", $"{_path}?tenantId=" },
        { "a tenant id with a control character", "DELETE", $"{_path}?tenantId=t%01" },
        { "a message type filter that is not a name", "GET", "/templates?messageType=Order-Shipped" },
        { "an empty tenant id of a list", "GET", "/templates?tenantId=" },
    };

    public static TheoryData<string, string> InvalidBodyRows => new()
    {
        { "invalid JSON", "{" },
        { "the JSON null", "null" },
        { "no version", """{"text":"Rendelés"}""" },
        { "no text", """{"version":0}""" },
        { "a null text", """{"text":null,"version":0}""" },
        { "a version as a string", """{"text":"Rendelés","version":"1"}""" },
    };

    public static TheoryData<int, HttpStatusCode> ErrorRows => new()
    {
        { 400, HttpStatusCode.BadRequest },
        { 404, HttpStatusCode.NotFound },
        { 409, HttpStatusCode.Conflict },
        { 422, HttpStatusCode.UnprocessableEntity },
        { 500, HttpStatusCode.InternalServerError },
        { 503, HttpStatusCode.InternalServerError },
    };

    [Theory]
    [MemberData(nameof(RouteRows))]
    public async Task Route_Request_CallsTheMatchingManagerMethodOneTime(string method, string path, string? body, string expected)
    {
        // Act
        await _host.Client.SendAsync(RequestOf(method, path, body), Ct);

        // Assert
        Assert.Equal([expected], _host.Manager.ReceivedCalls().Select(static call => call.GetMethodInfo().Name));
    }

    [Theory]
    [MemberData(nameof(RouteRows))]
    public async Task Route_TenantId_CallsOnlyTheManagerOfThatTenant(string method, string path, string? body, string expected)
    {
        // Act
        await _host.Client.SendAsync(RequestOf(method, $"{path}?tenantId=t2", body), Ct);

        // Assert
        Assert.Equal([expected], _host.T2Manager.ReceivedCalls().Select(static call => call.GetMethodInfo().Name));
        Assert.Empty(_host.Manager.ReceivedCalls());
    }

    [Theory]
    [InlineData("/templates", null, null)]
    [InlineData("/templates?messageType=OrderShipped", "OrderShipped", null)]
    [InlineData("/templates?messageType=OrderShipped&companyId=42", "OrderShipped", "42")]
    [InlineData("/templates?companyId=acme-1", null, "acme-1")]
    public async Task GetTemplates_Query_GivesTheFiltersToTheManager(string path, string? messageType, string? companyId)
    {
        // Act
        await _host.Client.SendAsync(RequestOf("GET", path, null), Ct);

        // Assert
        await _host.Manager.Received(1).GetTemplatesAsync(
            new TemplateQuery(messageType is null ? null : new MessageType(messageType), companyId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PutTemplate_PathQueryAndBody_SavesTheLookupTheTextAndTheVersion()
    {
        // Act
        await _host.Client.SendAsync(RequestOf("PUT", $"{_path}?companyId=42", """{"text":"Rendelés {{Name}} ő","version":5}"""), Ct);

        // Assert
        await _host.Manager.Received(1).SaveTemplateAsync(
            new TemplateLookup(new MessageType("OrderShipped"), "hu", "42", "subject", TemplatePartKind.PlainText), "Rendelés {{Name}} ő", 5,
            Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData("GET")]
    [InlineData("DELETE")]
    public async Task Route_TemplatePathWithNoCompany_GivesTheLookupWithNoScope(string method)
    {
        // Act
        await _host.Client.SendAsync(RequestOf(method, "/templates/OrderShipped/en/body/Html", null), Ct);

        // Assert
        var lookup = Assert.Single(_host.Manager.ReceivedCalls()).GetArguments()[0];
        Assert.Equal(new TemplateLookup(new MessageType("OrderShipped"), "en", Scope: null, "body", TemplatePartKind.Html), lookup);
    }

    [Fact]
    public async Task GetTemplates_Entries_Gives200WithTheEntriesAndNoTemplateId()
    {
        // Act
        var response = await _host.Client.SendAsync(RequestOf("GET", "/templates", null), Ct);

        // Assert
        Assert.Equal(
            (HttpStatusCode.OK,
                """[{"messageType":"A","language":"en","companyId":null,"part":"subject","kind":"PlainText","version":5},{"messageType":"A","language":"hu","companyId":"42","part":"body","kind":"Html","version":6}]"""),
            (response.StatusCode, await response.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task GetTemplate_Source_Gives200WithTheLookupTheVersionAndTheTextAndNoTemplateId()
    {
        // Act
        var response = await _host.Client.SendAsync(RequestOf("GET", $"{_path}?companyId=42", null), Ct);

        // Assert
        Assert.Equal(
            (HttpStatusCode.OK,
                """{"messageType":"OrderShipped","language":"hu","companyId":"42","part":"subject","kind":"PlainText","version":5,"text":"Order of {{Name}}"}"""),
            (response.StatusCode, await response.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task PutTemplate_SavedEntry_Gives200WithTheEntry()
    {
        // Act
        var response = await _host.Client.SendAsync(RequestOf("PUT", _path, """{"text":"Rendelés","version":0}"""), Ct);

        // Assert
        Assert.Equal(
            (HttpStatusCode.OK, """{"messageType":"OrderShipped","language":"hu","companyId":null,"part":"subject","kind":"PlainText","version":7}"""),
            (response.StatusCode, await response.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task DeleteTemplate_NoError_Gives204()
    {
        // Act
        var response = await _host.Client.SendAsync(RequestOf("DELETE", _path, null), Ct);

        // Assert
        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    [Theory]
    [MemberData(nameof(InvalidValueRows))]
    public async Task Route_InvalidPathOrQueryValue_Gives400AndCallsNoManager(string row, string method, string path)
    {
        // Act
        var response = await _host.Client.SendAsync(RequestOf(method, path, method == "PUT" ? """{"text":"a","version":0}""" : null), Ct);

        // Assert
        Assert.Equal(
            (row, HttpStatusCode.BadRequest, 0),
            (row, response.StatusCode, _host.Manager.ReceivedCalls().Count() + _host.T2Manager.ReceivedCalls().Count()));
    }

    [Theory]
    [MemberData(nameof(InvalidBodyRows))]
    public async Task PutTemplate_InvalidBody_Gives400AndCallsNoManager(string row, string body)
    {
        // Act
        var response = await _host.Client.SendAsync(RequestOf("PUT", _path, body), Ct);

        // Assert
        Assert.Equal((row, HttpStatusCode.BadRequest, 0), (row, response.StatusCode, _host.Manager.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task PutTemplate_BodyThatIsNotJson_Gives415AndCallsNoManager()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Put, _path)
        {
            Content = new StringContent("""{"text":"Rendelés","version":0}""", Encoding.UTF8, "text/plain"),
        };

        // Act
        var response = await _host.Client.SendAsync(request, Ct);

        // Assert
        Assert.Equal((HttpStatusCode.UnsupportedMediaType, 0), (response.StatusCode, _host.Manager.ReceivedCalls().Count()));
    }

    [Theory]
    [MemberData(nameof(ErrorRows))]
    public async Task PutTemplate_ManagerError_GivesItsStatusWithAProblemDetailsOfTheErrorNameAndMessage(int errorCode, HttpStatusCode expected)
    {
        // Arrange
        var error = new Error(ErrorTypeOf(errorCode), "The manager failed.");
        _host.Manager.Configure().SaveTemplateAsync(default, default!, default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<TemplateEntry>.ForError(error)));

        // Act
        var response = await _host.Client.SendAsync(RequestOf("PUT", _path, """{"text":"Rendelés","version":0}"""), Ct);

        // Assert
        var problem = await ProblemOf(response);
        Assert.Equal((expected, error.Type.Name, "The manager failed."), (response.StatusCode, problem.Title, problem.Detail));
    }

    [Theory]
    [MemberData(nameof(ErrorRows))]
    public async Task EachOtherRoute_ManagerError_GivesItsStatus(int errorCode, HttpStatusCode expected)
    {
        // Arrange
        var error = new Error(ErrorTypeOf(errorCode));
        _host.Manager.Configure().GetTemplatesAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<TemplateEntry>>.ForError(error)));
        _host.Manager.Configure().GetTemplateAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<TemplateSource>.ForError(error)));
        _host.Manager.Configure().DeleteTemplateAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(error));

        // Act
        var list = await _host.Client.SendAsync(RequestOf("GET", "/templates", null), Ct);
        var read = await _host.Client.SendAsync(RequestOf("GET", _path, null), Ct);
        var delete = await _host.Client.SendAsync(RequestOf("DELETE", _path, null), Ct);

        // Assert
        Assert.Equal((expected, expected, expected), (list.StatusCode, read.StatusCode, delete.StatusCode));
    }

    private static ErrorType ErrorTypeOf(int code) =>
        code switch
        {
            400 => Errors.ValidationFailed,
            404 => Errors.NotFound,
            409 => Errors.Conflict,
            422 => Errors.TemplateInvalid,
            500 => Errors.Exception,
            _ => Errors.Unavailable,
        };

    private static async Task<(string? Title, string? Detail)> ProblemOf(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return (document.RootElement.GetProperty("title").GetString(), document.RootElement.GetProperty("detail").GetString());
    }

    private static HttpRequestMessage RequestOf(string method, string path, string? body) =>
        new(new HttpMethod(method), path) { Content = body is null ? null : new StringContent(body, Encoding.UTF8, "application/json") };
}
