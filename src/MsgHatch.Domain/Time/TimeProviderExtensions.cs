namespace MsgHatch.Domain.Time;

internal static class TimeProviderExtensions
{
    public static DateTime GetUtcNowDateTime(this TimeProvider time) => time.GetUtcNow().UtcDateTime;
}
