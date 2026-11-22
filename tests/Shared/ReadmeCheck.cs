using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MsgHatch.Tests.Shared;

/// <summary>
/// The README check of D311. Each C# block of a package README equals a region <c>readme:name</c> of a test file, each
/// other checked block equals the file <c>Readme/name.language</c> of the test project, and each options table names
/// each settable property of its options class.
/// </summary>
internal static partial class ReadmeCheck
{
    private const string _projectDirectoryKey = "MsgHatch.TestProjectDirectory";
    private static readonly string[] _checkedLanguages = ["csharp", "json", "razor"];

    /// <summary>A fenced block of a README that follows a snippet marker, or no marker when the name is null.</summary>
    public sealed record Block(string? Name, string Language, string Content);

    /// <summary>A source file of a non-C# snippet, for example <c>Readme/options.json</c>.</summary>
    public sealed record SnippetFile(string Language, string Content);

    /// <summary>Asserts that each snippet of the README of the package equals its source in the test project.</summary>
    public static void AssertSnippets()
    {
        var project = TestProjectDirectory();
        Assert.Empty(FindSnippetFailures(File.ReadAllText(ReadmePath(project)), ReadRegions(project), ReadSnippetFiles(project)));
    }

    /// <summary>Asserts that the README has one options table for each given type, and each table names each option.</summary>
    public static void AssertOptionsTables(params Type[] optionsTypes) =>
        Assert.Empty(FindOptionsFailures(File.ReadAllText(ReadmePath(TestProjectDirectory())), optionsTypes));

    /// <summary>The text of the file <c>Readme/name.extension</c> of the test project.</summary>
    public static string ReadSnippetFile(string fileName) =>
        File.ReadAllText(Path.Combine(TestProjectDirectory(), "Readme", fileName));

    /// <summary>
    /// The settings of the JSON file <c>Readme/name.json</c> of the test project as configuration keys, for example
    /// <c>MsgHatch:Sqlite:ConnectionString</c>, with the index of an array item as a key part.
    /// </summary>
    public static Dictionary<string, string?> ReadSettings(string fileName)
    {
        using var document = JsonDocument.Parse(ReadSnippetFile(fileName));
        var settings = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        AddSettings(document.RootElement, prefix: null, settings);
        return settings;
    }

