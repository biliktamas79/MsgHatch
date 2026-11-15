using System.Globalization;

namespace MsgHatch.Email.Liquid.Tests.Unit.TestTypes;

/// <summary>
/// The payload of the Liquid rules: an order with lines and no <c>[PlaceholderPayload]</c> (D209). It is a class and
/// not a record, so that no <c>ToString</c> reads <see cref="Failing"/>.
/// </summary>
public sealed class LineOrderEmail(string name, IReadOnlyList<OrderLine> lines, string? note = null)
{
    public string Name { get; } = name;

    public IReadOnlyList<OrderLine> Lines { get; } = lines;

    public string? Note { get; } = note;

    /// <summary>A value of a type of the .NET runtime, whose members a template cannot read (D209).</summary>
    public TimeZoneInfo Zone { get; } = TimeZoneInfo.Utc;

    /// <summary>A value of a type in a namespace below <c>System</c>, whose members a template cannot read (D209).</summary>
    public CultureInfo Culture { get; } = CultureInfo.InvariantCulture;

    /// <summary>Payload code that throws when a template reads it.</summary>
    public string Failing => throw new InvalidOperationException($"The payload code of {Name} failed.");

    public static LineOrderEmail AnOrder(string name = "u1", string? note = null) =>
        new(name, [new OrderLine("Pump", 2), new OrderLine("Valve", 1)], note);
}

/// <summary>One line of a <see cref="LineOrderEmail"/>, a type that the host does not register (D209).</summary>
public sealed record OrderLine(string Product, int Quantity);
