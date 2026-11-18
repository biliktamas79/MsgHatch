using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.Management;

namespace MsgHatch.Templates.AdminUI.Tests.Unit.TestTypes;

public static class TemplateTestData
{
    public static readonly MessageType Requests = new("Requests");

    public static TemplateLookup Lookup(string language = "en", string? scope = null, string part = "subject", TemplatePartKind kind = TemplatePartKind.PlainText) =>
        new(Requests, language, scope, part, kind);

    public static TemplatePartInfo Part(string part = "subject", TemplatePartKind kind = TemplatePartKind.PlainText, params string[] engines) =>
        new(Requests, new TemplatePart(part, kind), engines.Length == 0 ? ["placeholder"] : engines, HasSample: true);

    public static TemplateSource Source(string text = "Hello {{Name}}", long version = 7) => new(new TemplateKey("Requests/en/subject/PlainText", version), text);

    public static ValueTask<ErrorOr<T>> Ok<T>(T value) => ValueTask.FromResult(ErrorOr<T>.ForValue(value));

    public static ValueTask<ErrorOr<T>> Fail<T>(ErrorType type, string? message = null) => ValueTask.FromResult(ErrorOr<T>.ForError(new Error(type, message)));
}
