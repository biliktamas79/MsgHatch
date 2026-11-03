namespace MsgHatch.SignalR;

/// <summary>The id parts of the SignalR names, with no <c>:</c>, so that two ids never give the same name (D114).</summary>
internal static class SignalRNames
{
    public static string Escape(string id) =>
        id.AsSpan().IndexOfAny(':', '%') < 0
            ? id
            : id.Replace("%", "%25", StringComparison.Ordinal).Replace(":", "%3A", StringComparison.Ordinal);
}
