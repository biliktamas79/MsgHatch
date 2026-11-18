using System.Collections.Concurrent;

namespace MsgHatch.Templates.Api.Client.Tests.Unit.TestTypes;

/// <summary>The URIs of the requests of a client host. Each HTTP client gets its own <see cref="Handler"/> on the same log.</summary>
public sealed class RequestLog
{
    public ConcurrentQueue<Uri> Uris { get; } = new();

    /// <summary>Gives a new handler that adds the URI of each request to this log.</summary>
    public DelegatingHandler Handler() => new RecordingHandler(this);

    private sealed class RecordingHandler(RequestLog log) : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            log.Uris.Enqueue(request.RequestUri!);
            return base.SendAsync(request, cancellationToken);
        }
    }
}
