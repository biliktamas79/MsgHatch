using System.Globalization;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Email.Engines;
using MsgHatch.Email.Templates;
using NSubstitute;

namespace MsgHatch.Email.Tests.Unit.TestTypes;

/// <summary>
/// Shared by the composer tests: a loader substitute and an engine substitute that record their calls. By default,
/// the loader gives one payload for each route with the target id as its name, and each part renders the name: the
/// subject <c>Subject {name}</c>, the html body <c>&lt;p&gt;{name}&lt;/p&gt;</c>, and the text body <c>Text {name}</c>.
/// </summary>
public sealed class ComposerDoubles
{
    public ComposerDoubles()
    {
        PrepareResult = TheTemplate;
        Loader = Substitute.For<IPayloadLoader<TestEvent, TestPayload>>();
        Loader.LoadAsync(default!, default, default).ReturnsForAnyArgs(call =>
        {
            var routes = call.ArgAt<ReadOnlyMemory<MessageRoute>>(1).ToArray();
            LoadCalls.Add(routes);
            return ValueTask.FromResult(LoadResult(routes));
        });
        Engine = Substitute.For<ITemplateEngine<TestPayload>>();
        Engine.PrepareAsync(default, default).ReturnsForAnyArgs(call =>
        {
            var request = call.ArgAt<TemplateRequest>(0);
            Prepares.Add(request);
            return ValueTask.FromResult(PrepareResult(request));
        });
    }

    public IPayloadLoader<TestEvent, TestPayload> Loader { get; }

    public ITemplateEngine<TestPayload> Engine { get; }

    public List<MessageRoute[]> LoadCalls { get; } = [];

    public List<TemplateRequest> Prepares { get; } = [];

    public List<(TestPayload Payload, TemplateRequest Request, CultureInfo Culture)> Renders { get; } = [];

    public Func<MessageRoute[], ErrorOr<IReadOnlyList<TestPayload>>> LoadResult { get; set; } =
        static routes => ErrorOr<IReadOnlyList<TestPayload>>.ForValue([.. routes.Select(static route => new TestPayload(route.Target.Id))]);

    public Func<TemplateRequest, ErrorOr<ITemplate<TestPayload>>> PrepareResult { get; set; }

    public Func<TemplateRequest, TestPayload, ErrorOr<string>> RenderResult { get; set; } = static (request, payload) =>
        request.Part == "subject" ? $"Subject {payload.Name}"
        : request.Kind == TemplatePartKind.Html ? $"<p>{payload.Name}</p>"
        : $"Text {payload.Name}";

    public EmailComposer<TestEvent, TestPayload> Composer() => new(Loader, Engine);

    public ValueTask<ErrorOr<IReadOnlyList<ErrorOr<EmailMessage>>>> ComposeAsync(params MessageRoute[] routes) =>
        Composer().ComposeAsync(TestMessages.AReceivedMessage(new TestEvent("1001")), routes, TestContext.Current.CancellationToken);

    public ErrorOr<ITemplate<TestPayload>> TheTemplate(TemplateRequest request) =>
        ErrorOr<ITemplate<TestPayload>>.ForValue(new RecordingTemplate(this, request));

    private sealed class RecordingTemplate(ComposerDoubles doubles, TemplateRequest request) : ITemplate<TestPayload>
    {
        public ValueTask<ErrorOr<string>> RenderAsync(TestPayload payload, CultureInfo culture, CancellationToken ct)
        {
            doubles.Renders.Add((payload, request, culture));
            return ValueTask.FromResult(doubles.RenderResult(request, payload));
        }
    }
}
