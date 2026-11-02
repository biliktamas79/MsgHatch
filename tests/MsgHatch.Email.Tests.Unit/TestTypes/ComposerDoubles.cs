using System.Globalization;
using MsgHatch.Domain.Delivery;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Pipeline;
using MsgHatch.Domain.Routing;
using MsgHatch.Email.Engines;
using NSubstitute;

namespace MsgHatch.Email.Tests.Unit.TestTypes;

/// <summary>
/// Shared by the composer tests: a loader and an engine substitute that record their calls. By default, the loader
/// gives one payload for each route with the target id as its name, and each template renders the target id.
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
        Template = Substitute.For<IEmailTemplate<TestPayload>>();
        Template.RenderAsync(default!, default, default!, default).ReturnsForAnyArgs(call =>
        {
            var route = call.ArgAt<MessageRoute>(1);
            Renders.Add((call.ArgAt<TestPayload>(0), route, call.ArgAt<CultureInfo>(2)));
            return ValueTask.FromResult(RenderResult(route));
        });
        Engine = Substitute.For<IEmailTemplateEngine<TestPayload>>();
        Engine.PrepareAsync(default, default).ReturnsForAnyArgs(call =>
        {
            var request = call.ArgAt<EmailTemplateRequest>(0);
            Prepares.Add(request);
            return ValueTask.FromResult(PrepareResult(request));
        });
    }

    public IPayloadLoader<TestEvent, TestPayload> Loader { get; }

    public IEmailTemplate<TestPayload> Template { get; }

    public IEmailTemplateEngine<TestPayload> Engine { get; }

    public List<MessageRoute[]> LoadCalls { get; } = [];

    public List<EmailTemplateRequest> Prepares { get; } = [];

    public List<(TestPayload Payload, MessageRoute Route, CultureInfo Culture)> Renders { get; } = [];

    public Func<MessageRoute[], ErrorOr<IReadOnlyList<TestPayload>>> LoadResult { get; set; } =
        static routes => ErrorOr<IReadOnlyList<TestPayload>>.ForValue([.. routes.Select(static route => new TestPayload(route.Target.Id))]);

    public Func<EmailTemplateRequest, ErrorOr<IEmailTemplate<TestPayload>>> PrepareResult { get; set; }

    public Func<MessageRoute, ErrorOr<RenderedEmail>> RenderResult { get; set; } =
        static route => new RenderedEmail($"Subject {route.Target.Id}", $"<p>{route.Target.Id}</p>", $"Text {route.Target.Id}");

    public EmailComposer<TestEvent, TestPayload> Composer() => new(Loader, Engine);

    public ValueTask<ErrorOr<IReadOnlyList<ErrorOr<EmailMessage>>>> ComposeAsync(params MessageRoute[] routes) =>
        Composer().ComposeAsync(TestMessages.AReceivedMessage(new TestEvent("1001")), routes, TestContext.Current.CancellationToken);

    public ErrorOr<IEmailTemplate<TestPayload>> TheTemplate(EmailTemplateRequest request) => ErrorOr<IEmailTemplate<TestPayload>>.ForValue(Template);
}
