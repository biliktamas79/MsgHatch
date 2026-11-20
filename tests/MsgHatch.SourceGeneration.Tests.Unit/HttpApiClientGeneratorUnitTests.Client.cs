using System.Net;
using System.Text;
using MsgHatch.Domain.Failures;
using MsgHatch.SourceGeneration.Tests.Unit.TestTypes;
using MsgHatch.Templates;
using MsgHatch.Templates.Api.Contracts;

namespace MsgHatch.SourceGeneration.Tests.Unit;

/// <summary>The generated client <c>ShapesApi</c>: the request of each shape, and the error of each failure (D233, D236).</summary>
public sealed partial class HttpApiClientGeneratorUnitTests
{
    private const string _entryJson = """{"messageType":"A","language":"en","companyId":null,"part":"subject","kind":"PlainText","version":7}""";

    private static readonly Guid _id = new("0b9d3a4e-1c2f-4e5a-9b8c-7d6e5f4a3b2c");

    private static readonly Guid _otherId = new("5f1e2d3c-4b5a-4c6d-8e7f-9a0b1c2d3e4f");

    private static readonly ErrorType _consumerType = new(470, "Custom", IsTransient: true);

    public static TheoryData<string, HttpStatusCode, string?, Error> ErrorRows => new()
    {
        { "a body with errorType", HttpStatusCode.NotFound, Problem(404, "NotFound", "No template."), new Error(Errors.NotFound, "No template.") },
        { "an error type of the consumer", (HttpStatusCode)470, Problem(470, "Custom", "Custom."), new Error(_consumerType, "Custom.") },
        { "a 401 with a default problem body", HttpStatusCode.Unauthorized, """{"title":"Unauthorized","status":401}""", new Error(Errors.Rejected) },
        { "a 403 with a default problem body", HttpStatusCode.Forbidden, """{"title":"Forbidden","status":403}""", new Error(Errors.Rejected) },
        { "a 404 with a default problem body", HttpStatusCode.NotFound, """{"title":"Not Found","status":404,"detail":"No route."}""", new Error(Errors.Exception, "No route.") },
        { "a 429 with no body", HttpStatusCode.TooManyRequests, null, new Error(Errors.RateLimited) },
        { "a 503 with no body", HttpStatusCode.ServiceUnavailable, null, new Error(Errors.Unavailable) },
        { "a 400 with a text body", HttpStatusCode.BadRequest, "Bad request", new Error(Errors.Exception) },
        { "a 409 with a JSON array", HttpStatusCode.Conflict, "[]", new Error(Errors.Exception) },
        { "an errorType that does not parse", HttpStatusCode.Conflict, """{"detail":"Old.","errorType":{"code":"409"}}""", new Error(Errors.Exception, "Old.") },
    };

    [Fact]
    public async Task GetAsync_ValueOfEachShape_WritesEachRouteValueAndEachQueryValueEscaped()
    {
        // Arrange
        var (api, handler) = AnApi(static _ => Json(HttpStatusCode.OK, _entryJson));

        // Act
        await api.GetAsync("a b/c", _id, 7, true, TemplatePartKind.Html, "x&y", _id, 42, false, TemplatePartKind.PlainText, 1.5m, Ct);

        // Assert
        var request = Assert.Single(handler.Requests).Request;
        Assert.Equal(
            (HttpMethod.Get, $"http://host/api/shapes/items/a%20b%2Fc/{_id}/7/true/Html?text=x%26y&key={_id}&number=42&enabled=false&part=PlainText&amount=1.5"),
            (request.Method, request.RequestUri!.AbsoluteUri));
    }

    [Fact]
    public async Task GetAsync_NullQueryValues_LeavesThemOut()
    {
        // Arrange
        var (api, handler) = AnApi(static _ => Json(HttpStatusCode.OK, _entryJson));

        // Act
        var entry = await api.GetAsync("a", _id, 0, false, TemplatePartKind.PlainText, null, null, null, null, null, 0m, Ct);

        // Assert
        Assert.Equal(
            ($"http://host/api/shapes/items/a/{_id}/0/false/PlainText?amount=0", 7),
            (Assert.Single(handler.Requests).Request.RequestUri!.AbsoluteUri, entry.Value.Version));
    }

    [Fact]
    public async Task SaveAsync_Body_SendsItAsJsonAndReadsTheResponseBody()
    {
        // Arrange
        var (api, handler) = AnApi(static _ => Json(HttpStatusCode.OK, _entryJson));

        // Act
        var entry = await api.SaveAsync(new TemplateSaveRequest("Order", 3), Ct);

        // Assert
        var (request, body) = Assert.Single(handler.Requests);
        Assert.Equal(
            (HttpMethod.Put, "http://host/api/shapes/items", "application/json", """{"text":"Order","version":3}""", "A"),
            (request.Method, request.RequestUri!.AbsoluteUri, request.Content!.Headers.ContentType!.MediaType, body, entry.Value.MessageType));
    }

    [Theory]
    [InlineData(null, "http://host/api/shapes")]
    [InlineData("t1", "http://host/api/shapes?tenantId=t1")]
    public async Task DeleteAsync_NoContent_GivesNullForTheRouteOfThePrefix(string? tenantId, string expected)
    {
        // Arrange
        var (api, handler) = AnApi(static _ => new HttpResponseMessage(HttpStatusCode.NoContent));

        // Act
        var error = await api.DeleteAsync(tenantId);

        // Assert
        Assert.Equal((null, HttpMethod.Delete, expected), (error, handler.Requests[0].Request.Method, handler.Requests[0].Request.RequestUri!.AbsoluteUri));
    }

