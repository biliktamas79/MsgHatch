using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.Management;

namespace MsgHatch.Templates.AdminUI;

internal static class TemplateRouteValues
{
    public static ErrorOr<TemplateLookup> LookupOf(string typeName, string language, string? companyId, string part, string kindName)
    {
        var type = TypeOf(typeName);
        if (type.IsError)
        {
            return type.Error;
        }

        if (KindOf(kindName) is not { } kind)
        {
            return Invalid($"The kind {kindName} is not {nameof(TemplatePartKind.PlainText)} or {nameof(TemplatePartKind.Html)}.");
        }

        var lookup = new TemplateLookup(type.Value, language, string.IsNullOrWhiteSpace(companyId) ? null : companyId, part, kind);
        return TemplateLookupRules.Check(lookup) is { } broken ? broken : lookup;
    }

    public static ErrorOr<MessageType> TypeOf(string typeName)
    {
        try
        {
            return new MessageType(typeName);
        }
        catch (ArgumentException)
        {
            return Invalid($"The message type {typeName} is not a name of ASCII letters, digits, and _ that starts with a letter.");
        }
    }

    private static TemplatePartKind? KindOf(string kindName) => kindName switch
    {
        nameof(TemplatePartKind.PlainText) => TemplatePartKind.PlainText,
        nameof(TemplatePartKind.Html) => TemplatePartKind.Html,
        _ => null,
    };

    private static Error Invalid(string message) => new(Errors.ValidationFailed, message);
}
