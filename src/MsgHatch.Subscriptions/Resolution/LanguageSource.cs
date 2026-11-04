namespace MsgHatch.Subscriptions.Resolution;

/// <summary>Which rule gave the language of a delivery (D92, section 4.17).</summary>
public enum LanguageSource
{
    /// <summary>The language of the channel settings of the applied subscription.</summary>
    ChannelSettings,

    /// <summary>The UI language of the target user.</summary>
    UserUiLanguage,

    /// <summary>The default language of the target company.</summary>
    CompanyDefaultLanguage,

    /// <summary><c>MsgHatchOptions.DefaultLanguage</c>.</summary>
    LibraryDefault,
}
