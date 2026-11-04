using MsgHatch.Email.Placeholders;

namespace MsgHatch.Subscriptions.Tests.Unit.TestTypes;

/// <summary>The payload of the email registrations of the scenario.</summary>
/// <param name="Target">The id of the delivery target.</param>
[PlaceholderPayload]
public sealed partial record NoticeEmail(string Target);
