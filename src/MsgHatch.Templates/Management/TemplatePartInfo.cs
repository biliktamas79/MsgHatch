using MsgHatch.Domain.Identity;

namespace MsgHatch.Templates.Management;

/// <summary>
/// One declared part of a message type (D220, D240), with the names of the engines of the registrations that read it,
/// for example <c>placeholder</c> or <c>liquid</c>, and whether a registration that reads it has a sample payload (D254).
/// </summary>
/// <param name="Type">The message type.</param>
/// <param name="Part">The part and its kind.</param>
/// <param name="Engines">The engine names, each one time, in the order of the registrations.</param>
/// <param name="HasSample">True when a registration that reads the part has a sample payload, so a preview of the part shows a text.</param>
public sealed record TemplatePartInfo(MessageType Type, TemplatePart Part, IReadOnlyList<string> Engines, bool HasSample);
