namespace MsgHatch.Subscriptions.Tests.Unit.TestTypes;

/// <summary>The domain event of the test message type P, on the test channel <c>Pager</c> only.</summary>
/// <param name="Users">The users of the scope.</param>
public sealed record PagerEvent(string[] Users);
