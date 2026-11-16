using MsgHatch.Templates.Tests.Unit.Contracts;

namespace MsgHatch.Templates.Tests.Unit.TestTypes;

/// <summary>
/// The test data of the embedded resource store: the build embeds the files of the folder <c>TemplateFiles</c>, with
/// the default resource names of MSBuild, so the scenario is always there.
/// </summary>
public sealed class EmbeddedTemplateFiles : ITemplateStoreTestData
{
    public const string Prefix = "MsgHatch.Templates.Tests.Unit.TemplateFiles";

    public ValueTask SeedAsync(CancellationToken ct) => ValueTask.CompletedTask;
}
