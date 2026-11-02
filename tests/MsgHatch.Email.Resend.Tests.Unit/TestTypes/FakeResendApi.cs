using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json.Nodes;
using NSubstitute;

namespace MsgHatch.Email.Resend.Tests.Unit.TestTypes;

/// <summary>
/// The Resend API of the unit tests: the primary HTTP handler of the Resend client. It keeps each request and gives the
/// prepared response, so no request leaves the process. By default it accepts each request.
/// </summary>
public sealed class FakeResendApi : HttpMessageHandler
{
    private Func<CancellationToken, Task<HttpResponseMessage>> _respond = static _ => Task.FromResult(ResponseOf(HttpStatusCode.OK, """{"id":"resend-1"}"""));

    public List<ResendRequest> Requests { get; } = [];

    /// <summary>The JSON body of a Resend error, in the shape of the Resend API.</summary>
    public static string ErrorBody(int statusCode, string name, string message) =>
        new JsonObject { ["statusCode"] = statusCode, ["name"] = name, ["message"] = message }.ToJsonString();

    public void RespondsWith(HttpStatusCode status, string body) => _respond = _ => Task.FromResult(ResponseOf(status, body));

    public void Throws(Exception exception) => _respond = _ => Task.FromException<HttpResponseMessage>(exception);

    public void WaitsUntilCancelled() => _respond = static async ct =>
    {
        await Task.Delay(Timeout.InfiniteTimeSpan, TimeProvider.System, ct);
        return new HttpResponseMessage(HttpStatusCode.OK);
    };

    /// <summary>An HTTP client factory whose client <see cref="ResendEmailClient.HttpClientName"/> sends to this API.</summary>
    public IHttpClientFactory Clients()
    {
        var clients = Substitute.For<IHttpClientFactory>();
        clients.CreateClient(ResendEmailClient.HttpClientName).Returns(_ => new HttpClient(this, disposeHandler: false));
        return clients;
    }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
        Requests.Add(new ResendRequest(request.Method, request.RequestUri, request.Headers, body));
        return await _respond(cancellationToken);
    }

    private static HttpResponseMessage ResponseOf(HttpStatusCode status, string body) =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
}

/// <summary>One request that the Resend client sent to <see cref="FakeResendApi"/>.</summary>
public sealed record ResendRequest(HttpMethod Method, Uri? Uri, HttpRequestHeaders Headers, string? Body)
{
    public JsonNode? Json => Body is null ? null : JsonNode.Parse(Body);

    public string? Header(string name) => Headers.TryGetValues(name, out var values) ? string.Join(' ', values) : null;
}
