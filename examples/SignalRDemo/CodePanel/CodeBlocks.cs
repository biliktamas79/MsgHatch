namespace SignalRDemo.CodePanel;

/// <summary>
/// The blocks of the code panel (D316): the JavaScript of each step of a page, and the C# of the hub, the
/// <c>IUserIdProvider</c>, and the router. Each block is a region of a source file that the app embeds as a resource, so
/// it equals the code that runs. A region starts with <c>#region name</c> in C# or <c>//#region name</c> in JavaScript,
/// and ends with the next <c>#endregion</c> or <c>//#endregion</c>.
/// </summary>
public sealed class CodeBlocks
{
    private static readonly (string Title, string File, string Region)[] _sources =
    [
        ("1. Connect with no token", "wwwroot/js/session.js", "connect"),
        ("2. Join the tenant of the page", "wwwroot/js/session.js", "join-tenant"),
        ("3. Log in", "wwwroot/js/session.js", "login"),
        ("4. Connect again with the token", "wwwroot/js/session.js", "connect-with-token"),
        ("The hub", "Sessions/DemoHub.cs", "hub"),
        ("The IUserIdProvider", "Sessions/DemoUserIdProvider.cs", "user-id-provider"),
        ("The router of the host", "Notices/DemoRouter.cs", "router"),
    ];

    /// <summary>Reads each block from the embedded source files.</summary>
    /// <exception cref="InvalidOperationException">A source file or a region is missing. This is a programmer error.</exception>
    public CodeBlocks()
    {
        Blocks = [.. _sources.Select(static source => new CodeBlock(
            source.Title, source.File, source.Region, LanguageOf(source.File), RegionOf(Read(source.File), source.Region)))];
    }

    /// <summary>The blocks, in the order of the panel.</summary>
    public IReadOnlyList<CodeBlock> Blocks { get; }

    private static string Read(string file)
    {
        using var stream = typeof(CodeBlocks).Assembly.GetManifestResourceStream(file)
            ?? throw new InvalidOperationException($"The app does not embed the source file {file}.");
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    private static string RegionOf(string text, string region)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n');
        var start = Array.FindIndex(lines, line => line.Trim() is var marker && (marker == $"#region {region}" || marker == $"//#region {region}"));
        var end = start < 0 ? -1 : Array.FindIndex(lines, start + 1, static line => line.TrimStart().StartsWith("#endregion", StringComparison.Ordinal)
            || line.TrimStart().StartsWith("//#endregion", StringComparison.Ordinal));
        if (end < 0)
        {
            throw new InvalidOperationException($"The source has no region {region}.");
        }

        var body = lines[(start + 1)..end];
        var indent = body.Where(static line => line.Trim().Length > 0).Min(static line => line.Length - line.TrimStart().Length);
        return string.Join('\n', body.Select(line => line.Length < indent ? line.TrimStart() : line[indent..]));
    }

    private static string LanguageOf(string file) => file.EndsWith(".cs", StringComparison.Ordinal) ? "csharp" : "javascript";
}

/// <summary>One block of the code panel.</summary>
/// <param name="Title">The title.</param>
/// <param name="File">The source file, relative to the project folder.</param>
/// <param name="Region">The name of the region.</param>
/// <param name="Language">The language: <c>csharp</c> or <c>javascript</c>.</param>
/// <param name="Code">The code of the region, with no common indent.</param>
public sealed record CodeBlock(string Title, string File, string Region, string Language, string Code);
