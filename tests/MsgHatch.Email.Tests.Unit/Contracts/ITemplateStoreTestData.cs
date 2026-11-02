namespace MsgHatch.Email.Tests.Unit.Contracts;

/// <summary>The test data of <see cref="TemplateStoreTestsBase{TStore}"/>: what the template store interface cannot do.</summary>
public interface ITemplateStoreTestData
{
    /// <summary>
    /// Puts the scenario into the store: each file of the folder <c>TemplateFiles</c> of this project, with the same name.
    /// A second call changes nothing.
    /// </summary>
    ValueTask SeedAsync(CancellationToken ct);
}
