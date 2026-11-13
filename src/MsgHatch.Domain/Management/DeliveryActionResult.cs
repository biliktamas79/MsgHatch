namespace MsgHatch.Domain.Management;

/// <summary>The result of a retry or a cancel of the messages of a query (section 4.19).</summary>
/// <param name="Matched">The number of messages that the query found.</param>
/// <param name="Changed">The number of messages that the action changed. The rules of the action skip the others.</param>
public readonly record struct DeliveryActionResult(int Matched, int Changed);
