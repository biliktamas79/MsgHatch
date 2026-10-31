namespace MsgHatch.Domain.Routing;

/// <summary>The features of a channel (D77).</summary>
[Flags]
public enum ChannelFeatures
{
    /// <summary>No feature.</summary>
    None = 0,

    /// <summary>The channel sends text in the language of the route.</summary>
    Languages = 1,

    /// <summary>The channel sends rich text.</summary>
    RichText = 2,

    /// <summary>The channel sends the payload as data.</summary>
    RawData = 4,

    /// <summary>The channel sends one message to a whole company.</summary>
    GroupDelivery = 8,
}