    public static TheoryData<string, Guid[]?, string> ListRows => new()
    {
        { "two items", [_id, _otherId], $"?ids={_id}&ids={_otherId}" },
        { "one item", [_id], $"?ids={_id}" },
        { "an empty list", [], "" },
        { "null", null, "" },
    };

    [Theory]
    [MemberData(nameof(ListRows))]
    public async Task ListAsync_ListQueryValue_WritesOnePairForEachItem(string row, Guid[]? ids, string expected)
    {
        // Arrange
        var (api, handler) = AnApi(static _ => new HttpResponseMessage(HttpStatusCode.NoContent));

        // Act
        await api.ListAsync(ids, [], from: null, Ct);

        // Assert
        Assert.Equal((row, "http://host/api/shapes/lists" + expected), (row, Assert.Single(handler.Requests).Request.RequestUri!.AbsoluteUri));
    }

    [Fact]
    public async Task ListAsync_StringsAndATime_WritesEachStringEscapedAndTheTimeInTheFormatO()
    {
        // Arrange
        var (api, handler) = AnApi(static _ => new HttpResponseMessage(HttpStatusCode.NoContent));
        var from = new DateTime(2030, 1, 1, 10, 0, 0, DateTimeKind.Utc).AddTicks(1234567);

        // Act
        await api.ListAsync(ids: null, ["a b", "c&d"], from, Ct);

        // Assert
        Assert.Equal(
            "http://host/api/shapes/lists?names=a%20b&names=c%26d&from=2030-01-01T10%3A00%3A00.1234567Z",
            Assert.Single(handler.Requests).Request.RequestUri!.AbsoluteUri);
    }

    [Theory]
    [MemberData(nameof(ErrorRows))]
    public async Task SaveAndDeleteAsync_FailedResponse_GiveTheErrorOfErrorProblems(string row, HttpStatusCode status, string? body, Error expected)
    {
        // Arrange
        var (api, _) = AnApi(_ => body is null ? new HttpResponseMessage(status) : Json(status, body));

        // Act
        var save = await api.SaveAsync(new TemplateSaveRequest("Order", 0), Ct);
        var delete = await api.DeleteAsync(tenantId: null);

        // Assert
        Assert.Equal((row, expected, expected), (row, save.Error, delete));
    }

    [Theory]
    [InlineData("{", "not JSON")]
    [InlineData("null", "the JSON null")]
    [InlineData("""{"messageType":"A"}""", "no required property")]
    public async Task SaveAsync_SuccessBodyThatDoesNotParse_GivesSerializationFailed(string body, string row)
    {
        // Arrange
        var (api, _) = AnApi(_ => Json(HttpStatusCode.OK, body));

        // Act
        var save = await api.SaveAsync(new TemplateSaveRequest("Order", 0), Ct);

        // Assert
        Assert.Equal((row, Errors.SerializationFailed), (row, save.Error?.Type));
    }

    [Fact]
    public async Task SaveAndDeleteAsync_HandlerThrowsHttpRequestException_GiveUnavailableWithTheException()
    {
        // Arrange
        var exception = new HttpRequestException(HttpRequestError.ConnectionError);
        var (api, _) = AnApi(_ => throw exception);

        // Act
        var save = await api.SaveAsync(new TemplateSaveRequest("Order", 0), Ct);
        var delete = await api.DeleteAsync(tenantId: null);

        // Assert
        Assert.Equal((Errors.Unavailable, exception, Errors.Unavailable), (save.Error?.Type, save.Error?.Exception, delete?.Type));
    }

    [Fact]
    public async Task SaveAsync_TimeoutOfTheClient_GivesUnavailable()
    {
        // Arrange
        var (api, _) = AnApi(static _ => throw new TaskCanceledException("The request timed out.", new TimeoutException()));

        // Act
        var save = await api.SaveAsync(new TemplateSaveRequest("Order", 0), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, save.Error?.Type);
    }

    [Fact]
    public async Task SaveAsync_CancelledToken_GivesOperationCancelled()
    {
        // Arrange
        var (api, _) = AnApi(static _ => Json(HttpStatusCode.OK, _entryJson));
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        // Act
        var save = await api.SaveAsync(new TemplateSaveRequest("Order", 0), cancellation.Token);

        // Assert
        Assert.Equal(new Error(Errors.OperationCancelled), save.Error);
    }

    private static (ShapesApi Api, StubHandler Handler) AnApi(Func<HttpRequestMessage, HttpResponseMessage> respond)
    {
        var handler = new StubHandler(respond);
        return (new ShapesApi(new HttpClient(handler) { BaseAddress = new Uri("http://host/api/") }, "/shapes/"), handler);
    }

    private static HttpResponseMessage Json(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };

    private static string Problem(int code, string name, string detail) =>
        $$$"""{"title":"{{{name}}}","status":{{{code}}},"detail":"{{{detail}}}","errorType":{"code":{{{code}}},"name":"{{{name}}}","isTransient":{{{(code == 470 ? "true" : "false")}}}}}""";
}
