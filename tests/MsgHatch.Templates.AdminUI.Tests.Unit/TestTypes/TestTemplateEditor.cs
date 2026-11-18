using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace MsgHatch.Templates.AdminUI.Tests.Unit.TestTypes;

public sealed class TestTemplateEditor : TemplateEditorBase
{
    [Inject]
    public TestEditorSettings Settings { get; set; } = default!;

    public override ValueTask<bool> CanSaveAsync(CancellationToken ct) => ValueTask.FromResult(Settings.AllowSave);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        builder.OpenElement(0, "div");
        builder.AddAttribute(1, "data-editor", "test");
        builder.AddContent(2, Text);
        builder.CloseElement();
    }
}

public sealed class TestEditorSettings
{
    public bool AllowSave { get; set; } = true;
}
