using MsgHatch.Domain.Delivery;

namespace MsgHatch.Domain.Management;

/// <summary>The store result of <c>IOutboxStore.GetAsync</c>: the whole message as its closed type, and all its tries.</summary>
/// <param name="Message">The message.</param>
/// <param name="Tries">The publish tries, in the order of their numbers.</param>
public sealed record OutgoingMessageHistory(OutgoingMessage Message, IReadOnlyList<PublishTry> Tries);
