using System.Diagnostics.CodeAnalysis;

namespace MsgHatch.Templates.AdminUI;

/// <summary>The edit page of one part of a template (D239, D240).</summary>
[UnconditionalSuppressMessage(
    "Trimming",
    "IL2111",
    Justification = "The DynamicComponent of the editor gets only the plain editor, which AddMsgHatchTemplatesAdminUI keeps, or a type of AddTemplateEditor, whose type parameter keeps all members.")]
public partial class TemplateEditPage;
