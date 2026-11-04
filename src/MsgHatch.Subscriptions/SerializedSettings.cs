namespace MsgHatch.Subscriptions;

/// <summary>Channel settings as JSON, with the content type and the version that a store keeps beside them (D149).</summary>
/// <param name="ContentType">The content type <c>Settings:{Channel}</c>.</param>
/// <param name="Version">The version of the content type.</param>
/// <param name="Json">The JSON of the settings.</param>
public sealed record SerializedSettings(string ContentType, int Version, string Json);
