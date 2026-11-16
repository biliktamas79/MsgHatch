using System.Globalization;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.Engines;
using MsgHatch.Email.Templates;
using MsgHatch.Email.Tests.Unit.TestTypes;

namespace MsgHatch.Email.Tests.Unit.Contracts;

/// <summary>
/// The use cases of <see cref="ITemplateEngine{TPayload}"/> and <see cref="ITemplate{TPayload}"/> for one part (section
/// 5.5, D213). Each engine gets them with one subclass that builds the engine and its test data. Each test method uses
/// a message type of its own, with the name of the method, so the tests share no template. An engine can find an
/// unknown name at prepare or at render (D207).
/// </summary>
/// <typeparam name="TEngine">The engine type.</typeparam>
public abstract partial class TemplateEngineTestsBase<TEngine>(TEngine engine, ITemplateTestData testData)
    where TEngine : class, ITemplateEngine<OrderEmail>
{
    protected const string Part = "body";

    protected static readonly DateTime ShippedAt = new(2030, 1, 2, 15, 4, 5, DateTimeKind.Utc);

    protected TEngine Engine { get; } = engine;

    protected ITemplateTestData TestData { get; } = testData;

    protected MessageType Type { get; } = new(TestContext.Current.TestMethod!.MethodName);

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected static OrderEmail APayload(string name = "Client 1", int count = 3, decimal total = 1234.5m) =>
        new(name, count, total, ShippedAt);

    protected TemplateRequest ARequest(string language = "en", TemplatePartKind kind = TemplatePartKind.PlainText) =>
        new(Type, language, Scope: null, Part, kind);

    protected async Task<string> RenderAsync(OrderEmail payload, string language, TemplatePartKind kind = TemplatePartKind.PlainText)
    {
        var template = await Engine.PrepareAsync(ARequest(language, kind), Ct);
        var rendered = await template.Value.RenderAsync(payload, CultureInfo.GetCultureInfo(language), Ct);
        return rendered.Value;
    }

    protected static int? CodeOf<T>(ErrorOr<T> result) => result.Error?.Type.Code;
}
