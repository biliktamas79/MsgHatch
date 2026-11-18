namespace MsgHatch.Templates.AdminUI.Tests.Unit;

public sealed class HtmlTokenizerUnitTests
{
    public static TheoryData<string, string[]> SpanRows => new()
    {
        { "Hello", ["Text:Hello"] },
        { "<p>Hi</p>", ["Markup:<", "TagName:p", "Markup:>", "Text:Hi", "Markup:</", "TagName:p", "Markup:>"] },
        {
            "<a href=\"{{Link}}\" target='_blank'>",
            ["Markup:<", "TagName:a", "Text: ", "AttributeName:href", "Markup:=", "AttributeValue:\"{{Link}}\"", "Text: ", "AttributeName:target", "Markup:=", "AttributeValue:'_blank'", "Markup:>"]
        },
        { "<td width=100 nowrap>", ["Markup:<", "TagName:td", "Text: ", "AttributeName:width", "Markup:=", "AttributeValue:100", "Text: ", "AttributeName:nowrap", "Markup:>"] },
        { "<br/>", ["Markup:<", "TagName:br", "Markup:/>"] },
        { "<img src=\"a\" />", ["Markup:<", "TagName:img", "Text: ", "AttributeName:src", "Markup:=", "AttributeValue:\"a\"", "Text: ", "Markup:/>"] },
        { "<!DOCTYPE html>", ["Markup:<!", "TagName:DOCTYPE", "Text: ", "AttributeName:html", "Markup:>"] },
        { "a<!-- <b> -->c", ["Text:a", "Comment:<!-- <b> -->", "Text:c"] },
        { "a &amp; b&#160;c&#x2014;", ["Text:a ", "Entity:&amp;", "Text: b", "Entity:&#160;", "Text:c", "Entity:&#x2014;"] },
        { "a & b &x y;", ["Text:a & b &x y;"] },
        { "a < b <3", ["Text:a < b <3"] },
        { "<!-- open", ["Comment:<!-- open"] },
        { "<p class=\"open", ["Markup:<", "TagName:p", "Text: ", "AttributeName:class", "Markup:=", "AttributeValue:\"open"] },
        { "<div\n  id=x>", ["Markup:<", "TagName:div", "Text:\n  ", "AttributeName:id", "Markup:=", "AttributeValue:x", "Markup:>"] },
        { "<p", ["Markup:<", "TagName:p"] },
        { "", [] },
    };

    public static TheoryData<HtmlSpanKind, string> CssClassRows => new()
    {
        { HtmlSpanKind.Text, "html-text" },
        { HtmlSpanKind.Markup, "html-markup" },
        { HtmlSpanKind.TagName, "html-tagname" },
        { HtmlSpanKind.AttributeName, "html-attribute" },
        { HtmlSpanKind.AttributeValue, "html-value" },
        { HtmlSpanKind.Comment, "html-comment" },
        { HtmlSpanKind.Entity, "html-entity" },
    };

    [Theory]
    [MemberData(nameof(SpanRows))]
    public void Tokenize_Text_GivesASpanForEachKindInTheOrderOfTheText(string text, string[] expected)
    {
        var spans = HtmlTokenizer.Tokenize(text);

        Assert.Equal(expected, spans.Select(span => $"{span.Kind}:{text.Substring(span.Start, span.Length)}"));
    }

    [Theory]
    [MemberData(nameof(CssClassRows))]
    public void CssClassOf_Kind_GivesTheClassOfTheHighlight(HtmlSpanKind kind, string expected)
    {
        Assert.Equal(expected, HtmlTokenizer.CssClassOf(kind));
    }

    [Fact]
    public void Tokenize_NullText_Throws()
    {
        Assert.Throws<ArgumentNullException>(() => HtmlTokenizer.Tokenize(null!));
    }
}
