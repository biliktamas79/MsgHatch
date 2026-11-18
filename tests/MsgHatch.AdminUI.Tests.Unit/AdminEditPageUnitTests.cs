using Bunit;

namespace MsgHatch.AdminUI.Tests.Unit;

public sealed class AdminEditPageUnitTests : BunitContext
{
    [Fact]
    public void Render_AllParts_PutsTheHeaderTheScrollingBodyAndTheActionsUnderIt()
    {
        var cut = Render<AdminEditPage>(p => p
            .Add(x => x.Header, builder => builder.AddMarkupContent(0, "<h1>Template</h1>"))
            .Add(x => x.Actions, builder => builder.AddMarkupContent(0, "<button data-save>Save</button>"))
            .AddChildContent("<textarea data-text></textarea>"));

        var body = cut.Find("[data-edit-body]");
        var actions = cut.Find("[data-edit-actions]");
        Assert.Equal(
            ("Template", true, true, false, true, true),
            (cut.Find("[data-edit-header] h1").TextContent, body.QuerySelector("[data-text]") is not null, actions.QuerySelector("[data-save]") is not null,
                body.Contains(actions), body.NextElementSibling?.HasAttribute("data-edit-actions") == true,
                cut.Find("[data-edit-page]").ClassList.Contains("flex-column") && body.ClassList.Contains("flex-grow-1")));
    }

    [Fact]
    public void Render_NoHeaderAndNoActions_ShowsOnlyTheBody()
    {
        var cut = Render<AdminEditPage>(p => p.AddChildContent("<p>Form</p>"));

        Assert.Equal((0, 0, "Form"), (cut.FindAll("[data-edit-header]").Count, cut.FindAll("[data-edit-actions]").Count, cut.Find("[data-edit-body]").TextContent));
    }
}
