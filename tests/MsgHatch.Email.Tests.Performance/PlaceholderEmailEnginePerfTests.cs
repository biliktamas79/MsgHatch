using System.Globalization;
using BenchmarkDotNet.Attributes;
using Microsoft.Extensions.Options;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using MsgHatch.Email.Engines;

namespace MsgHatch.Email.Tests.Performance;

/// <summary>
/// The placeholder engine (section 4.11): template parsing and binding with and without a hit of the parsed template
/// cache, the rendering of one prepared template, and the composition of one email. The fixed invocation count bounds
/// the number of template versions of the run with no cache hit, because the parsed template cache keeps each one.
/// </summary>
[MemoryDiagnoser]
[SimpleJob(warmupCount: 5, iterationCount: 20, invocationCount: _invocationsPerIteration)]
public class PlaceholderEmailEnginePerfTests
{
    private const int _invocationsPerIteration = 1024;
    private const int _maxInvocations = _invocationsPerIteration * 64;

    private static readonly EmailTemplateRequest _request = new(PerfTemplateStore.Type, "en", Scope: null);
    private static readonly CultureInfo _culture = CultureInfo.GetCultureInfo("en");
    private static readonly PerfPayload _payload = new("Ádám & <Éva>", 3, 129.90m);
    private static readonly MessageRoute _route = new(
        new DeliveryTarget(DeliveryTargetKind.User, "1"), EmailChannel.Id, new EmailChannelSettings(), "en", "client1@acme.example",
        TemplateScope: null, Trace: null);

    private static readonly ReceivedMessage<string> _message = new(
        1, new DeliveryState(Guid.NewGuid(), DeliveryStatus.Pending, 0, 8, DateTime.UnixEpoch, null, DateTime.UnixEpoch, null, null, null),
        Tenant: null, PerfTemplateStore.Type, 1, MessageFlags.None, TraceParent: null, "event");

    private static readonly MessageRoute[] _routes = [_route];

    private PlaceholderEmailEngine<PerfPayload> _hits = null!;
    private PlaceholderEmailEngine<PerfPayload> _misses = null!;
    private IEmailTemplate<PerfPayload> _template = null!;
    private EmailComposer<string, PerfPayload> _composer = null!;

    [GlobalSetup]
    public void Setup()
    {
        var options = Options.Create(new MsgHatchOptions());
        _hits = new PlaceholderEmailEngine<PerfPayload>(tenantStore: null, PerfTemplateStore.OneVersion(), options);
        _misses = new PlaceholderEmailEngine<PerfPayload>(tenantStore: null, PerfTemplateStore.NewVersions(_maxInvocations), options);
        _template = _hits.PrepareAsync(_request, default).GetAwaiter().GetResult().Value;
        _composer = new EmailComposer<string, PerfPayload>(new PerfPayloadLoader(), _hits);
    }

    [Benchmark(Baseline = true)]
    public ErrorOr<IEmailTemplate<PerfPayload>> PrepareAsync_ParsedTemplateCacheHit() =>
        _hits.PrepareAsync(_request, default).GetAwaiter().GetResult();

    [Benchmark]
    public ErrorOr<IEmailTemplate<PerfPayload>> PrepareAsync_ParsedTemplateCacheMiss() =>
        _misses.PrepareAsync(_request, default).GetAwaiter().GetResult();

    [Benchmark]
    public ErrorOr<RenderedEmail> RenderAsync_OnePayload() =>
        _template.RenderAsync(_payload, _route, _culture, default).GetAwaiter().GetResult();

    [Benchmark]
    public ErrorOr<IReadOnlyList<ErrorOr<EmailMessage>>> ComposeAsync_OneEmail() =>
        _composer.ComposeAsync(_message, _routes, default).GetAwaiter().GetResult();
}
