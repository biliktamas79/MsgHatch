namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>An envelope type with one more property than <see cref="BaseTestEnvelope"/>.</summary>
public sealed record DerivedTestEnvelope(string Text, string Extra) : BaseTestEnvelope(Text);
