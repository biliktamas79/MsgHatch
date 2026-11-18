using System.Net;
using System.Text;

namespace MsgHatch.Api.Client.Tests.Unit.TestTypes;

/// <summary>
/// A primary HTTP handler that gives each request the same JSON response, or throws the same exception, and records the
/// URI of each request.
/// </summary>
/// <param name="status">The status of the response.</param>
/// <param name="json">The JSON body of the response.</param>
/// <param name="exception">The exception of each request, or null for the response.</param>
public sealed class StubHandler(HttpStatusCode status, string json, Exception? exception = null) : HttpMessageHandler
{
    public List<Uri> Uris { get; } = [];

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Uris.Add(request.RequestUri!);
        return exception is not null
            ? Task.FromException<HttpResponseMessage>(exception)
            : Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, "application/json") });
    }
}
