using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Dispatch;

/// <summary>The topic of a dispatch loop for the dispatch signal (D102): the inbox, or one channel of the outbox.</summary>
/// <param name="Name">The name, for example <c>inbox</c> or <c>outbox:Email</c>.</param>
public readonly record struct DispatchTopic(string Name)
{
    /// <summary>The topic of the inbox loop, with the name <c>inbox</c>.</summary>
    public static DispatchTopic Inbox { get; } = new("inbox");

    /// <summary>Gives the topic of the outbox loop of one channel, with the name <c>outbox:{Channel}</c>.</summary>
    /// <param name="channel">The channel.</param>
    /// <returns>The topic.</returns>
    public static DispatchTopic ForChannel(ChannelId channel) => new($"outbox:{channel.Name}");
}
