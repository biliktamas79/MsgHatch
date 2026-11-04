namespace MsgHatch.Domain.Tests.Unit.TestTypes;

/// <summary>An options class that two test setup methods share (D150), with the section <c>Test:Shared</c>.</summary>
public sealed class SharedTestOptions
{
    public const string SectionName = "Test:Shared";

    public string Value { get; set; } = string.Empty;
}
