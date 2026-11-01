using MsgHatch.Domain.Dispatch;
using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Tests.Unit.Dispatch;

public sealed class DispatchTopicUnitTests
{
    [Fact]
    public void Inbox_Name_IsInbox()
    {
        // Act
        var topic = DispatchTopic.Inbox;

        // Assert
        Assert.Equal("inbox", topic.Name);
    }

    [Fact]
    public void ForChannel_TwoChannels_GivesTheOutboxTopicOfEach()
    {
        // Act
        DispatchTopic[] topics = [DispatchTopic.ForChannel(new ChannelId("Email")), DispatchTopic.ForChannel(new ChannelId("SignalR"))];

        // Assert
        Assert.Equal(["outbox:Email", "outbox:SignalR"], topics.Select(static topic => topic.Name));
    }
}
