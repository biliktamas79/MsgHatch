namespace MsgHatch.Domain.DependencyInjection;

/// <summary>
/// The options type of the startup validation of the setup (section 4.7). It holds no setting. Its validator runs the
/// checks with <c>ValidateOnStart</c>, apart from <see cref="MsgHatchOptions"/>, because a check resolves the routers,
/// and a router resolves <see cref="MsgHatchOptions"/>.
/// </summary>
internal sealed class MsgHatchSetupCheck;
