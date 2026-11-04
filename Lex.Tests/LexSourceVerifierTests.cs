using Xunit;

namespace PayrollEngine.Lex.Tests;

public class LexSourceVerifierTests
{
    private static LexValue Entry(string content) => new()
    {
        Key = "Test.2026",
        Content = content,
        SourceUrl = "https://example.org/doc"
    };

    private static string Page(string body) =>
        "<html><body>" + body + "<h2>Notes</h2><p>" + string.Concat(Enumerable.Repeat("filler text ", 60)) + "</p></body></html>";

    [Fact]
    public void Numbers_NormalizesSeparators()
    {
        var numbers = LexSourceDocument.Numbers("Limit 12,570.50 and 13.518,68 and 7 500 and 0.45");
        Assert.Contains(12570.50m, numbers);
        Assert.Contains(13518.68m, numbers);
        Assert.Contains(7500m, numbers);
        Assert.Contains(0.45m, numbers);
    }

    [Fact]
    public void AllNumbersInOneSection_IsFound_WithLocation()
    {
        var document = LexSourceDocument.FromHtml(Page(
            "<h2>Rates</h2><p>The limit is 12,570 and the rate 40%.</p><h2>Other</h2><p>unrelated 99</p>"));
        var result = LexSourceVerifier.Verify(Entry("{\"Limit\":12570,\"Rate\":0.4}"), document);
        Assert.Equal(LexSourceMatch.Found, result.Match);
        Assert.Contains("Rates", result.Location);
    }

    [Fact]
    public void MissingNumber_IsPartial()
    {
        var document = LexSourceDocument.FromHtml(Page("<h2>Rates</h2><p>The limit is 12,570.</p>"));
        var result = LexSourceVerifier.Verify(Entry("{\"Limit\":12570,\"Other\":4321}"), document);
        Assert.Equal(LexSourceMatch.Partial, result.Match);
        Assert.Contains("4321", result.Missing);
    }

    [Fact]
    public void NoMatchingNumber_IsNotFound()
    {
        var document = LexSourceDocument.FromHtml(Page("<h2>Rates</h2><p>Nothing relevant here.</p>"));
        var result = LexSourceVerifier.Verify(Entry("{\"Limit\":12570}"), document);
        Assert.Equal(LexSourceMatch.NotFound, result.Match);
    }

    [Fact]
    public void ShortText_IsUnreadable()
    {
        var document = LexSourceDocument.FromHtml("<html>Loading</html>");
        var result = LexSourceVerifier.Verify(Entry("{\"Limit\":12570}"), document);
        Assert.Equal(LexSourceMatch.Unreadable, result.Match);
    }
}
