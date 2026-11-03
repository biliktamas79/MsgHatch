using System.Collections.Concurrent;
using Wolverine;
using Wolverine.Configuration;
using Wolverine.Runtime;
using Wolverine.Transports;
using Wolverine.Transports.Sending;

namespace MsgHatch.Broker.Wolverine.Tests.Unit.TestTypes;

/// <summary>
/// A Wolverine transport in memory, with one endpoint at <see cref="Address"/>. Its sender fails the first
/// <c>failedSends</c> sends, by default with an <see cref="IOException"/> as a broker that is not available, and records
/// each send after them. Wolverine reuses a sent envelope, so the sender copies its fields at the time of the send.
/// </summary>
/// <param name="failedSends">The number of sends that fail before the first send that succeeds.</param>
/// <param name="failure">The exception of a failed send, or null for an <see cref="IOException"/>.</param>
public sealed class FakeWolverineTransport(int failedSends = 0, Func<Exception>? failure = null)
    : TransportBase<FakeWolverineEndpoint>("fake", "Fake", [])
{
    public static readonly Uri Address = new("fake://broker");

    private readonly FakeWolverineEndpoint _endpoint =
        new(Address, failedSends, failure ?? (static () => new IOException("The fake broker is not available.")));

    public IReadOnlyCollection<SentEnvelope> Sent => _endpoint.Sender.Sent;

    protected override IEnumerable<FakeWolverineEndpoint> endpoints() => [_endpoint];

    protected override FakeWolverineEndpoint findEndpointByUri(Uri uri) => _endpoint;
}

/// <summary>The endpoint of <see cref="FakeWolverineTransport"/>. It only sends.</summary>
public sealed class FakeWolverineEndpoint(Uri uri, int failedSends, Func<Exception> failure) : Endpoint(uri, EndpointRole.Application)
{
    public FakeWolverineSender Sender { get; } = new(uri, failedSends, failure);

    public override ValueTask<IListener> BuildListenerAsync(IWolverineRuntime runtime, IReceiver receiver) =>
        throw new NotSupportedException("The fake endpoint only sends.");

    protected override ISender CreateSender(IWolverineRuntime runtime) => Sender;
}

/// <summary>The sender of <see cref="FakeWolverineTransport"/>.</summary>
public sealed class FakeWolverineSender(Uri destination, int failedSends, Func<Exception> failure) : ISender
{
    private readonly ConcurrentQueue<SentEnvelope> _sent = new();
    private int _sends;

    public IReadOnlyCollection<SentEnvelope> Sent => _sent;

    public bool SupportsNativeScheduledSend => false;

    public Uri Destination => destination;

    public Task<bool> PingAsync() => Task.FromResult(false);

    public ValueTask SendAsync(Envelope envelope)
    {
        if (Interlocked.Increment(ref _sends) <= failedSends)
        {
            return ValueTask.FromException(failure());
        }

        _sent.Enqueue(new SentEnvelope(envelope.Message, new Dictionary<string, string?>(envelope.Headers), envelope.TenantId, envelope.ParentId));
        return ValueTask.CompletedTask;
    }
}

/// <summary>The fields of an envelope that <see cref="FakeWolverineSender"/> sent.</summary>
public sealed record SentEnvelope(object? Message, IReadOnlyDictionary<string, string?> Headers, string? TenantId, string? ParentId);
