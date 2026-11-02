using MsgHatch.Email.Templates;
using MsgHatch.Email.Tests.Unit.Contracts;
using MsgHatch.Email.Tests.Unit.TestTypes;

namespace MsgHatch.Email.Tests.Unit.Templates;

public sealed class EmbeddedResourceTemplateStoreUnitTests()
    : TemplateStoreTestsBase<EmbeddedResourceTemplateStore>(
        new EmbeddedResourceTemplateStore(typeof(EmbeddedResourceTemplateStoreUnitTests).Assembly, EmbeddedTemplateFiles.Prefix),
        new EmbeddedTemplateFiles())
{
    [Fact]
    public async Task FindAsync_EmbeddedTemplate_HasTheAssemblyAndTheResourceNameAsItsIdAndTheVersion0()
    {
        // Arrange
        var lookup = new TemplateLookup(TestEvent, "en", Scope: null);

        // Act
        var result = await Store.FindAsync(lookup, Ct);

        // Assert
        Assert.Equal(
            new TemplateKey("MsgHatch.Email.Tests.Unit:MsgHatch.Email.Tests.Unit.TemplateFiles.TestEvent.en", 0),
            result.Value?.Key);
    }
}
