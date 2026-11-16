using MsgHatch.Templates.Placeholders;

namespace ExampleApp.Notices;

/// <summary>The payload of the email templates of the message types A to E.</summary>
/// <param name="Text">The text of the notification.</param>
/// <param name="Target">The id of the user or the company that gets the email.</param>
[PlaceholderPayload]
public sealed partial record NoticeEmail(string Text, string Target);
