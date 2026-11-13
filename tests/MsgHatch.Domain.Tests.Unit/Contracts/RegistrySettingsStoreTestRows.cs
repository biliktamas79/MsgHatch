using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Tests.Unit.Contracts;

/// <summary>The rows of the data-driven tests of <see cref="RegistrySettingsStoreTestsBase{TStore}"/>. A generic class has no static members (CA1000).</summary>
public static class RegistrySettingsStoreTestRows
{
    /// <summary>The revision of a new entry, and the error code or null. A missing entry has the revision 0.</summary>
    public static TheoryData<int, int?> NewEntryRevisionRows => new()
    {
        { 0, 409 },
        { 1, null },
        { 2, 409 },
    };

    /// <summary>The revision of an entry over a stored entry with the revision 1, and the error code or null.</summary>
    public static TheoryData<int, int?> UpdateRevisionRows => new()
    {
        { 1, 409 },
        { 2, null },
        { 3, 409 },
    };

    public static TheoryData<string?, string?> TextRows => new()
    {
        { null, null },
        { "Értesítő ő ű", "Quote \" backslash \\ less < and & ő" },
    };

    public static TheoryData<string, ChannelId[]> DisabledChannelRows => new()
    {
        { "none", [] },
        { "one", [new ChannelId("SignalR")] },
        { "two, not in the ordinal order", [new ChannelId("SignalR"), new ChannelId("Email")] },
    };
}
