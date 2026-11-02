using System.Buffers;
using System.Text.Encodings.Web;
using MsgHatch.Email.Templates;

namespace MsgHatch.Email.Placeholders;

/// <summary>
/// Renders one parsed part for one payload (section 4.10). It writes each segment into a pooled buffer. In an
/// <see cref="TemplatePartKind.Html"/> part, it encodes each value with <see cref="HtmlEncoder.Default"/>. In the other
/// parts, it does not encode.
/// </summary>
internal static class PlaceholderRenderer
{
    public static string Render<TPayload>(
        ParsedTemplate.Part part, in TPayload payload, IPlaceholderWriter<TPayload> writer, IFormatProvider culture,
        PooledCharBufferWriter output, PooledCharBufferWriter value)
    {
        output.Clear();
        foreach (var segment in part.Segments)
        {
            if (segment.Literal is not null)
            {
                output.Write(segment.Literal);
            }
            else if (part.Kind == TemplatePartKind.Html)
            {
                value.Clear();
                writer.TryWrite(payload, segment.PropertyIndex, value, culture);
                Encode(value.WrittenSpan, output);
            }
            else
            {
                writer.TryWrite(payload, segment.PropertyIndex, output, culture);
            }
        }

        return output.WrittenSpan.ToString();
    }

    private static void Encode(ReadOnlySpan<char> source, PooledCharBufferWriter output)
    {
        var status = OperationStatus.DestinationTooSmall;
        while (status != OperationStatus.Done)
        {
            status = HtmlEncoder.Default.Encode(source, output.GetSpan(source.Length + 16), out var consumed, out var written);
            output.Advance(written);
            source = source[consumed..];
        }
    }
}
