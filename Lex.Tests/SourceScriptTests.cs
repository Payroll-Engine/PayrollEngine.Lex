using Xunit;
// ReSharper disable StringLiteralTypo

namespace PayrollEngine.Lex.Tests;

public class SourceScriptTests
{
    [Theory]
    [InlineData("\u0662\u0660\u0662\u0666", "2026")] // Arabic-Indic
    [InlineData("\u06f1\u06f2\u06f3", "123")] // Persian
    [InlineData("\u0968\u0966\u0968\u096c", "2026")] // Devanagari
    [InlineData("\u09e8\u09e6\u09e8\u09ec", "2026")] // Bengali
    [InlineData("\u0ae8\u0ae6\u0ae8\u0aec", "2026")] // Gujarati
    [InlineData("\u0be8\u0be6\u0be8\u0bec", "2026")] // Tamil
    [InlineData("\u0ce8\u0ce6\u0ce8\u0cec", "2026")] // Kannada
    [InlineData("\uff12\uff10\uff12\uff16", "2026")] // full width
    [InlineData("\u0661\u0662\u066b\u0665\u066a", "12.5%")] // Arabic decimal separator and percent sign
    [InlineData("\u0661\u066c\u0662\u0663\u0664", "1,234")] // Arabic thousands separator
    [InlineData("5\uff05", "5%")]
    [InlineData("Limit 1,234 (Grenze)", "Limit 1,234 (Grenze)")]
    [InlineData("caf\u00e9 \u00bd \u00b2", "caf\u00e9 \u00bd \u00b2")]
    public void Normalize(string text, string expected) =>
        Assert.Equal(expected, SourceScript.Normalize(text));

    [Fact]
    public void Normalize_KeepsLength()
    {
        const string text = "\u0938\u0940\u092e\u093e \u0968,\u096b\u0966,\u0966\u0966\u0966 \u0930\u0941\u092a\u092f\u0947";

        Assert.Equal(text.Length, SourceScript.Normalize(text).Length);
    }

    [Fact]
    public void Normalize_AsciiText_IsSameInstance()
    {
        const string text = "Limit 12,570 for 2026";

        Assert.Same(text, SourceScript.Normalize(text));
    }

    [Fact]
    public void Numbers_Devanagari_LakhGrouping() =>
        Assert.Contains(250000m,
            SourceDocument.Numbers("\u0938\u0940\u092e\u093e \u0968,\u096b\u0966,\u0966\u0966\u0966 \u0930\u0941\u092a\u092f\u0947"));

    [Fact]
    public void Numbers_ArabicPercent_IsPercentage()
    {
        var result = SourceVerifier.Verify(
            new Entry { Key = "Test.2026", Content = "0.125", Unit = "rate", SourceUrls = ["https://example.org/doc"] },
            new SourceDocument { State = SourceState.Readable, Sections = [("Page 1", "Rate \u0661\u0662\u066b\u0665\u066a of wages")] });

        Assert.Equal(1, result.NumbersFound);
    }

    [Fact]
    public void Hits_PositionsOfOriginalText()
    {
        const string text = "Limit \u0661\u066c\u0662\u0663\u0664 in \u0662\u0660\u0662\u0666";

        var hit = Assert.Single(SourceNumbers.Scan(text).Hits(1234m, false));
        var year = Assert.Single(SourceVerifier.YearHits(text));

        Assert.Equal((6, 5), hit);
        Assert.Equal(15, year.Index);
        Assert.Equal(4, year.Length);
    }

    [Fact]
    public void Years_Devanagari() =>
        Assert.Equal([2026], SourceVerifier.Years("\u0935\u0930\u094d\u0937 \u0968\u0966\u0968\u096c"));

    [Fact]
    public void Quote_OtherScript_IsFound()
    {
        var entry = new Entry
        {
            Key = "Test.2026", Content = "12345", SourceQuote = "monthly limit 12,345",
            SourceUrls = ["https://example.org/doc"]
        };
        var document = new SourceDocument
        {
            State = SourceState.Readable,
            Sections = [("Page 1", "The monthly limit \u0661\u0662\u066c\u0663\u0664\u0665 applies.")]
        };

        var result = SourceVerifier.Verify(entry, document);

        Assert.Equal(SourceMatch.Found, result.Match);
        Assert.Equal("monthly limit 12,345", result.Quote);
    }

    [Fact]
    public void NormalizedText_KeepsScript()
    {
        var document = new SourceDocument
        {
            State = SourceState.Readable,
            Sections = [("Page 1", "\u0938\u0940\u092e\u093e \u0968\u0966\u0968\u096c 5")]
        };

        Assert.Contains("\u0968\u0966\u0968\u096c", document.NormalizedText());
    }

    // currency signs -----------------------------------------------------------

    [Theory]
    [InlineData("\u20b92025 per month")]
    [InlineData("Rs. 2025 per month")]
    [InlineData("Rs.2025 per month")]
    [InlineData("R$ 2025 per month")]
    [InlineData("CHF 2025 per month")]
    [InlineData("Fr. 2025 per month")]
    [InlineData("kr 2025 per month")]
    [InlineData("2025 \u20ac per month")]
    [InlineData("2025\u20ac per month")]
    [InlineData("2025 EUR per month")]
    [InlineData("2025 CHF per month")]
    [InlineData("2025 \u20b9 per month")]
    public void Years_AmountWithCurrency_IsNoYear(string text) =>
        Assert.Empty(SourceVerifier.Years(text));

    [Theory]
    [InlineData("in 2025 the rate")]
    [InlineData("2025 Euros per month")]
    [InlineData("CHFX 2025")]
    public void Years_WithoutCurrency_IsYear(string text) =>
        Assert.Equal([2025], SourceVerifier.Years(text));
}
