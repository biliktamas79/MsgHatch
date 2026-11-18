using System.Globalization;
using Microsoft.Extensions.DependencyInjection;
using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.DependencyInjection;

namespace MsgHatch.Templates.Management;

/// <summary>
/// The template manager of one tenant key (D222). It validates each request before it calls the store, and checks the
/// text of a save with the engine of each payload type whose registration of the message type declares the part (D219,
/// D220).
/// </summary>
/// <param name="store">The template store of the key, or null when none is registered.</param>
/// <param name="setup">The registrations of the template engines.</param>
/// <param name="tenantServices">The service provider of the tenant services of the key, which gives the engines of the key.</param>
internal sealed class TemplateManager(ITemplateStore? store, TemplateSetup setup, IServiceProvider tenantServices)
    : ITemplateQueries, ITemplateCommands
{
    private const int _maxTextLength = 1_000_000;
    private const int _maxFolderNameLength = 255;

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
        var invalid = query.Scope is null ? null : ScopeError(query.Scope);
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

    public async ValueTask<ErrorOr<TemplateEntry>> SaveTemplateAsync(TemplateLookup lookup, string text, long expectedVersion, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(text);
        var invalid = LookupError(lookup) ?? TextError(text);
        if (invalid is not null)
        {
            return invalid;
        }

        var writer = Writer();
        if (writer.IsError)
        {
            return writer.Error;
        }

        var readers = ReadersOf(lookup);
        if (readers.IsError)
        {
            return readers.Error;
        }

        foreach (var reader in readers.Value)
        {
            var rejected = await reader.Check(tenantServices, lookup, text, ct);
            if (rejected is not null)
            {
                return rejected;
            }
        }

        return await writer.Value.SaveAsync(lookup, text, expectedVersion, ct);
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

    private static Error? LookupError(TemplateLookup lookup) =>
        LanguageError(lookup.Language) ?? (lookup.Scope is null ? null : ScopeError(lookup.Scope)) ?? PartError(lookup.Part);

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

    private static Error? ScopeError(string scope)
    {
        var company = CompanyId.Create(scope);
        return company.IsError ? company.Error
            : ScopeFolders.FolderOf(scope)!.Length > _maxFolderNameLength
                ? Invalid($"The folder name of the company id has more than {_maxFolderNameLength} characters.")
                : null;
    }

    private static Error? PartError(string? part) =>
        TemplatePartNames.IsValid(part) ? null : Invalid($"The part {part} is not a name of ASCII letters, digits, and _ that starts with a letter.");

    private static Error? TextError(string text) =>
        text.Length > _maxTextLength ? Invalid($"A template text has at most {_maxTextLength} characters.") : null;

    private static Error NoStore() => new(Errors.NotFound, "No template store is registered for this tenant.");

    private static Error Invalid(string message) => new(Errors.ValidationFailed, message);
}
