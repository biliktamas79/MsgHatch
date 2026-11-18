using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.DependencyInjection;

namespace MsgHatch.Templates.Management;

/// <summary>
/// The template manager of one tenant key (D222). It runs the rules of <see cref="TemplateLookupRules"/> first, then the
/// check of the language, and then the checks that need the store or the engines (D234). It checks the text of a save with
/// the engine of each payload type whose registration of the message type declares the part (D219, D220).
/// </summary>
/// <param name="store">The template store of the key, or null when none is registered.</param>
/// <param name="setup">The registrations of the template engines.</param>
/// <param name="tenantServices">The service provider of the tenant services of the key, which gives the engines of the key.</param>
internal sealed class TemplateManager(ITemplateStore? store, TemplateSetup setup, IServiceProvider tenantServices)
    : ITemplateQueries, ITemplateCommands
{
    public static TemplateManager For(IServiceProvider tenantServices, TenantKey key)
    {
        var services = tenantServices.GetRequiredService<IServiceProvider>();
        return new TemplateManager(
            key == TenantKey.None ? services.GetService<ITemplateStore>() : services.GetKeyedService<ITemplateStore>(key),
            services.GetRequiredService<TemplateSetup>(),
            tenantServices);
    }

    public async ValueTask<ErrorOr<IReadOnlyList<TemplateEntry>>> GetTemplatesAsync(TemplateQuery query, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(query);
        var invalid = TemplateLookupRules.Check(query);
        if (invalid is not null)
        {
            return invalid;
        }

        var writer = Writer();
        return writer.IsError ? writer.Error : await writer.Value.ListAsync(query, ct);
    }

    public async ValueTask<ErrorOr<TemplateSource>> GetTemplateAsync(TemplateLookup lookup, CancellationToken ct)
    {
        var invalid = LookupError(lookup);
        if (invalid is not null)
        {
            return invalid;
        }

        if (store is null)
        {
            return NoStore();
        }

        var source = await store.FindAsync(lookup, ct);
        if (source.IsError)
        {
            return source.Error;
        }

        return source.Value is { } found
            ? found
            : new Error(Errors.NotFound, $"No {lookup.Kind} {lookup.Part} template of {lookup.Type.Name} in the language {lookup.Language} exists in this store.");
    }

    public ValueTask<ErrorOr<IReadOnlyList<TemplatePartInfo>>> GetTemplatePartsAsync(MessageType? type, CancellationToken ct)
    {
        IReadOnlyList<TemplatePartInfo> parts =
        [
            .. setup.EngineSelections
                .Where(selection => type is null || selection.Type == type)
                .SelectMany(static selection => selection.Parts.Select(part => (selection.Type, Part: part, selection.EngineName)))
                .GroupBy(static declared => (declared.Type, declared.Part))
                .Select(static group => new TemplatePartInfo(
                    group.Key.Type, group.Key.Part, [.. group.Select(static declared => declared.EngineName).Distinct(StringComparer.Ordinal)])),
        ];
        return ValueTask.FromResult(ErrorOr<IReadOnlyList<TemplatePartInfo>>.ForValue(parts));
    }

    public async ValueTask<ErrorOr<IReadOnlyList<TemplateIssue>>> CheckTemplateAsync(TemplateLookup lookup, string text, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        var invalid = TemplateLookupRules.Check(lookup) ?? TemplateLookupRules.CheckText(text) ?? LanguageError(lookup.Language);
        return invalid is not null ? invalid : await IssuesOfAsync(lookup, text, ct);
    }

    public async ValueTask<ErrorOr<TemplateEntry>> SaveTemplateAsync(TemplateLookup lookup, string text, long expectedVersion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        var invalid = TemplateLookupRules.Check(lookup) ?? TemplateLookupRules.CheckText(text) ?? LanguageError(lookup.Language);
        if (invalid is not null)
        {
            return invalid;
        }

        var writer = Writer();
        if (writer.IsError)
        {
            return writer.Error;
        }

        var issues = await IssuesOfAsync(lookup, text, ct);
        return issues.IsError ? issues.Error
            : issues.Value.Count > 0 ? new Error(Errors.TemplateInvalid, issues.Value[0].Message)
            : await writer.Value.SaveAsync(lookup, text, expectedVersion, ct);
    }

    public async ValueTask<Error?> DeleteTemplateAsync(TemplateLookup lookup, CancellationToken ct)
    {
        var invalid = LookupError(lookup);
        if (invalid is not null)
        {
            return invalid;
        }

        var writer = Writer();
        return writer.IsError ? writer.Error : await writer.Value.DeleteAsync(lookup, ct);
    }

    private async ValueTask<ErrorOr<IReadOnlyList<TemplateIssue>>> IssuesOfAsync(TemplateLookup lookup, string text, CancellationToken ct)
    {
        var readers = ReadersOf(lookup);
        if (readers.IsError)
        {
            return readers.Error;
        }

        List<TemplateIssue> issues = [];
        foreach (var reader in readers.Value)
        {
            var checkedText = await reader.Check(tenantServices, lookup, text, ct);
            if (checkedText.IsError)
            {
                return checkedText.Error;
            }

            issues.AddRange(checkedText.Value);
        }

        return issues;
    }

    private ErrorOr<ITemplateWriter> Writer() =>
        store is null ? NoStore()
        : store is ITemplateWriter writer ? ErrorOr<ITemplateWriter>.ForValue(writer)
        : Invalid("The template store of this tenant cannot write, for example an embedded template store.");

    private ErrorOr<EngineSelection[]> ReadersOf(TemplateLookup lookup)
    {
        EngineSelection[] registrations = [.. setup.EngineSelections.Where(selection => selection.Type == lookup.Type)];
        if (registrations.Length == 0)
        {
            return new Error(Errors.NotFound, $"No registration renders the templates of the message type {lookup.Type.Name}.");
        }

        EngineSelection[] readers =
        [
            .. registrations
                .Where(selection => selection.Parts.Contains(new TemplatePart(lookup.Part, lookup.Kind)))
                .DistinctBy(static selection => selection.PayloadType),
        ];
        return readers.Length > 0 ? readers : Invalid($"No registration of {lookup.Type.Name} reads the {lookup.Kind} part {lookup.Part}.");
    }

    private static Error? LookupError(TemplateLookup lookup) => TemplateLookupRules.Check(lookup) ?? LanguageError(lookup.Language);

    private static Error? LanguageError(string? language) =>
        language is { Length: > 0 } && CultureNameOf(language) == language
            ? null
            : Invalid($"The language {language} is not the name of a known culture, for example en or hu-HU.");

    private static string? CultureNameOf(string language)
    {
        try
        {
            return CultureInfo.GetCultureInfo(language, predefinedOnly: true).Name;
        }
        catch (CultureNotFoundException)
        {
            return null;
        }
    }

    private static Error NoStore() => new(Errors.NotFound, "No template store is registered for this tenant.");

    private static Error Invalid(string message) => new(Errors.ValidationFailed, message);
}
