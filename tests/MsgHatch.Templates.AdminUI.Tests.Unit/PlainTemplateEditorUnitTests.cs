using Bunit;
using static MsgHatch.Templates.AdminUI.Tests.Unit.TestTypes.TemplateTestData;

namespace MsgHatch.Templates.AdminUI.Tests.Unit;

public sealed class PlainTemplateEditorUnitTests : BunitContext
{
    [Fact]
    public void Input_NewText_GivesTheTextToTextChanged()
    {
        var texts = new List<string>();
        var cut = Render<PlainTemplateEditor>(p => p.Add(x => x.Lookup, Lookup()).Add(x => x.Text, "a").Add(x => x.TextChanged, texts.Add));

        cut.Find("textarea").Input("ab");

        Assert.Equal(["ab"], texts);
    }

    [Fact]
    public void Render_Disabled_DisablesTheTextArea()
    {
        var cut = Render<PlainTemplateEditor>(p => p.Add(x => x.Lookup, Lookup()).Add(x => x.Disabled, true));

        Assert.True(cut.Find("textarea").HasAttribute("disabled"));
    }

    [Fact]
    public void Render_IssuesWithAndWithNoPosition_ShowsEachIssueUnderTheText()
    {
        var cut = Render<PlainTemplateEditor>(p => p.Add(x => x.Lookup, Lookup())
            .Add(x => x.Issues, [new TemplateIssue("The tag has no end.", 3, 9), new TemplateIssue("The filter is not known.", null, null)]));

        Assert.Equal(["Line 3, column 9: The tag has no end.", "The filter is not known."], cut.FindAll("[data-issue]").Select(static issue => issue.TextContent));
        Assert.Contains("is-invalid", cut.Find("textarea").ClassList);
    }
}
