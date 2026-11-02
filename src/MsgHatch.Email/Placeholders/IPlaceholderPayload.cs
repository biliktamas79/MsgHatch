namespace MsgHatch.Email.Placeholders;

/// <summary>A template payload of the placeholder engine. The source generator implements it (D18).</summary>
/// <typeparam name="TSelf">The payload type itself.</typeparam>
public interface IPlaceholderPayload<TSelf>
    where TSelf : IPlaceholderPayload<TSelf>
{
    /// <summary>The generated writer of the properties of the payload type.</summary>
    static abstract IPlaceholderWriter<TSelf> Writer { get; }
}
