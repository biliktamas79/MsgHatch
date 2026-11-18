using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Templates.Management;

/// <summary>
/// The rules of a template request that need no state (D234). Each implementation of <see cref="ITemplateQueries"/> and
/// <see cref="ITemplateCommands"/> runs them first, then the check of the language, which needs the culture data of the
/// machine, and then the checks that need the store or the engines. So each implementation gives the same first error for
/// an input.
/// </summary>
public static class TemplateLookupRules
{
    private const int _maxTextLength = 1_000_000;
    private const int _maxFolderNameLength = 255;

    /// <summary>
    /// Checks a lookup: the template scope is null or a valid <see cref="CompanyId"/> whose folder name has at most 255
    /// characters, and the part is a name of ASCII letters, digits, and <c>_</c> that starts with a letter.
    /// </summary>
    /// <param name="lookup">The lookup.</param>
    /// <returns>Null, or <see cref="Errors.ValidationFailed"/> with the message of the first rule that the lookup breaks.</returns>
    public static Error? Check(TemplateLookup lookup) => ScopeError(lookup.Scope) ?? PartError(lookup.Part);

    /// <summary>Checks the filters of a list: the template scope is null or a valid <see cref="CompanyId"/> whose folder name has at most 255 characters.</summary>
    /// <param name="query">The filters.</param>
    /// <returns>Null, or <see cref="Errors.ValidationFailed"/>.</returns>
    /// <exception cref="ArgumentNullException">The query is null.</exception>
    public static Error? Check(TemplateQuery query)
    {
        ArgumentNullException.ThrowIfNull(query);
        return ScopeError(query.Scope);
    }

    /// <summary>Checks the text of a save or of a check: it has at most 1,000,000 characters.</summary>
    /// <param name="text">The text.</param>
    /// <returns>Null, or <see cref="Errors.ValidationFailed"/>.</returns>
    /// <exception cref="ArgumentNullException">The text is null.</exception>
    public static Error? CheckText(string text)
    {
        ArgumentNullException.ThrowIfNull(text);
        return text.Length > _maxTextLength ? Invalid($"A template text has at most {_maxTextLength} characters.") : null;
    }

    private static Error? ScopeError(string? scope)
    {
        if (scope is null)
        {
            return null;
        }

        var company = CompanyId.Create(scope);
        return company.IsError ? company.Error
            : ScopeFolders.FolderOf(scope)!.Length > _maxFolderNameLength
                ? Invalid($"The folder name of the company id has more than {_maxFolderNameLength} characters.")
                : null;
    }

    private static Error? PartError(string? part) =>
        TemplatePartNames.IsValid(part) ? null : Invalid($"The part {part} is not a name of ASCII letters, digits, and _ that starts with a letter.");

    private static Error Invalid(string message) => new(Errors.ValidationFailed, message);
}
