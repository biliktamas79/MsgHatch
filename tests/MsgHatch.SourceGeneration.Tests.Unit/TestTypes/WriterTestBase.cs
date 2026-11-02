namespace MsgHatch.SourceGeneration.Tests.Unit.TestTypes;

/// <summary>The base type of <see cref="WriterTestPayload"/>, so that the writer tests cover a property of a base type.</summary>
public abstract record WriterTestBase(string BaseText);
