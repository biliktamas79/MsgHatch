using MsgHatch.Domain.Management;

namespace MsgHatch.Api.Contracts.Tests.Unit;

/// <summary>The text of a cursor of the delivery API, which the routes and the client share (D112, D291).</summary>
public sealed class DeliveryApiRoutesUnitTests
{
    private static readonly MessageCursor _cursor = new(new DateTime(2030, 1, 1, 10, 0, 0, DateTimeKind.Utc).AddTicks(1234567), new Guid("7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31"));

    [Fact]
    public void CursorText_Cursor_GivesTheTimeWithSevenDigitsAndTheId()
    {
        // Act
        var text = DeliveryApiRoutes.CursorText(_cursor);

        // Assert
        Assert.Equal("2030-01-01T10:00:00.1234567Z_7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31", text);
    }

    [Fact]
    public void TryParseCursor_TextOfACursor_GivesTheSameCursor()
    {
        // Act
        var parsed = DeliveryApiRoutes.TryParseCursor(DeliveryApiRoutes.CursorText(_cursor), out var cursor);

        // Assert
        Assert.Equal((true, _cursor, DateTimeKind.Utc), (parsed, cursor, cursor.CreatedAtUtc.Kind));
    }

    [Theory]
    [InlineData("no separator", "2030-01-01T10:00:00.1234567Z")]
    [InlineData("a separator first", "_7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31")]
    [InlineData("a time with six digits", "2030-01-01T10:00:00.123456Z_7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31")]
    [InlineData("a time with an offset", "2030-01-01T10:00:00.1234567+01:00_7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31")]
    [InlineData("an id that is not a GUID", "2030-01-01T10:00:00.1234567Z_x")]
    [InlineData("an id with braces", "2030-01-01T10:00:00.1234567Z_{7d2e4b1a-5c3f-5a9e-8b10-4f6a2d9c0e31}")]
    public void TryParseCursor_TextThatIsNotACursor_GivesFalse(string row, string text)
    {
        // Act
        var parsed = DeliveryApiRoutes.TryParseCursor(text, out var cursor);

        // Assert
        Assert.Equal((row, false, default(MessageCursor)), (row, parsed, cursor));
    }
}
