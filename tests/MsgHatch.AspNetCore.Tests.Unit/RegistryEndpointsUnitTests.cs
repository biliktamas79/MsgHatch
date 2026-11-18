using System.Net;
using System.Text;
using System.Text.Json;
using MsgHatch.Api.Contracts;
using MsgHatch.AspNetCore.Tests.Unit.TestTypes;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Domain.Tests.Unit.TestTypes;
using NSubstitute;
using NSubstitute.Extensions;

namespace MsgHatch.AspNetCore.Tests.Unit;

/// <summary>The registry endpoints (section 4.20) on a <c>TestServer</c> with an <see cref="IRegistryWriter"/> substitute.</summary>
public sealed partial class RegistryEndpointsUnitTests : IAsyncLifetime
{
    private RegistryHost _host = null!;

    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    public async ValueTask InitializeAsync() => _host = await RegistryHost.StartAsync();

    public async ValueTask DisposeAsync() => await _host.DisposeAsync();

    public static TheoryData<string, string, string?, string> RouteRows => new()
    {
        { "GET", "/registry/channels", null, nameof(IRegistryWriter.GetEntriesAsync) },
        { "PUT", "/registry/channels/Test", """{"enabled":true,"revision":0}""", nameof(IRegistryWriter.SaveChannelAsync) },
        { "DELETE", "/registry/channels/Test", null, nameof(IRegistryWriter.ResetChannelAsync) },
        { "GET", "/registry/message-types", null, nameof(IRegistryWriter.GetEntriesAsync) },
        { "PUT", "/registry/message-types/TestEvent", """{"enabled":false,"revision":2}""", nameof(IRegistryWriter.SaveMessageTypeAsync) },
        { "DELETE", "/registry/message-types/TestEvent", null, nameof(IRegistryWriter.ResetMessageTypeAsync) },
    };

    [Theory]
    [MemberData(nameof(RouteRows))]
    public async Task Route_Request_CallsTheMatchingWriterMethodOneTime(string method, string path, string? body, string expected)
    {
        // Act
        await _host.Client.SendAsync(RequestOf(method, path, body), Ct);

        // Assert
        Assert.Equal([expected], _host.Writer.ReceivedCalls().Select(static call => call.GetMethodInfo().Name));
    }

