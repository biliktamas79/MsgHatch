using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.Management;

namespace MsgHatch.Templates.Api.Contracts;

/// <summary>The body of a save: the text, and the version that the caller read, or 0 for a new template (D223).</summary>
/// <param name="Text">The text.</param>
/// <param name="Version">The version that the caller read, or 0 for a new template.</param>
public sealed record TemplateSaveRequest(string Text, long Version);

/// <summary>One template with no text, with no template id, because the id of the file store is a path on the host (D224).</summary>
/// <param name="MessageType">The message type.</param>
/// <param name="Language">The language.</param>
/// <param name="CompanyId">The company of the template scope, or null for no company.</param>
/// <param name="Part">The part.</param>
/// <param name="Kind">The kind of the part.</param>
/// <param name="Version">The version.</param>
public sealed record TemplateEntryResponse(string MessageType, string Language, string? CompanyId, string Part, TemplatePartKind Kind, long Version)
{
    /// <summary>Gives the body of an entry (D228).</summary>
    /// <param name="entry">The entry.</param>
    /// <returns>The body.</returns>
    public static TemplateEntryResponse From(TemplateEntry entry)
    {
        ArgumentNullException.ThrowIfNull(entry);
        return new TemplateEntryResponse(entry.Lookup.Type.Name, entry.Lookup.Language, entry.Lookup.Scope, entry.Lookup.Part, entry.Lookup.Kind, entry.Version);
    }

    /// <summary>Gives the entry of the body. A value that is not valid gives <see cref="Errors.SerializationFailed"/> (D228).</summary>
    /// <returns>The entry, or an error.</returns>
    public ErrorOr<TemplateEntry> ToEntry()
    {
        var lookup = TemplateWireValues.LookupOf(MessageType, Language, CompanyId, Part, Kind);
        return lookup.IsError ? lookup.Error : new TemplateEntry(lookup.Value, Version);
    }
}

/// <summary>One template with its text, with no template id (D224).</summary>
/// <param name="MessageType">The message type.</param>
/// <param name="Language">The language.</param>
/// <param name="CompanyId">The company of the template scope, or null for no company.</param>
/// <param name="Part">The part.</param>
/// <param name="Kind">The kind of the part.</param>
/// <param name="Version">The version.</param>
/// <param name="Text">The text.</param>
public sealed record TemplateResponse(
    string MessageType, string Language, string? CompanyId, string Part, TemplatePartKind Kind, long Version, string Text)
{
    /// <summary>Gives the body of a template that a lookup found (D228).</summary>
    /// <param name="lookup">The lookup.</param>
    /// <param name="source">The text with its key.</param>
    /// <returns>The body.</returns>
    public static TemplateResponse From(TemplateLookup lookup, TemplateSource source)
    {
        ArgumentNullException.ThrowIfNull(source);
        return new TemplateResponse(lookup.Type.Name, lookup.Language, lookup.Scope, lookup.Part, lookup.Kind, source.Key.Version, source.Text);
    }

    /// <summary>Gives the lookup of the body. A value that is not valid gives <see cref="Errors.SerializationFailed"/> (D228).</summary>
    /// <returns>The lookup, or an error.</returns>
    public ErrorOr<TemplateLookup> ToLookup() => TemplateWireValues.LookupOf(MessageType, Language, CompanyId, Part, Kind);

    /// <summary>
    /// Gives the text of the body with its key. The API has no template id (D224), so the id of the key is a stable id of the
    /// lookup, which a caller reads as opaque. A value that is not valid gives <see cref="Errors.SerializationFailed"/> (D228).
    /// </summary>
    /// <returns>The text with its key, or an error.</returns>
    public ErrorOr<TemplateSource> ToSource()
    {
        var lookup = ToLookup();
        return lookup.IsError ? lookup.Error
            : Text is null ? TemplateWireValues.Invalid("A template has a text.")
            : new TemplateSource(new TemplateKey(TemplateWireValues.StableIdOf(lookup.Value), Version), Text);
    }
}

/// <summary>The body of a check of a draft: the text (D241).</summary>
/// <param name="Text">The text of the draft.</param>
public sealed record TemplateCheckRequest(string Text);

