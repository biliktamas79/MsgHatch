using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Templates.Api.Contracts;
using MsgHatch.Templates.Management;

namespace MsgHatch.Templates.Api.Contracts.Tests.Unit;

/// <summary>The mappings of the template bodies in both directions (D228), and the HTTP concerns of the contracts package.</summary>
public sealed class TemplateBodiesUnitTests
{
    private static readonly TemplateLookup _lookup = new(new MessageType("OrderShipped"), "hu", "acme-1", "subject", TemplatePartKind.PlainText);

    public static TheoryData<string, string, string?, string> InvalidLookupRows => new()
    {
        { "a message type that is not a name", "1Order", null, "subject" },
        { "an empty company id", "OrderShipped", "", "subject" },
        { "a company id with a control character", "OrderShipped", "acme\u0001", "subject" },
        { "a part with a dot", "OrderShipped", null, "sub.ject" },
    };

    [Fact]
    public void ToEntry_ResponseOfAnEntry_GivesAnEqualEntry()
    {
        // Arrange
        var entry = new TemplateEntry(_lookup, 638_000_000_000_000_000);

        // Act
        var roundTrip = TemplateEntryResponse.From(entry).ToEntry();

        // Assert
        Assert.Equal(entry, roundTrip.Value);
    }

    [Theory]
    [MemberData(nameof(InvalidLookupRows))]
    public void ToEntry_ValueThatIsNotValid_GivesSerializationFailed(string row, string messageType, string? companyId, string part)
    {
        // Arrange
        var response = new TemplateEntryResponse(messageType, "hu", companyId, part, TemplatePartKind.PlainText, 5);

        // Act
        var entry = response.ToEntry();

        // Assert
        Assert.Equal((row, Errors.SerializationFailed.Code), (row, entry.Error?.Type.Code));
    }

    [Fact]
    public void ToSource_ResponseOfASource_GivesTheLookupTheVersionTheTextAndAStableIdOfTheLookup()
    {
        // Arrange
        var source = new TemplateSource(new TemplateKey(@"C:\host\templates\acme-1\OrderShipped.hu.subject.txt", 5), "Rendelés {{ Name }}");

        // Act
        var response = TemplateResponse.From(_lookup, source);
        var first = response.ToSource();
        var second = response.ToSource();

        // Assert
        Assert.Equal(
            (_lookup, 5L, source.Text, first.Value.Key.Id, false),
            (response.ToLookup().Value, first.Value.Key.Version, first.Value.Text, second.Value.Key.Id, first.Value.Key.Id.Contains(@"C:\host", StringComparison.Ordinal)));
    }

    [Fact]
    public void ToSource_TwoLookupsOfOtherCompanies_GiveTwoIds()
    {
        // Arrange
        var acme = new TemplateResponse("OrderShipped", "hu", "acme-1", "subject", TemplatePartKind.PlainText, 5, "A");
        var globex = acme with { CompanyId = "globex" };
        var shared = acme with { CompanyId = null };

        // Act
        string[] ids = [acme.ToSource().Value.Key.Id, globex.ToSource().Value.Key.Id, shared.ToSource().Value.Key.Id];

        // Assert
        Assert.Equal(3, ids.Distinct(StringComparer.Ordinal).Count());
    }

    [Fact]
    public void ToIssue_ResponseOfAnIssue_GivesAnEqualIssue()
    {
        // Arrange
        var issue = new TemplateIssue("An identifier was expected.", 3, 13);

        // Act
        var roundTrip = TemplateIssueResponse.From(issue).ToIssue();

        // Assert
        Assert.Equal(issue, roundTrip.Value);
    }

    [Theory]
    [InlineData(0, 1)]
    [InlineData(1, 0)]
    public void ToIssue_PositionBelow1_GivesSerializationFailed(int line, int column)
    {
        // Act
        var issue = new TemplateIssueResponse("An issue.", line, column).ToIssue();

        // Assert
        Assert.Equal(Errors.SerializationFailed.Code, issue.Error?.Type.Code);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void ToPartInfo_ResponseOfADeclaredPart_GivesTheSameValues(bool hasSample)
    {
        // Arrange
        var info = new TemplatePartInfo(new MessageType("OrderShipped"), new TemplatePart("body", TemplatePartKind.Html), ["placeholder", "liquid"], hasSample);

        // Act
        var roundTrip = TemplatePartResponse.From(info).ToPartInfo().Value;

        // Assert
        Assert.Equal(
            (info.Type, info.Part, "placeholder, liquid", hasSample),
            (roundTrip.Type, roundTrip.Part, string.Join(", ", roundTrip.Engines), roundTrip.HasSample));
    }

    [Theory]
    [InlineData("1Order", "body")]
    [InlineData("OrderShipped", "bo dy")]
    public void ToPartInfo_NameThatIsNotValid_GivesSerializationFailed(string messageType, string part)
    {
        // Act
        var info = new TemplatePartResponse(messageType, part, TemplatePartKind.Html, ["liquid"], HasSample: false).ToPartInfo();

        // Assert
        Assert.Equal(Errors.SerializationFailed.Code, info.Error?.Type.Code);
    }

    [Fact]
    public void ContractsAssembly_ReferencesAndPublicTypes_HaveNoHttpConcernAndNoException()
    {
        // Arrange
        var assembly = typeof(TemplateEntryResponse).Assembly;

        // Act
        string[] httpReferences =
        [
            .. assembly.GetReferencedAssemblies().Select(static reference => reference.Name!)
                .Where(static name => name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal) || name == "System.Net.Http"),
        ];
        string[] exceptions = [.. assembly.GetExportedTypes().Where(static type => typeof(Exception).IsAssignableFrom(type)).Select(static type => type.Name)];

        // Assert
        Assert.Equal((0, 0, true), (httpReferences.Length, exceptions.Length, assembly.GetExportedTypes().Length > 0));
    }
}
