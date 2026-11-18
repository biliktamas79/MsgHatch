using MsgHatch.Domain.Identity;

namespace MsgHatch.Templates.Management;

/// <summary>
/// One declared part of a message type (D220, D240), with the names of the engines of the registrations that read it,
/// for example <c>placeholder</c> or <c>liquid</c>.
/// </summary>
/// <param name="Type">The message type.</param>
/// <param name="Part">The part and its kind.</param>
/// <param name="Engines">The engine names, each one time, in the order of the registrations.</param>
public sealed record TemplatePartInfo(MessageType Type, TemplatePart Part, IReadOnlyList<string> Engines);