    [Fact]
    public async Task PutChannel_Body_SavesTheEntryOfThePathAndTheBody()
    {
        // Arrange
        const string body = """{"displayName":"Shop email","description":"Értesítő ő","enabled":false,"revision":3}""";

        // Act
        await _host.Client.SendAsync(RequestOf("PUT", "/registry/channels/Test", body), Ct);

        // Assert
        await _host.Writer.Received(1).SaveChannelAsync(
            new ChannelEntry(TestChannel.Id, "Shop email", "Értesítő ő", Enabled: false, Revision: 3, default), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PutMessageType_Body_SavesTheEntryWithTheDisabledChannelsOfTheBody()
    {
        // Arrange
        const string body = """{"enabled":true,"revision":1,"disabledChannels":["Second","Test"]}""";

        // Act
        await _host.Client.SendAsync(RequestOf("PUT", "/registry/message-types/TestEvent", body), Ct);

        // Assert
        await _host.Writer.Received(1).SaveMessageTypeAsync(
            Arg.Is<MessageTypeEntry>(entry =>
                entry.Type == new MessageType("TestEvent") && entry.DisplayName == null && entry.Description == null && entry.Enabled
                && entry.Revision == 1 && entry.DisabledChannels.SequenceEqual(new[] { TestChannel.SecondId, TestChannel.Id })),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PutChannel_SavedEntry_Gives200WithTheSavedEntry()
    {
        // Act
        var response = await _host.Client.SendAsync(
            RequestOf("PUT", "/registry/channels/Test", """{"displayName":"Shop email","enabled":false,"revision":3}"""), Ct);

        // Assert
        Assert.Equal(
            (HttpStatusCode.OK,
                """{"channel":"Test","displayName":"Shop email","description":null,"enabled":false,"revision":4,"lastModifiedAtUtc":"2030-01-01T00:00:00Z"}"""),
            (response.StatusCode, await response.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task PutMessageType_SavedEntry_Gives200WithTheSavedEntry()
    {
        // Act
        var response = await _host.Client.SendAsync(
            RequestOf("PUT", "/registry/message-types/TestEvent", """{"enabled":false,"revision":0,"disabledChannels":["Second"]}"""), Ct);

        // Assert
        Assert.Equal(
            (HttpStatusCode.OK,
                """{"type":"TestEvent","displayName":null,"description":null,"enabled":false,"disabledChannels":["Second"],"revision":1,"lastModifiedAtUtc":"2030-01-01T00:00:00Z"}"""),
            (response.StatusCode, await response.Content.ReadAsStringAsync(Ct)));
    }

    [Fact]
    public async Task GetChannels_StoredEntryOfOneChannel_GivesEachChannelWithTheRevisionOfItsEntryOr0()
    {
        // Arrange
        WriterGives(new RegistryEntries([new ChannelEntry(TestChannel.SecondId, null, null, true, 4, TestHost.Start.UtcDateTime)], []));

        // Act
        var json = await _host.Client.GetStringAsync(new Uri("/registry/channels", UriKind.Relative), Ct);

        // Assert
        Assert.Equal(
            """[{"id":"Test","name":"Test channel","description":null,"features":"None","enabled":true,"revision":0},"""
            + """{"id":"Second","name":"Second test channel","description":null,"features":"GroupDelivery","enabled":true,"revision":4}]""",
            json);
    }

    [Fact]
    public async Task GetMessageTypes_StoredEntry_GivesEachMessageTypeWithItsChannelsAndTheRevisionOfItsEntry()
    {
        // Arrange
        WriterGives(new RegistryEntries([], [new MessageTypeEntry(new MessageType("TestEvent"), null, null, true, [], 2, TestHost.Start.UtcDateTime)]));

        // Act
        var json = await _host.Client.GetStringAsync(new Uri("/registry/message-types", UriKind.Relative), Ct);

        // Assert
        Assert.Equal(
            """[{"name":"TestEvent","displayName":null,"description":null,"channels":["Test","Second"],"enabled":true,"disabledChannels":[],"revision":2}]""",
            json);
    }

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
    [MemberData(nameof(ErrorRows))]
    public async Task PutChannel_WriterError_GivesItsStatusWithAProblemDetailsOfTheErrorName(int errorCode, HttpStatusCode expected)
    {
        // Arrange
        var error = new Error(ErrorTypeOf(errorCode), "The writer failed.");
        _host.Writer.Configure().SaveChannelAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<ChannelEntry>.ForError(error)));

        // Act
        var response = await _host.Client.SendAsync(RequestOf("PUT", "/registry/channels/Test", """{"enabled":true,"revision":0}"""), Ct);

        // Assert
        Assert.Equal((expected, error.Type.Name), (response.StatusCode, await TitleOf(response)));
    }

    [Theory]
    [MemberData(nameof(ErrorRows))]
    public async Task Routes_WriterOrQueryError_GiveTheProblemBodyWithTheDetailAndTheErrorType(int errorCode, HttpStatusCode expected)
    {
        // Arrange
        var error = new Error(ErrorTypeOf(errorCode), "The writer failed.");
        _host.Writer.GetEntriesAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<RegistryEntries>.ForError(error)));
        _host.Writer.Configure().SaveMessageTypeAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<MessageTypeEntry>.ForError(error)));
        _host.Writer.ResetChannelAsync(default, Ct).ReturnsForAnyArgs(ValueTask.FromResult<Error?>(error));
        (string, string?, string?, int?) problem = ($"{expected}", error.Type.Name, error.Message, error.Type.Code);

        // Act
        var channels = await ProblemOf(await _host.Client.SendAsync(RequestOf("GET", "/registry/channels", body: null), Ct));
        var types = await ProblemOf(await _host.Client.SendAsync(RequestOf("GET", "/registry/message-types", body: null), Ct));
        var save = await ProblemOf(await _host.Client.SendAsync(RequestOf("PUT", "/registry/message-types/TestEvent", """{"enabled":true,"revision":0}"""), Ct));
        var reset = await ProblemOf(await _host.Client.SendAsync(RequestOf("DELETE", "/registry/channels/Test", body: null), Ct));

        // Assert
        Assert.Equal((problem, problem, problem, problem), (channels, types, save, reset));
    }

    [Theory]
    [InlineData("PUT", "/registry/channels/not-a-name", """{"enabled":true,"revision":0}""")]
    [InlineData("PUT", "/registry/channels/Test", "{ not json")]
    [InlineData("PUT", "/registry/message-types/TestEvent", """{"enabled":true}""")]
    [InlineData("DELETE", "/registry/message-types/not-a-name", null)]
    public async Task Route_InvalidRequest_GivesTheErrorTypeValidationFailed(string method, string path, string? body)
    {
        // Act
        var problem = await ProblemOf(await _host.Client.SendAsync(RequestOf(method, path, body), Ct));

        // Assert
        Assert.Equal(
            ($"{HttpStatusCode.BadRequest}", (string?)Errors.ValidationFailed.Name, (int?)Errors.ValidationFailed.Code),
            (problem.Status, problem.Title, problem.ErrorCode));
    }

    [Fact]
    public async Task PutMessageType_WriterConflict_Gives409()
    {
        // Arrange
        _host.Writer.Configure().SaveMessageTypeAsync(default!, Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<MessageTypeEntry>.ForError(new Error(Errors.Conflict))));

        // Act
        var response = await _host.Client.SendAsync(RequestOf("PUT", "/registry/message-types/TestEvent", """{"enabled":true,"revision":0}"""), Ct);

        // Assert
        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    public static TheoryData<string, string> DeleteRows => new()
    {
        { "/registry/channels/Test", nameof(IRegistryWriter.ResetChannelAsync) },
        { "/registry/message-types/TestEvent", nameof(IRegistryWriter.ResetMessageTypeAsync) },
    };

    [Theory]
    [MemberData(nameof(DeleteRows))]
    public async Task Delete_Success_Gives204(string path, string member)
    {
        // Act
        var response = await _host.Client.SendAsync(RequestOf("DELETE", path, body: null), Ct);

        // Assert
        Assert.Equal((member, HttpStatusCode.NoContent), (member, response.StatusCode));
    }

    [Theory]
    [MemberData(nameof(DeleteRows))]
    public async Task Delete_WriterError_GivesItsStatus(string path, string member)
    {
        // Arrange
        var error = ValueTask.FromResult<Error?>(new Error(Errors.Unavailable));
        _host.Writer.ResetChannelAsync(default, Ct).ReturnsForAnyArgs(error);
        _host.Writer.ResetMessageTypeAsync(default, Ct).ReturnsForAnyArgs(error);

        // Act
        var response = await _host.Client.SendAsync(RequestOf("DELETE", path, body: null), Ct);

        // Assert
        Assert.Equal((member, HttpStatusCode.ServiceUnavailable), (member, response.StatusCode));
    }

    public static TheoryData<string> GetRows => ["/registry/channels", "/registry/message-types"];

    [Theory]
    [MemberData(nameof(GetRows))]
    public async Task Get_WriterUnavailable_Gives503(string path)
    {
        // Arrange
        _host.Writer.GetEntriesAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<RegistryEntries>.ForError(new Error(Errors.Unavailable))));

