namespace MsgHatch.SourceGeneration.Tests.Unit.TestTypes;

/// <summary>A primary HTTP handler that records each request with its body, and gives the response of a function.</summary>
/// <param name="respond">The function that gives the response of a request, or throws.</param>
public sealed class StubHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) : HttpMessageHandler
{
    public List<(HttpRequestMessage Request, string? Body)> Requests { get; } = [];

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add((request, request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken)));
        return respond(request);
    }
}
