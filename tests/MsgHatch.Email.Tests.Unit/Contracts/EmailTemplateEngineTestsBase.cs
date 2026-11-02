using System.Globalization;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Email.Engines;
using MsgHatch.Email.Tests.Unit.TestTypes;

namespace MsgHatch.Email.Tests.Unit.Contracts;

/// <summary>
/// The use cases of <see cref="IEmailTemplateEngine{TPayload}"/> and <see cref="IEmailTemplate{TPayload}"/> (section
/// 5.5). Each engine gets them with one subclass that builds the engine and its test data. Each test uses a message
/// type of its own, so the tests share no template.
/// </summary>
/// <typeparam name="TEngine">The engine type.</typeparam>
public abstract partial class EmailTemplateEngineTestsBase<TEngine>(TEngine engine, IEmailTemplateTestData testData)
    where TEngine : class, IEmailTemplateEngine<OrderEmail>
{
    protected static readonly DateTime ShippedAt = new(2030, 1, 2, 15, 4, 5, DateTimeKind.Utc);

    protected TEngine Engine { get; } = engine;

    protected IEmailTemplateTestData TestData { get; } = testData;

    protected MessageType Type { get; } = new("T" + Guid.NewGuid().ToString("N"));

    protected static CancellationToken Ct => TestContext.Current.CancellationToken;

    protected static OrderEmail APayload(string name = "Client 1", int count = 3, decimal total = 1234.5m) =>
        new(name, count, total, ShippedAt);

    protected async Task<RenderedEmail> RenderAsync(OrderEmail payload, string language)
    {
        var template = await Engine.PrepareAsync(new EmailTemplateRequest(Type, language, Scope: null), Ct);
        var rendered = await template.Value.RenderAsync(payload, TestRoutes.AnEmailRoute(language: language), CultureInfo.GetCultureInfo(language), Ct);
        return rendered.Value;
    }

    protected static int? CodeOf<T>(ErrorOr<T> result) => result.Error?.Type.Code;
}
