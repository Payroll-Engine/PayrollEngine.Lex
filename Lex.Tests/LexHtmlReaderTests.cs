using Xunit;

namespace PayrollEngine.Lex.Tests;

public class LexHtmlReaderTests
{
    [Fact]
    public void ScriptStyleAndComments_AreDropped()
    {
        var sections = LexHtmlReader.Read(
            "<p>visible 100</p><script>var x = \"<p>4711</p>\";</script><style>p { width: 4712px }</style>" +
            "<!-- hidden 4713 --><noscript>4714</noscript><p>after</p>");
        var text = string.Join("\n", sections.Select(s => s.Text));
        Assert.Contains("visible 100", text);
        Assert.Contains("after", text);
        Assert.DoesNotContain("4711", text);
        Assert.DoesNotContain("4712", text);
        Assert.DoesNotContain("4713", text);
        Assert.DoesNotContain("4714", text);
    }

    [Fact]
    public void Headings_SplitSections()
    {
        var sections = LexHtmlReader.Read("<p>intro</p><h2 class=\"x\">Tax <b>rates</b></h2><p>40%</p><h3>Limits</h3><p>12,570</p>");
        Assert.Equal([LexHtmlReader.StartHeading, "Tax rates", "Limits"], sections.Select(s => s.Heading));
        Assert.Equal("40%", sections[1].Text);
    }

    [Fact]
    public void TableCells_AreSeparated_RowsAreLines()
    {
        var sections = LexHtmlReader.Read("<table><tr><th>Band</th><th>Rate</th></tr><tr><td>12</td><td>345</td></tr></table>");
        var text = Assert.Single(sections).Text;
        Assert.Equal("Band\tRate\n12\t345", text);
        Assert.DoesNotContain(12345m, LexSourceDocument.Numbers(text));
    }

    [Fact]
    public void Entities_AreDecoded()
    {
        var text = Assert.Single(LexHtmlReader.Read("<p>1&nbsp;000 &euro; &amp; employer&#8217;s share</p>")).Text;
        Assert.Equal("1 000 € & employer’s share", text);
        Assert.Contains(1000m, LexSourceDocument.Numbers(text));
    }

    [Fact]
    public void QuotedAttributeWithGreaterThan_IsPartOfTheTag()
    {
        var text = Assert.Single(LexHtmlReader.Read("<a title=\"a > b\" href='x>y'>link</a> text")).Text;
        Assert.Equal("link text", text);
    }

    [Fact]
    public void UnclosedHeading_EndsAtNextHeading()
    {
        var sections = LexHtmlReader.Read("<h2>First<h2>Second</h2><p>body</p>");
        Assert.Equal("Second", sections.Single().Heading);
    }

    [Fact]
    public void UnterminatedTag_IsText()
    {
        var text = Assert.Single(LexHtmlReader.Read("<p>value 5 < 7 and more")).Text;
        Assert.Contains("value 5", text);
    }

    [Fact]
    public void InlineElements_KeepWordsApart()
    {
        var text = Assert.Single(LexHtmlReader.Read("<p><span>rate</span><span>8.5</span></p>")).Text;
        Assert.Equal("rate 8.5", text);
    }
}
