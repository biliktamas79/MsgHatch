namespace MsgHatch.Domain.Delivery;

/// <summary>The receipt of a transport for a sent message.</summary>
/// <param name="ProviderMessageId">The message id of the provider, or null when the provider gives none.</param>
public readonly record struct PublishReceipt(string? ProviderMessageId);
