using Xunit;

namespace PayrollEngine.Lex.Tests;

public class SourceDocumentTests
{
    private static readonly string Filler = string.Concat(Enumerable.Repeat("filler text ", 60));

    private static string Page(string body, string updated = "Last updated 3 November 2026") =>
        $"<html><head><script>var t = {DateTime.Now.Ticks};</script></head><body><h2>Rates</h2>{body}" +
        $"<h2>Notes</h2><p>{Filler}</p><footer>{updated}</footer></body></html>";

    private static string Hash(string html) =>
        SourceDocument.FromHtml(html).TextHash();

    [Fact]
    public void TextHash_IgnoresLayoutScriptsAndWhitespace()
    {
        var a = Hash(Page("<p>The rate is 8.5%.</p>"));
        var b = Hash(Page("<div class=\"new\">\n   <p>The rate is   <b>8.5%</b>.</p>\n</div>"));
        Assert.NotNull(a);
        Assert.Equal(a, b);
    }

    [Theory]
    [InlineData("Last updated 4 November 2026")]
    [InlineData("Last updated November 4, 2026")]
    [InlineData("Last updated 2026-11-04T10:15:00Z")]
    [InlineData("Last updated 04.11.2026 10:15")]
    [InlineData("Last updated 4/11/2026 9:05 am")]
    public void TextHash_IgnoresDatesAndTimes(string updated) =>
        Assert.Equal(Hash(Page("<p>The rate is 8.5%.</p>")), Hash(Page("<p>The rate is 8.5%.</p>", updated)));

    [Fact]
    public void TextHash_IgnoresBlockOrder() =>
        Assert.Equal(Hash(Page("<p>Question one 100</p><p>Question two 200</p>")),
            Hash(Page("<p>Question two 200</p><p>Question one 100</p>")));

    [Fact]
    public void TextHash_IgnoresOrderOfNumberedItems() =>
        Assert.Equal(
            Hash(Page("<div><label>1</label> First question</div><p>Answer 100</p><div><label>2</label> Second question</div><p>Answer 200</p>")),
            Hash(Page("<div><label>1</label> Second question</div><p>Answer 200</p><div><label>2</label> First question</div><p>Answer 100</p>")));

    [Fact]
    public void TextHash_DetectsValueMovedToOtherRow() =>
        Assert.NotEqual(
            Hash(Page("<table><tr><td>Band A</td><td>20%</td></tr><tr><td>Band B</td><td>40%</td></tr></table>")),
            Hash(Page("<table><tr><td>Band A</td><td>40%</td></tr><tr><td>Band B</td><td>20%</td></tr></table>")));

    [Fact]
    public void TextHash_IgnoresLinesWithoutNumbers() =>
        Assert.Equal(Hash(Page("<p>The rate is 8.5%.</p><div><a>For developers</a><a>Help</a></div>")),
            Hash(Page("<p>The rate is 8.5%.</p><div><a>Login</a></div>")));

    [Fact]
    public void TextHash_DetectsChangedTextInValueLine() =>
        Assert.NotEqual(Hash(Page("<p>The rate is 8.5% for employers.</p>")),
            Hash(Page("<p>The rate is 8.5% for employees.</p>")));

    [Fact]
    public void TextHash_DetectsChangedValue() =>
        Assert.NotEqual(Hash(Page("<p>The rate is 8.5%.</p>")), Hash(Page("<p>The rate is 9.0%.</p>")));

    [Fact]
    public void TextHash_UnreadableDocument_IsNull() =>
        Assert.Null(SourceDocument.FromHtml("<html>Loading</html>").TextHash());

    [Fact]
    public async Task LoadLocal_MissingFile_IsNoEvidence()
    {
        var document = await SourceDocument.LoadLocal(null);
        var result = SourceVerifier.Verify(new Entry { Key = "Rates.A", Content = "8.5", SourceUrl = "https://example.org/doc" }, document);
        Assert.Equal(SourceMatch.NoEvidence, result.Match);
    }

    [Fact]
    public async Task LoadLocal_ExistingFile_IsReadable()
    {
        using var s = new TestSatellite();
        var path = Path.Combine(s.LexDirectory, "page.html");
        await File.WriteAllTextAsync(path, Page("<p>The rate is 8.5%.</p>"));
        var document = await SourceDocument.LoadLocal(path);
        var result = SourceVerifier.Verify(new Entry { Key = "Rates.A", Content = "0.085", SourceUrl = "https://example.org/doc" }, document);
        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal("Section \"Rates\"", result.Location);
    }
}