/// <summary>One issue of a checked text, with the line and the column of the engine, or null (D241).</summary>
/// <param name="Message">The message of the issue.</param>
/// <param name="Line">The line, from 1, or null.</param>
/// <param name="Column">The column, from 1, or null.</param>
public sealed record TemplateIssueResponse(string Message, int? Line, int? Column)
{
    /// <summary>Gives the body of an issue (D228).</summary>
    /// <param name="issue">The issue.</param>
    /// <returns>The body.</returns>
    public static TemplateIssueResponse From(TemplateIssue issue)
    {
        ArgumentNullException.ThrowIfNull(issue);
        return new TemplateIssueResponse(issue.Message, issue.Line, issue.Column);
    }

    /// <summary>Gives the issue of the body. A value that is not valid gives <see cref="Errors.SerializationFailed"/> (D228).</summary>
    /// <returns>The issue, or an error.</returns>
    public ErrorOr<TemplateIssue> ToIssue() =>
        Message is null || Line < 1 || Column < 1
            ? TemplateWireValues.Invalid("An issue has a message, and a line and a column from 1 or none.")
            : new TemplateIssue(Message, Line, Column);
}

/// <summary>One declared part of a message type, with the names of the engines that read it (D240), and whether it has a sample (D254).</summary>
/// <param name="MessageType">The message type.</param>
/// <param name="Part">The name of the part.</param>
/// <param name="Kind">The kind of the part.</param>
/// <param name="Engines">The names of the engines that read the part.</param>
/// <param name="HasSample">True when a registration that reads the part has a sample payload.</param>
public sealed record TemplatePartResponse(string MessageType, string Part, TemplatePartKind Kind, IReadOnlyList<string> Engines, bool HasSample)
{
    /// <summary>Gives the body of a declared part (D228).</summary>
    /// <param name="info">The declared part.</param>
    /// <returns>The body.</returns>
    public static TemplatePartResponse From(TemplatePartInfo info)
    {
        ArgumentNullException.ThrowIfNull(info);
        return new TemplatePartResponse(info.Type.Name, info.Part.Name, info.Part.Kind, info.Engines, info.HasSample);
    }

    /// <summary>Gives the declared part of the body. A value that is not valid gives <see cref="Errors.SerializationFailed"/> (D228).</summary>
    /// <returns>The declared part, or an error.</returns>
    public ErrorOr<TemplatePartInfo> ToPartInfo()
    {
        var lookup = TemplateWireValues.LookupOf(MessageType, TemplateWireValues.NoLanguage, companyId: null, Part, Kind);
        return lookup.IsError ? lookup.Error
            : Engines is null || Engines.Any(string.IsNullOrWhiteSpace) ? TemplateWireValues.Invalid("A part has the names of its engines.")
            : new TemplatePartInfo(lookup.Value.Type, new TemplatePart(Part, Kind), [.. Engines], HasSample);
    }
}

/// <summary>Checks each value of a template body from the wire. A value that is not valid gives <see cref="Errors.SerializationFailed"/> (D228).</summary>
internal static class TemplateWireValues
{
    /// <summary>The language of the lookup that checks a declared part, which has no language: the BCP 47 tag of an undetermined language.</summary>
    public const string NoLanguage = "und";

    public static ErrorOr<TemplateLookup> LookupOf(string messageType, string language, string? companyId, string part, TemplatePartKind kind)
    {
        var type = MessageTypeOf(messageType);
        if (type.IsError)
        {
            return type.Error;
        }

        if (language is null || !Enum.IsDefined(kind))
        {
            return Invalid("A template has a language and a known kind.");
        }

        var lookup = new TemplateLookup(type.Value, language, companyId, part, kind);
        return TemplateLookupRules.Check(lookup) is { } broken ? Invalid(broken.Message ?? broken.Type.Name) : lookup;
    }

    public static string StableIdOf(TemplateLookup lookup) =>
        $"{lookup.Type.Name}/{lookup.Language}/{lookup.Part}/{lookup.Kind}" + (lookup.Scope is { } scope ? $"?companyId={Uri.EscapeDataString(scope)}" : string.Empty);

    public static Error Invalid(string message) => new(Errors.SerializationFailed, message);

    private static ErrorOr<MessageType> MessageTypeOf(string name)
    {
        try
        {
            return new MessageType(name);
        }
        catch (ArgumentException exception)
        {
            return Invalid($"The message type is not valid: {exception.Message}");
        }
    }
}
