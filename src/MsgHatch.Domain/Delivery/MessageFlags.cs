using System.Diagnostics.CodeAnalysis;

namespace MsgHatch.Domain.Delivery;

/// <summary>The flags of a message (D82). The stores keep them as text.</summary>
[Flags]
[SuppressMessage("Naming", "CA1711", Justification = "Section 4.4 of the design fixes the name MessageFlags.")]
public enum MessageFlags
{
    /// <summary>No flag.</summary>
    None = 0,

    /// <summary>A test message. Its data stays apart from the real data, and a test run deletes it.</summary>
    Test = 1,
}