        // Act
        var response = await _host.Client.GetAsync(new Uri(path, UriKind.Relative), Ct);

        // Assert
        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    public static TheoryData<string, string, string?> InvalidRequestRows => new()
    {
        { "PUT", "/registry/channels/not-a-name", """{"enabled":true,"revision":0}""" },
        { "PUT", "/registry/channels/Test", """{"revision":0}""" },
        { "PUT", "/registry/channels/Test", "null" },
        { "PUT", "/registry/channels/Test", "{ not json" },
        { "PUT", "/registry/message-types/not-a-name", """{"enabled":true,"revision":0}""" },
        { "PUT", "/registry/message-types/TestEvent", """{"enabled":true,"revision":0,"disabledChannels":["not a name"]}""" },
        { "PUT", "/registry/message-types/TestEvent", """{"enabled":true}""" },
        { "DELETE", "/registry/channels/not-a-name", null },
        { "DELETE", "/registry/message-types/not-a-name", null },
    };

    [Theory]
    [MemberData(nameof(InvalidRequestRows))]
    public async Task Route_InvalidRequest_Gives400AndCallsNoWriter(string method, string path, string? body)
    {
        // Act
        var response = await _host.Client.SendAsync(RequestOf(method, path, body), Ct);

        // Assert
        Assert.Equal((HttpStatusCode.BadRequest, 0), (response.StatusCode, _host.Writer.ReceivedCalls().Count()));
    }

    [Fact]
    public async Task PutChannel_BodyThatIsNotJson_Gives415AndCallsNoWriter()
    {
        // Arrange
        using var request = new HttpRequestMessage(HttpMethod.Put, "/registry/channels/Test")
        {
            Content = new StringContent("""{"enabled":true,"revision":0}""", Encoding.UTF8, "text/plain"),
        };

        // Act
        var response = await _host.Client.SendAsync(request, Ct);

        // Assert
        Assert.Equal((HttpStatusCode.UnsupportedMediaType, 0), (response.StatusCode, _host.Writer.ReceivedCalls().Count()));
    }

    private void WriterGives(RegistryEntries entries) =>
        _host.Writer.GetEntriesAsync(Ct).ReturnsForAnyArgs(ValueTask.FromResult(ErrorOr<RegistryEntries>.ForValue(entries)));

    private static ErrorType ErrorTypeOf(int code) => Errors.Find(code)!;

    private static async Task<string?> TitleOf(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        return problem.RootElement.GetProperty("title").GetString();
    }

    private static async Task<(string Status, string? Title, string? Detail, int? ErrorCode)> ProblemOf(HttpResponseMessage response)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(Ct));
        var root = problem.RootElement;
        return (
            $"{response.StatusCode}",
            root.GetProperty("title").GetString(),
            root.TryGetProperty("detail", out var detail) ? detail.GetString() : null,
            root.TryGetProperty("errorType", out var errorType) ? errorType.Deserialize(ApiErrorJson.Default.ErrorTypeBody)?.Code : null);
    }

    private static HttpRequestMessage RequestOf(string method, string path, string? body) =>
        new(new HttpMethod(method), path) { Content = body is null ? null : new StringContent(body, Encoding.UTF8, "application/json") };
}
