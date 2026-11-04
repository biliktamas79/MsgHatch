using MsgHatch.Domain.Routing;

namespace MsgHatch.Subscriptions.Tests.Unit.TestTypes;

/// <summary>
/// The settings of the test channel <c>Pager</c>, which has no feature <c>Languages</c>, but settings with a language, so
/// that the manager rule 2.3.5 can fail. The validator of the channel rejects a negative priority (rule 2.3.7).
/// </summary>
/// <param name="Language">The language, or null.</param>
/// <param name="Priority">The priority. A negative value is not valid.</param>
public sealed record PagerSettings(string? Language = null, int Priority = 0) : ChannelSettings, ILanguageSettings;
