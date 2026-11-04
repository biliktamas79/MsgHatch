namespace MsgHatch.Subscriptions.Tests.Unit.TestTypes;

/// <summary>The envelope of the test channel <c>Pager</c>.</summary>
/// <param name="Text">The text.</param>
public sealed record PagerEnvelope(string Text);
