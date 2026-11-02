using System.Buffers;
using System.Globalization;
using MsgHatch.Email.Placeholders;
using MsgHatch.SourceGeneration.Tests.Unit.TestTypes;

namespace MsgHatch.SourceGeneration.Tests.Unit;

public sealed partial class PlaceholderPayloadGeneratorUnitTests
{
    private static readonly CultureInfo _english = CultureInfo.GetCultureInfo("en-US");
    private static readonly Guid _orderId = Guid.Parse("6f9619ff-8b86-d011-b42d-00c04fc964ff");
    private static readonly DateTime _shippedAt = new(2030, 1, 2, 15, 4, 5, DateTimeKind.Utc);

    public static TheoryData<string, string> PropertyRows => new()
    {
        { nameof(WriterTestPayload.Name), "Ödön Űr <b>&\"" },
        { nameof(WriterTestPayload.Note), "" },
        { nameof(WriterTestPayload.Count), "-5" },
        { nameof(WriterTestPayload.Rank), "" },
        { nameof(WriterTestPayload.OrderId), "6f9619ff-8b86-d011-b42d-00c04fc964ff" },
        { nameof(WriterTestPayload.Express), "True" },
        { nameof(WriterTestPayload.Extra), "extra" },
    };

    public static TheoryData<string> CultureRows => new() { "en-US", "hu-HU", "ar-SA" };

    [Theory]
    [MemberData(nameof(PropertyRows))]
    public void TryWrite_PropertyOfEachKind_WritesItsValue(string propertyName, string expectedText)
    {
        // Arrange
        var payload = APayload(name: "Ödön Űr <b>&\"", note: null, count: -5, rank: null, extra: "extra");

        // Act
        var text = Write(payload, propertyName, _english);

        // Assert
        Assert.Equal(expectedText, text);
    }

    [Theory]
    [MemberData(nameof(CultureRows))]
    public void TryWrite_DateTimeProperty_WritesItInTheGivenCulture(string language)
    {
        // Arrange
        var culture = CultureInfo.GetCultureInfo(language);

        // Act
        var text = Write(APayload(), nameof(WriterTestPayload.ShippedAt), culture);

        // Assert
        Assert.Equal(_shippedAt.ToString(culture), text);
    }

    [Fact]
    public void TryWrite_ValueLongerThanTheFirstSpan_AsksForALargerSpanAndWritesItWhole()
    {
        // Arrange
        var output = new ArrayBufferWriter<char>(1);
        var writer = Writer<WriterTestPayload>();

        // Act
        writer.TryWrite(APayload(), writer.IndexOf(nameof(WriterTestPayload.OrderId)), output, _english);

        // Assert
        Assert.Equal("6f9619ff-8b86-d011-b42d-00c04fc964ff", output.WrittenSpan.ToString());
    }

    [Fact]
    public void TryWrite_UnknownPropertyIndex_ReturnsFalseAndWritesNothing()
    {
        // Arrange
        var output = new ArrayBufferWriter<char>();

        // Act
        var written = Writer<WriterTestPayload>().TryWrite(APayload(), 99, output, _english);

        // Assert
        Assert.Equal((false, 0), (written, output.WrittenCount));
    }

    [Theory]
    [InlineData("Name", 0)]
    [InlineData("Extra", 7)]
    [InlineData("name", -1)]
    [InlineData("Unknown", -1)]
    [InlineData("", -1)]
    public void IndexOf_PropertyName_GivesItsIndexWithAnOrdinalComparison(string propertyName, int expectedIndex)
    {
        // Arrange
        var writer = Writer<WriterTestPayload>();

        // Act
        var index = writer.IndexOf(propertyName);

        // Assert
        Assert.Equal(expectedIndex, index);
    }

    [Fact]
    public void TryWrite_IntAndDateTimeProperty_AllocatesZeroBytes()
    {
        // Arrange
        var payload = APayload();
        var writer = Writer<WriterTestPayload>();
        var count = writer.IndexOf(nameof(WriterTestPayload.Count));
        var shippedAt = writer.IndexOf(nameof(WriterTestPayload.ShippedAt));
        var output = new ArrayBufferWriter<char>(256);
        WriteTwoValues(writer, payload, count, shippedAt, output);
        var before = GC.GetAllocatedBytesForCurrentThread();

        // Act
        WriteTwoValues(writer, payload, count, shippedAt, output);

        // Assert
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static void WriteTwoValues(
        IPlaceholderWriter<WriterTestPayload> writer, WriterTestPayload payload, int count, int shippedAt, ArrayBufferWriter<char> output)
    {
        output.ResetWrittenCount();
        writer.TryWrite(payload, count, output, _english);
        writer.TryWrite(payload, shippedAt, output, _english);
    }

    private static string Write(WriterTestPayload payload, string propertyName, CultureInfo culture)
    {
        var writer = Writer<WriterTestPayload>();
        var output = new ArrayBufferWriter<char>();
        writer.TryWrite(payload, writer.IndexOf(propertyName), output, culture);
        return output.WrittenSpan.ToString();
    }

    private static IPlaceholderWriter<TPayload> Writer<TPayload>()
        where TPayload : IPlaceholderPayload<TPayload> =>
        TPayload.Writer;

    private static WriterTestPayload APayload(
        string name = "Client 1", string? note = "note", int count = 3, int? rank = 1, object? extra = null) =>
        new(name, note, count, rank, _shippedAt, _orderId, Express: true, extra);
}
