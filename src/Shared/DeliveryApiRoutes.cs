using System.Globalization;
using MsgHatch.Domain.Management;

namespace MsgHatch;

/// <summary>
/// The route paths of the delivery API under its prefix, the names of its query values, and the text of a cursor
/// <c>{CreatedAtUtc}_{Id}</c> with the time in the format of D57 (section 4.19, D291). <c>MsgHatch.AspNetCore</c> and the
/// client package compile this file, so a change of a route changes both sides in one commit.
/// </summary>
internal static class DeliveryApiRoutes
{
    public const string Channels = "/channels";
    public const string ChannelPause = Channels + "/{channel}/pause";
    public const string ChannelResume = Channels + "/{channel}/resume";
    public const string Traffic = "/traffic";
    public const string Inbox = "/inbox";
    public const string InboxRetry = Inbox + "/retry";
    public const string InboxCancel = Inbox + "/cancel";
    public const string ReceivedContent = Inbox + "/{id:guid}/content";
    public const string Outbox = "/outbox";
    public const string OutgoingMessage = Outbox + "/{id:guid}";
    public const string OutboxRetry = Outbox + "/retry";
    public const string OutboxCancel = Outbox + "/cancel";
    public const string OutgoingContent = OutgoingMessage + "/content";
    public const string Purge = "/purge";
    public const string TestData = "/test-data";

    private const string _cursorTimeFormat = "yyyy-MM-dd'T'HH:mm:ss.fffffff'Z'";
    private const DateTimeStyles _utc = DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal;

    /// <summary>Gives the text of a cursor.</summary>
    public static string CursorText(MessageCursor cursor) =>
        $"{cursor.CreatedAtUtc.ToString(_cursorTimeFormat, CultureInfo.InvariantCulture)}_{cursor.Id:D}";

    /// <summary>Reads the text of a cursor. A text that is not <c>{CreatedAtUtc}_{Id}</c> gives false.</summary>
    public static bool TryParseCursor(string text, out MessageCursor cursor)
    {
        var separator = text.LastIndexOf('_');
        if (separator > 0
            && DateTime.TryParseExact(text[..separator], _cursorTimeFormat, CultureInfo.InvariantCulture, _utc, out var createdAtUtc)
            && Guid.TryParseExact(text[(separator + 1)..], "D", out var id))
        {
            cursor = new MessageCursor(createdAtUtc, id);
            return true;
        }

        cursor = default;
        return false;
    }

    /// <summary>The names of the query values of the delivery routes.</summary>
    public static class Query
    {
        public const string TenantId = "tenantId";
        public const string Ids = "ids";
        public const string Status = "status";
        public const string MessageType = "messageType";
        public const string Channel = "channel";
        public const string TargetKind = "targetKind";
        public const string TargetId = "targetId";
        public const string SourceMessageId = "sourceMessageId";
        public const string CreatedFrom = "createdFrom";
        public const string CreatedTo = "createdTo";
        public const string DueBefore = "dueBefore";
        public const string IncludeTest = "includeTest";
        public const string Limit = "limit";
        public const string After = "after";
    }
}
