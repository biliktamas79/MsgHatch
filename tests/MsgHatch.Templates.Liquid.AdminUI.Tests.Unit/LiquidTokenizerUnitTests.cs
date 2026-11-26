namespace MsgHatch.Templates.Liquid.AdminUI.Tests.Unit;

public sealed class LiquidTokenizerUnitTests
{
    public static TheoryData<string, string[]> SpanRows => new()
    {
        { "Hello", ["Text:Hello"] },
        { "{ a } % b }", ["Text:{ a } % b }"] },
        { "a{{ x }}b", ["Text:a", "Delimiter:{{", "Code: x ", "Delimiter:}}", "Text:b"] },
        { "{{- x -}}", ["Delimiter:{{-", "Code: x ", "Delimiter:-}}"] },
        { "{% if x %}", ["Delimiter:{%", "Code: ", "TagName:if", "Code: x ", "Delimiter:%}"] },
        { "{%- endif -%}", ["Delimiter:{%-", "Code: ", "TagName:endif", "Code: ", "Delimiter:-%}"] },
        { "{{ x | upcase }}", ["Delimiter:{{", "Code: x | ", "FilterName:upcase", "Code: ", "Delimiter:}}"] },
        { "{{ 'a}}' }}", ["Delimiter:{{", "Code: ", "Quoted:'a}}'", "Code: ", "Delimiter:}}"] },
        { "{{ \"b\" }}", ["Delimiter:{{", "Code: ", "Quoted:\"b\"", "Code: ", "Delimiter:}}"] },
        { "{{ 42 | plus: 1.5 }}", ["Delimiter:{{", "Code: ", "Number:42", "Code: | ", "FilterName:plus", "Code:: ", "Number:1.5", "Code: ", "Delimiter:}}"] },
        {
            "{% comment %}x{{ y }}{% endcomment %}",
            ["Delimiter:{%", "Code: ", "TagName:comment", "Code: ", "Delimiter:%}", "Comment:x{{ y }}", "Delimiter:{%", "Code: ", "TagName:endcomment", "Code: ", "Delimiter:%}"]
        },
        {
            "{% raw %}{{ x }}{% endraw %}",
            ["Delimiter:{%", "Code: ", "TagName:raw", "Code: ", "Delimiter:%}", "Raw:{{ x }}", "Delimiter:{%", "Code: ", "TagName:endraw", "Code: ", "Delimiter:%}"]
        },
        { "{% comment %}open", ["Delimiter:{%", "Code: ", "TagName:comment", "Code: ", "Delimiter:%}", "Comment:open"] },
        { "a {{ x", ["Text:a ", "Delimiter:{{", "Code: x"] },
        { "{%", ["Delimiter:{%"] },
    };

    public static TheoryData<string, string[]> IssueRows => new()
    {
        { "Hello {{ Name }} {% if x %}{% endif %}", [] },
        { "a\nb {{ x", ["2:3:" + LiquidTokenizer.OpenOutputMessage] },
        { "{% if x", ["1:1:" + LiquidTokenizer.OpenTagMessage] },
        { "ab {%", ["1:4:" + LiquidTokenizer.OpenTagMessage] },
        { "x\n{% liquid echo 1 %}", ["2:1:" + LiquidTokenizer.LiquidTagMessage] },
        { "{%-liquid%}", ["1:1:" + LiquidTokenizer.LiquidTagMessage] },
        { "{% raw %}{% liquid %}{% endraw %}", ["1:10:" + LiquidTokenizer.LiquidTagMessage] },
        { "{% liquid", ["1:1:" + LiquidTokenizer.LiquidTagMessage, "1:1:" + LiquidTokenizer.OpenTagMessage] },
    };

    [Theory]
    [MemberData(nameof(SpanRows))]
    public void Tokenize_Text_GivesASpanForEachKindInTheOrderOfTheText(string text, string[] expected)
    {
        // Act
        var spans = LiquidTokenizer.Tokenize(text);

        // Assert
        Assert.Equal(expected, spans.Select(span => $"{span.Kind}:{text.Substring(span.Start, span.Length)}"));
    }

    [Theory]
    [MemberData(nameof(IssueRows))]
    public void FindIssues_Text_GivesTheLineTheColumnAndTheMessageOfEachIssue(string text, string[] expected)
    {
        // Act
        var issues = LiquidTokenizer.FindIssues(text);

        // Assert
        Assert.Equal(expected, issues.Select(static issue => $"{issue.Line}:{issue.Column}:{issue.Message}"));
    }

    [Fact]
    public void Tokenize_NullText_Throws()
    {
        // Act and assert
        Assert.Throws<ArgumentNullException>(() => LiquidTokenizer.Tokenize(null!));
    }

    [Fact]
    public void FindIssues_NullText_Throws()
    {
        // Act and assert
        Assert.Throws<ArgumentNullException>(() => LiquidTokenizer.FindIssues(null!));
    }
}
