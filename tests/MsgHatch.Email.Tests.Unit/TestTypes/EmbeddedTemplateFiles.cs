using MsgHatch.Email.Tests.Unit.Contracts;

namespace MsgHatch.Email.Tests.Unit.TestTypes;

/// <summary>
/// The test data of the embedded resource store: the build embeds the files of the folder <c>TemplateFiles</c>, so
/// the scenario is always there.
/// </summary>
public sealed class EmbeddedTemplateFiles : ITemplateStoreTestData
{
    public const string Prefix = "MsgHatch.Email.Tests.Unit.TemplateFiles";

    public static IEnumerable<(string FileName, string ResourceName)> Names() =>
        typeof(EmbeddedTemplateFiles).Assembly.GetManifestResourceNames()
            .Where(static name => name.StartsWith(Prefix + ".", StringComparison.Ordinal))
            .Select(static name => (name[(Prefix.Length + 1)..], name));

    public ValueTask SeedAsync(CancellationToken ct) => ValueTask.CompletedTask;
}
