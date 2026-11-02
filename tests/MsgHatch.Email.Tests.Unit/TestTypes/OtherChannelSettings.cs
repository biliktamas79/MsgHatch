using MsgHatch.Domain.Routing;

namespace MsgHatch.Email.Tests.Unit.TestTypes;

/// <summary>The settings of a channel that is not the email channel.</summary>
public sealed record OtherChannelSettings : ChannelSettings;
