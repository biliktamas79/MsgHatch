using System.Net;
using System.Text;
using System.Text.Json;
using MsgHatch.Api.Contracts;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.AspNetCore.Tests.Unit.TestTypes;
using MsgHatch.Templates.Management;
using NSubstitute;
using NSubstitute.Extensions;

namespace MsgHatch.Templates.AspNetCore.Tests.Unit;

/// <summary>The template endpoints (section 4.22, D224) on a <c>TestServer</c> with a substitute of both template interfaces for each key.</summary>
public sealed class TemplateEndpointsUnitTests : IAsyncLifetime
{
    private const string _path = "/templates/OrderShipped/hu/subject/PlainText";

    private TemplateEndpointHost _host = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _host = await TemplateEndpointHost.StartAsync();

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    public static TheoryData<string, string, string?, string> RouteRows => new()
    {
        { "GET", "/templates", null, nameof(ITemplateQueries.GetTemplatesAsync) },
        { "GET", _path, null, nameof(ITemplateQueries.GetTemplateAsync) },
        { "PUT", _path, """{"text":"Rendelés","version":0}""", nameof(ITemplateCommands.SaveTemplateAsync) },
        { "DELETE", _path, null, nameof(ITemplateCommands.DeleteTemplateAsync) },
        { "GET", "/templates/parts", null, nameof(ITemplateQueries.GetTemplatePartsAsync) },
        { "POST", $"{_path}/check", """{"text":"Rendelés"}""", nameof(ITemplateQueries.CheckTemplateAsync) },
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
        { "an unknown kind of a check", "POST", "/templates/OrderShipped/hu/subject/Text/check" },
        { "a message type filter of the parts that is not a name", "GET", "/templates/parts?messageType=1A" },
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
        { 403, HttpStatusCode.Forbidden },
        { 404, HttpStatusCode.NotFound },
        { 409, HttpStatusCode.Conflict },
        { 410, HttpStatusCode.Gone },
        { 415, HttpStatusCode.InternalServerError },
        { 422, HttpStatusCode.UnprocessableEntity },
        { 426, HttpStatusCode.InternalServerError },
        { 429, HttpStatusCode.TooManyRequests },
        { 460, HttpStatusCode.InternalServerError },
        { 499, HttpStatusCode.InternalServerError },
        { 500, HttpStatusCode.InternalServerError },
        { 503, HttpStatusCode.ServiceUnavailable },
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

    [Fact]
    public async Task GetTemplateParts_Parts_Gives200WithEachPartAndItsEngines()
    {
        // Act
        var response = await _host.Client.SendAsync(RequestOf("GET", "/templates/parts?messageType=A", null), Ct);

        // Assert
        await _host.Manager.Received(1).GetTemplatePartsAsync(new MessageType("A"), Arg.Any<CancellationToken>());
        Assert.Equal(
            (HttpStatusCode.OK, """[{"messageType":"A","part":"subject","kind":"PlainText","engines":["placeholder","liquid"]}]"""),
            (response.StatusCode, await response.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task PostCheck_PathQueryAndBody_Gives200WithTheIssuesOfTheLookupAndTheText()
    {
        // Act
        var response = await _host.Client.SendAsync(RequestOf("POST", $"{_path}/check?companyId=42", """{"text":"Rendelés {{Name"}"""), Ct);

        // Assert
        await _host.Manager.Received(1).CheckTemplateAsync(
            new TemplateLookup(new MessageType("OrderShipped"), "hu", "42", "subject", TemplatePartKind.PlainText), "Rendelés {{Name",
            Arg.Any<CancellationToken>());
        Assert.Equal(
            (HttpStatusCode.OK, """[{"message":"An issue.","line":2,"column":5},{"message":"Another issue.","line":null,"column":null}]"""),
            (response.StatusCode, await response.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task PostCheck_BodyThatIsNotJson_Gives415AndCallsNoManager()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Post, $"{_path}/check")
        {
            Content = new StringContent("""{"text":"Rendelés"}""", Encoding.UTF8, "text/plain"),
        };

        // Act
        var response = await _host.Client.SendAsync(request, Ct);

        // Assert
        Assert.Equal((HttpStatusCode.UnsupportedMediaType, 0), (response.StatusCode, _host.Manager.ReceivedCalls().Count()));
    }

    [Theory]
    [MemberData(nameof(InvalidValueRows))]
    public async Task Route_InvalidPathOrQueryValue_Gives400AndCallsNoManager(string row, string method, string path)
    {
        // Act
        var response = await _host.Client.SendAsync(RequestOf(method, path, method is "PUT" or "POST" ? """{"text":"a","version":0}""" : null), Ct);

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
    public async Task PutTemplate_ManagerError_GivesItsStatusWithAProblemDetailsOfTheErrorNameMessageAndErrorType(int errorCode, HttpStatusCode expected)
    {
        // Arrange
        var error = new Error(ErrorTypeOf(errorCode), "The manager failed.");
        _host.Manager.Configure().SaveTemplateAsync(default, default!, default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<TemplateEntry>.ForError(error)));

        // Act
        var response = await _host.Client.SendAsync(RequestOf("PUT", _path, """{"text":"Rendelés","version":0}"""), Ct);

        // Assert
        var problem = await ProblemOf(response);
        Assert.Equal(
            (expected, error.Type.Name, "The manager failed.", ErrorTypeBody.From(error.Type)),
            (response.StatusCode, problem.Title, problem.Detail, problem.ErrorType));
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
        _host.Manager.Configure().GetTemplatePartsAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<TemplatePartInfo>>.ForError(error)));
        _host.Manager.Configure().CheckTemplateAsync(default, default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<IReadOnlyList<TemplateIssue>>.ForError(error)));

        // Act
        var list = await _host.Client.SendAsync(RequestOf("GET", "/templates", null), Ct);
        var read = await _host.Client.SendAsync(RequestOf("GET", _path, null), Ct);
        var delete = await _host.Client.SendAsync(RequestOf("DELETE", _path, null), Ct);
        var parts = await _host.Client.SendAsync(RequestOf("GET", "/templates/parts", null), Ct);
        var check = await _host.Client.SendAsync(RequestOf("POST", $"{_path}/check", """{"text":"Rendelés"}"""), Ct);

        // Assert
        Assert.Equal(
            (expected, expected, expected, expected, expected),
            (list.StatusCode, read.StatusCode, delete.StatusCode, parts.StatusCode, check.StatusCode));
    }

    private static ErrorType ErrorTypeOf(int code) => Errors.Find(code)!;

    private static async Task<(string? Title, string? Detail, ErrorTypeBody? ErrorType)> ProblemOf(HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var root = document.RootElement;
        return (
            root.GetProperty("title").GetString(),
            root.GetProperty("detail").GetString(),
            root.TryGetProperty("errorType", out var errorType) ? errorType.Deserialize(ApiErrorJson.Default.ErrorTypeBody) : null);
    }

    private static HttpRequestMessage RequestOf(string method, string path, string? body) =>
        new(new HttpMethod(method), path) { Content = body is null ? null : new StringContent(body, Encoding.UTF8, "application/json") };
}
