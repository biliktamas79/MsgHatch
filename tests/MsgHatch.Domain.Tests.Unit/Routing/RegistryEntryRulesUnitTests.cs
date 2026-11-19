using MsgHatch.Domain.Failures;
using MsgHatch.Domain.Identity;
using MsgHatch.Domain.Routing;
using static MsgHatch.Domain.Tests.Unit.TestTypes.RegistryTestHost;

namespace MsgHatch.Domain.Tests.Unit.Routing;

/// <summary>The stateless rules of a registry entry (section 4.20, D171, D285), which need no registry.</summary>
public sealed class RegistryEntryRulesUnitTests
{
    public static TheoryData<string, string?, string?, int, bool> ChannelEntryRows => new()
    {
        { "no texts", null, null, 0, true },
        { "display name empty", "", null, 0, false },
        { "display name white space", " \t", null, 0, false },
        { "display name 200 characters", new string('n', 200), null, 0, true },
        { "display name 201 characters", new string('n', 201), null, 0, false },
        { "description empty", null, "", 0, false },
        { "description white space", null, "  ", 0, false },
        { "description 2000 characters", null, new string('d', 2000), 0, true },
        { "description 2001 characters", null, new string('d', 2001), 0, false },
        { "revision -1", null, null, -1, false },
        { "revision 3", null, null, 3, true },
    };

    public static TheoryData<string, ChannelId[]?, string?, string?, int, bool> MessageTypeEntryRows => new()
    {
        { "disabled channels empty", [], null, null, 0, true },
        { "disabled channels null", null, null, null, 0, false },
        { "two different disabled channels", [new ChannelId("Third"), new ChannelId("Test")], null, null, 0, true },
        { "duplicate disabled channel", [new ChannelId("Third"), new ChannelId("Third")], null, null, 0, false },
        { "disabled channel that the rules cannot check", [new ChannelId("Unknown")], null, null, 0, true },
        { "display name white space", [], " ", null, 0, false },
        { "display name 201 characters", [], new string('n', 201), null, 0, false },
        { "description empty", [], null, "", 0, false },
        { "description 2001 characters", [], null, new string('d', 2001), 0, false },
        { "revision -1", [], null, null, -1, false },
    };

    [Theory]
    [MemberData(nameof(ChannelEntryRows))]
    public void Check_ChannelEntry_GivesValidationFailedOnlyForAnInvalidEntry(
        string row, string? displayName, string? description, int revision, bool valid)
    {
        // Arrange
        var entry = AChannelEntry(displayName: displayName, description: description, revision: revision);

        // Act
        var error = RegistryEntryRules.Check(entry);

        // Assert
        Assert.Equal((row, valid ? null : Errors.ValidationFailed.Code), (row, error?.Type.Code));
    }

    [Theory]
    [MemberData(nameof(MessageTypeEntryRows))]
    public void Check_MessageTypeEntry_GivesValidationFailedOnlyForAnInvalidEntry(
        string row, ChannelId[]? disabledChannels, string? displayName, string? description, int revision, bool valid)
    {
        // Arrange
        var entry = AMessageTypeEntry(displayName: displayName, description: description, revision: revision) with { DisabledChannels = disabledChannels! };

        // Act
        var error = RegistryEntryRules.Check(entry);

        // Assert
        Assert.Equal((row, valid ? null : Errors.ValidationFailed.Code), (row, error?.Type.Code));
    }
}
