using System.Text;

namespace MsgHatch.Email.AmazonSES;

/// <summary>
/// The MIME encoded-word syntax of RFC 2047, which SES requires for a sender name with letters outside 7-bit ASCII. An
/// encoded-word is at most 75 characters, so one word holds at most 45 bytes of UTF-8 text (60 Base64 characters).
/// </summary>
internal static class EncodedWords
{
    private const int _maxBytesOfAWord = 45;

    /// <summary>Gives an ASCII text unchanged, and each other text as encoded-words, divided only between letters.</summary>
    public static string Of(string text) => Ascii.IsValid(text) ? text : string.Join(' ', WordsOf(text));

    private static List<string> WordsOf(string text)
    {
        var words = new List<string>();
        var word = new List<byte>(_maxBytesOfAWord);
        foreach (var rune in text.EnumerateRunes())
        {
            var bytes = Encoding.UTF8.GetBytes(rune.ToString());
            if (word.Count + bytes.Length > _maxBytesOfAWord)
            {
                words.Add(WordOf(word));
                word.Clear();
            }

            word.AddRange(bytes);
        }

        words.Add(WordOf(word));
        return words;
    }

    private static string WordOf(List<byte> bytes) => $"=?UTF-8?B?{Convert.ToBase64String([.. bytes])}?=";
}
