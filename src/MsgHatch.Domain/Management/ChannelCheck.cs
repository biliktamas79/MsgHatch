using MsgHatch.Domain.Identity;

namespace MsgHatch.Domain.Management;

/// <summary>The result of <see cref="IRegistryCommands.CheckChannelAsync"/> (D283).</summary>
/// <param name="Channel">The channel.</param>
/// <param name="ErrorCode">The code of the error of the health check, or null when the check gave no error.</param>
/// <param name="ErrorName">The name of the error of the health check, or null when the check gave no error.</param>
/// <param name="Changed">True when the check added a state row of the channel.</param>
public sealed record ChannelCheck(ChannelId Channel, int? ErrorCode, string? ErrorName, bool Changed)
{
    /// <summary>True when the health check gave no error.</summary>
    public bool Healthy => ErrorCode is null;
}
