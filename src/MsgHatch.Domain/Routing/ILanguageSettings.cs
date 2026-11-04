namespace MsgHatch.Domain.Routing;

/// <summary>
/// Channel settings with a language (D145). A router reads the language of the settings through it, with no reference
/// to the channel package. Settings that do not implement it have no language.
/// </summary>
public interface ILanguageSettings
{
    /// <summary>
    /// The language, or null for the UI language of the user, or the default language of the company for a company target.
    /// </summary>
    string? Language { get; }
}
