using System.Buffers;

namespace MsgHatch.Email.Placeholders;

/// <summary>
/// Writes the top-level properties of one payload type into a text buffer, with no boxing and no reflection (D18).
/// The source generator writes one for each payload type.
/// </summary>
/// <typeparam name="TPayload">The payload type.</typeparam>
public interface IPlaceholderWriter<TPayload>
{
    /// <summary>Gives the index of a property, with an ordinal comparison of the name.</summary>
    /// <param name="propertyName">The property name.</param>
    /// <returns>The property index, or -1 for an unknown property name.</returns>
    int IndexOf(ReadOnlySpan<char> propertyName);

    /// <summary>
    /// Writes the value of one property. A <see cref="ISpanFormattable"/> value is formatted with the culture. A null
    /// value writes nothing.
    /// </summary>
    /// <param name="payload">The payload.</param>
    /// <param name="propertyIndex">The property index from <see cref="IndexOf"/>.</param>
    /// <param name="output">The buffer that gets the text.</param>
    /// <param name="culture">The culture of the formats.</param>
    /// <returns>False for an unknown property index.</returns>
    bool TryWrite(in TPayload payload, int propertyIndex, IBufferWriter<char> output, IFormatProvider culture);
}