    private static void AddSettings(JsonElement element, string? prefix, Dictionary<string, string?> settings)
    {
        switch (element.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var property in element.EnumerateObject())
                {
                    AddSettings(property.Value, prefix is null ? property.Name : $"{prefix}:{property.Name}", settings);
                }

                break;
            case JsonValueKind.Array:
                var index = 0;
                foreach (var item in element.EnumerateArray())
                {
                    AddSettings(item, $"{prefix}:{index++}", settings);
                }

                break;
            case JsonValueKind.Null:
                settings[prefix!] = null;
                break;
            default:
                settings[prefix!] = element.ValueKind == JsonValueKind.String ? element.GetString() : element.GetRawText();
                break;
        }
    }

    /// <summary>The failures of the snippets of a README against the regions and the snippet files of its tests.</summary>
    public static IReadOnlyList<string> FindSnippetFailures(
        string readme, IReadOnlyDictionary<string, string> regions, IReadOnlyDictionary<string, SnippetFile> files)
    {
        var blocks = ReadBlocks(readme);
        var failures = new List<string>();
        foreach (var block in blocks.Where(b => b.Name is null && _checkedLanguages.Contains(b.Language)))
        {
            failures.Add($"A {block.Language} block has no snippet marker: {FirstLine(block.Content)}");
        }

        foreach (var block in blocks.Where(b => b.Name is not null))
        {
            failures.AddRange(CompareWithSource(block, regions, files));
        }

        var named = blocks.Where(b => b.Name is not null).ToLookup(b => b.Name!, StringComparer.Ordinal);
        failures.AddRange(regions.Keys
            .Where(name => !named[name].Any(b => b.Language == "csharp"))
            .Select(name => $"The region readme:{name} has no snippet."));
        failures.AddRange(files
            .Where(file => !named[file.Key].Any(b => b.Language == file.Value.Language))
            .Select(file => $"The file Readme/{file.Key}.{ExtensionOf(file.Value.Language)} has no snippet."));
        return failures;
    }

    /// <summary>The failures of the options tables of a README against the settable properties of the options types.</summary>
    public static IReadOnlyList<string> FindOptionsFailures(string readme, IReadOnlyCollection<Type> optionsTypes)
    {
        var tables = ReadOptionsTables(readme);
        var failures = new List<string>();
        foreach (var type in optionsTypes)
        {
            if (!tables.TryGetValue(type.Name, out var rows))
            {
                failures.Add($"The README has no options table of {type.Name}.");
                continue;
            }

            var options = SettablePropertyNames(type);
            failures.AddRange(options.Except(rows, StringComparer.Ordinal).Select(name => $"The table of {type.Name} has no row {name}."));
            failures.AddRange(rows.Except(options, StringComparer.Ordinal).Select(name => $"The table of {type.Name} names {name}, which is no option."));
        }

        failures.AddRange(tables.Keys
            .Where(name => optionsTypes.All(type => type.Name != name))
            .Select(name => $"The options table of {name} has no options type in the test."));
        return failures;
    }

    private static IEnumerable<string> CompareWithSource(
        Block block, IReadOnlyDictionary<string, string> regions, IReadOnlyDictionary<string, SnippetFile> files)
    {
        string? source = block.Language == "csharp"
            ? regions.GetValueOrDefault(block.Name!)
            : files.TryGetValue(block.Name!, out var file) && file.Language == block.Language ? file.Content : null;
        if (source is null)
        {
            yield return $"The snippet {block.Name} ({block.Language}) has no source.";
        }
        else if (Normalize(source) != Normalize(block.Content))
        {
            yield return $"The snippet {block.Name} differs from its source.\nREADME:\n{Normalize(block.Content)}\nSource:\n{Normalize(source)}";
        }
    }

    private static List<Block> ReadBlocks(string readme)
    {
        var text = Normalize(readme);
        return [.. FencedBlockRegex().Matches(text).Select(match => new Block(
            match.Groups["name"].Success ? match.Groups["name"].Value : null,
            match.Groups["language"].Value,
            match.Groups["content"].Value))];
    }

    private static Dictionary<string, HashSet<string>> ReadOptionsTables(string readme)
    {
        var tables = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var lines = Normalize(readme).Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var marker = OptionsMarkerRegex().Match(lines[i]);
            if (!marker.Success)
            {
                continue;
            }

            var rows = lines.Skip(i + 1).SkipWhile(string.IsNullOrWhiteSpace).TakeWhile(line => line.StartsWith('|')).Skip(2);
            tables[marker.Groups["type"].Value] = [.. rows.Select(row => row.Split('|')[1].Trim().Trim('`'))];
        }

        return tables;
    }

    private static HashSet<string> SettablePropertyNames(Type type) =>
        [.. type.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.SetMethod?.IsPublic == true || IsBindableCollection(property))
            .Select(property => property.Name)];

    private static bool IsBindableCollection(PropertyInfo property) =>
        property.GetMethod?.IsPublic == true
        && property.PropertyType != typeof(string)
        && typeof(System.Collections.IEnumerable).IsAssignableFrom(property.PropertyType);

    private static Dictionary<string, string> ReadRegions(string project)
    {
        var regions = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var file in SourceFiles(project, "*.cs"))
        {
            foreach (Match match in RegionRegex().Matches(Normalize(File.ReadAllText(file))))
            {
                if (!regions.TryAdd(match.Groups["name"].Value, Dedent(match.Groups["content"].Value)))
                {
                    Assert.Fail($"The region readme:{match.Groups["name"].Value} is in the test project two times.");
                }
            }
        }

        return regions;
    }

    private static Dictionary<string, SnippetFile> ReadSnippetFiles(string project)
    {
        var folder = Path.Combine(project, "Readme");
        var files = new Dictionary<string, SnippetFile>(StringComparer.Ordinal);
        if (!Directory.Exists(folder))
        {
            return files;
        }

        foreach (var file in Directory.GetFiles(folder))
        {
            var language = LanguageOf(Path.GetExtension(file));
            files.Add(Path.GetFileNameWithoutExtension(file), new SnippetFile(language, File.ReadAllText(file)));
        }

        return files;
    }

    private static IEnumerable<string> SourceFiles(string project, string pattern) =>
        Directory.EnumerateFiles(project, pattern, SearchOption.AllDirectories)
            .Where(file => !Path.GetRelativePath(project, file).Split(Path.DirectorySeparatorChar).Any(part => part is "bin" or "obj"));

    private static string TestProjectDirectory() =>
        typeof(ReadmeCheck).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(attribute => attribute.Key == _projectDirectoryKey).Value!;

    private static string ReadmePath(string project)
    {
        var testProject = Path.GetFileName(project.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        var package = TestLevelSuffixRegex().Replace(testProject, string.Empty);
        return Path.Combine(project, "..", "..", "src", package, "README.md");
    }

    private static string Dedent(string text)
    {
        var lines = text.Split('\n');
        var indent = lines.Where(line => line.Trim().Length > 0).Select(line => line.Length - line.TrimStart().Length).DefaultIfEmpty(0).Min();
        return string.Join('\n', lines.Select(line => line.Length >= indent ? line[indent..] : line.TrimStart()));
    }

    private static string Normalize(string text) =>
        string.Join('\n', text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n').Select(line => line.TrimEnd())).Trim('\n');

    private static string FirstLine(string text) => Normalize(text).Split('\n')[0];

    private static string LanguageOf(string extension) => extension.TrimStart('.') switch
    {
        "cs" => "csharp",
        var other => other,
    };

    private static string ExtensionOf(string language) => language == "csharp" ? "cs" : language;

    [GeneratedRegex(@"(?:^\[//\]: \# \(snippet: (?<name>[\w.-]+)\)\n+)?^```(?<language>\w+)\n(?<content>.*?)\n```$", RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex FencedBlockRegex();

    [GeneratedRegex(@"^\[//\]: \# \(options: (?<type>\w+)\)$")]
    private static partial Regex OptionsMarkerRegex();

    [GeneratedRegex(@"^[ \t]*#region readme:(?<name>[\w.-]+)[ \t]*\n(?<content>.*?)\n[ \t]*#endregion", RegexOptions.Singleline | RegexOptions.Multiline)]
    private static partial Regex RegionRegex();

    [GeneratedRegex(@"\.Tests\.(Unit|Integration)$")]
    private static partial Regex TestLevelSuffixRegex();
}
