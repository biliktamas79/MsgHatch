using System.Reflection;
using MsgHatch.Domain.Failures;
using MsgHatch.Email.Templates;
using MsgHatch.Email.Tests.Unit.Contracts;
using MsgHatch.Email.Tests.Unit.TestTypes;
using NSubstitute;

namespace MsgHatch.Email.Tests.Unit.Templates;

public sealed class EmbeddedResourceTemplateStoreUnitTests()
    : TemplateStoreTestsBase<EmbeddedResourceTemplateStore>(
        new EmbeddedResourceTemplateStore(typeof(EmbeddedResourceTemplateStoreUnitTests).Assembly, EmbeddedTemplateFiles.Prefix),
        new EmbeddedTemplateFiles())
{
    public static TheoryData<string, Assembly?, string?> ConstructorRows => new()
    {
        { "no assembly", null, "Prefix" },
        { "a null prefix", typeof(EmbeddedResourceTemplateStoreUnitTests).Assembly, null },
        { "an empty prefix", typeof(EmbeddedResourceTemplateStoreUnitTests).Assembly, "" },
        { "a prefix of only spaces", typeof(EmbeddedResourceTemplateStoreUnitTests).Assembly, " " },
    };

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

    [Fact]
    public async Task FindAsync_ResourceThatCannotBeRead_GivesUnavailable()
    {
        // Arrange
        var assembly = Substitute.For<Assembly>();
        assembly.GetManifestResourceStream(Arg.Any<string>()).Returns(static _ => throw new IOException("The resource is locked."));
        var store = new EmbeddedResourceTemplateStore(assembly, "Prefix");

        // Act
        var result = await store.FindAsync(new TemplateLookup(TestEvent, "en", Scope: null), Ct);

        // Assert
        Assert.Equal(Errors.Unavailable, result.Error?.Type);
    }

    [Theory]
    [MemberData(nameof(ConstructorRows))]
    public void Constructor_NoAssemblyOrNoPrefix_ThrowsArgumentException(string row, Assembly? assembly, string? prefix)
    {
        // Arrange
        var create = () => new EmbeddedResourceTemplateStore(assembly!, prefix!);

        // Act
        var exception = Record.Exception(create);

        // Assert
        Assert.Equal((row, true), (row, exception is ArgumentException));
    }
}
