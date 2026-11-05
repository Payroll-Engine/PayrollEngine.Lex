using Xunit;

namespace PayrollEngine.Lex.Tests;

public class NumberSearchTests
{
    private static SourceResult Verify(string content, string text) =>
        SourceVerifier.Verify(new Entry { Key = "Test.2026", Content = content, SourceUrls = ["https://example.org/doc"] },
            new SourceDocument { State = SourceState.Readable, Sections = [("Page 1", text)] });

    [Theory]
    [InlineData("Cap 20,00,000", 2000000)]
    [InlineData("Rebate 1,50,000.", 150000)]
    [InlineData("Limit 1,50,000, then", 150000)]
    [InlineData("Tier 1,00,00,000", 10000000)]
    [InlineData("Amount 1,50,000.50", 150000.50)]
    public void Numbers_LakhGrouping_IsOneNumber(string text, double expected) =>
        Assert.Contains((decimal)expected, SourceDocument.Numbers(text));

    [Theory]
    [InlineData("Cap 20,00,000", new[] { 2000, 20, 2000.000 })]
    [InlineData("Rebate 1,50,000", new[] { 150, 1.5, 50, 15000 })]
    [InlineData("Amount 1,50,000.50", new[] { 15000050, 150.0 })]
    public void Numbers_LakhGrouping_HasNoOtherReading(string text, double[] unexpected)
    {
        var numbers = SourceDocument.Numbers(text);
        foreach (var value in unexpected)
        {
            Assert.DoesNotContain((decimal)value, numbers);
        }
    }

    [Theory]
    [InlineData("Limit 150,000", new[] { 150000, 150.0 })]
    [InlineData("Limit 1,234,567", new[] { 1234567.0 })]
    [InlineData("Limit 12,345", new[] { 12345, 12.345 })]
    [InlineData("Rate 12,50", new[] { 12.5, 1250 })]
    [InlineData("Values 10, 20, 300", new[] { 10, 20, 300.0 })]
    [InlineData("Limit 13.518,68", new[] { 13518.68 })]
    public void Numbers_OtherGroupings_AreUnchanged(string text, double[] expected)
    {
        var numbers = SourceDocument.Numbers(text);
        foreach (var value in expected)
        {
            Assert.Contains((decimal)value, numbers);
        }
    }

    [Theory]
    [InlineData("WH = 263 + [BASE x 0.0475]", 0.0475, 0.047)]
    [InlineData("Rate 0.01675 of wages", 0.01675, 0.016)]
    [InlineData("Limit 1,2345", 1.2345, 1.234)]
    public void Numbers_FourDecimals_AreNotGrouped(string text, double expected, double unexpected)
    {
        var numbers = SourceDocument.Numbers(text);
        Assert.Contains((decimal)expected, numbers);
        Assert.DoesNotContain((decimal)unexpected, numbers);
    }

    [Fact]
    public void Numbers_List_IsNotOneNumber() =>
        Assert.DoesNotContain(1020300m, SourceDocument.Numbers("Values 10, 20, 300"));

    [Theory]
    [InlineData("150", "Cap 1,50,000")]
    [InlineData("1.5", "Cap 1,50,000")]
    [InlineData("2000", "Cap 20,00,000")]
    [InlineData("20", "Cap 20,00,000")]
    public void Verify_PartOfLakhNumber_IsNotFound(string content, string text) =>
        Assert.Equal(SourceMatch.NotFound, Verify(content, text).Match);

    [Theory]
    [InlineData("2050000", "Cap 20,50,000")]
    [InlineData("{ \"threshold\": 1200000, \"rebate\": 60000 }", "Rebate up to 60,000 for income up to 12,00,000")]
    public void Verify_LakhNumber_IsFound(string content, string text) =>
        Assert.Equal(SourceMatch.Found, Verify(content, text).Match);

    [Theory]
    [InlineData(999999, true)]
    [InlineData(99999999, true)]
    [InlineData(999999999, true)]
    [InlineData(999999.99, true)]
    [InlineData(9999999999.0, true)]
    [InlineData(99999, false)]
    [InlineData(999998, false)]
    [InlineData(1999999, false)]
    [InlineData(999999.5, false)]
    [InlineData(0.999999, false)]
    public void IsPlaceholder(double value, bool expected) =>
        Assert.Equal(expected, SourceVerifier.IsPlaceholder((decimal)value));

    [Fact]
    public void ContentNumbers_SkipsPlaceholders() =>
        Assert.Equal([250000m, 500000m],
            SourceVerifier.ContentNumbers("[{\"from\":0,\"to\":250000},{\"from\":250000,\"to\":500000},{\"from\":500000,\"to\":\"999999.99\"},{\"to\":9999999999.0}]"));

    [Fact]
    public void Verify_SlabWithPlaceholder_IsFound() =>
        Assert.Equal(SourceMatch.Found,
            Verify("[{\"from\":0,\"to\":25000,\"tax\":0},{\"from\":25000,\"to\":99999999,\"tax\":200}]", "Up to 25,000 nil, above 25,000 a tax of 200").Match);

    [Fact]
    public void Verify_OnlyPlaceholder_HasNoNumbers() =>
        Assert.Equal(SourceMatch.NoNumbers, Verify("99999999", "Limit 99999999").Match);

    [Theory]
    [InlineData("wages that exceed $1 million", 1000000)]
    [InlineData("a cap of 1.5 million", 1500000)]
    [InlineData("above 2 billion", 2000000000)]
    [InlineData("rebate of 2.5 lakh", 250000)]
    [InlineData("rebate of 3 lakhs", 300000)]
    // ReSharper disable once StringLiteralTypo
    [InlineData("turnover of 1 crore", 10000000)]
    [InlineData("deduction up to 2,50,000", 250000)]
    [InlineData("at one-half the regular rate", 0.5)]
    [InlineData("at one half the regular rate", 0.5)]
    [InlineData("at ½ the regular rate", 0.5)]
    [InlineData("one and one-half times the regular rate", 1.5)]
    [InlineData("one and a half times the regular rate", 1.5)]
    [InlineData("paid at time and a half", 1.5)]
    [InlineData("paid at time and one-half", 1.5)]
    [InlineData("paid at 1½ times", 1.5)]
    [InlineData("Sunday work is double time", 2)]
    public void Numbers_InWords(string text, double expected) =>
        Assert.Contains((decimal)expected, SourceDocument.Numbers(text));

    [Theory]
    [InlineData("a million reasons", 1000000)]
    [InlineData("billions of records", 1000000000)]
    [InlineData("one and one-half times the regular rate", 0.5)]
    [InlineData("1,000 million", 1000000)]
    public void Numbers_InWords_NoFalseMatch(string text, double unexpected) =>
        Assert.DoesNotContain((decimal)unexpected, SourceDocument.Numbers(text));

    [Fact]
    public void Verify_NumberInWords_IsAmbiguous()
    {
        var result = Verify("1000000", "37% on supplemental wages that exceed $1 million during the year");

        Assert.Equal(SourceMatch.Ambiguous, result.Match);
        Assert.Equal("number in words", result.Ambiguity);
    }

    [Fact]
    public void Verify_FactorInWords_IsAmbiguous() =>
        Assert.Equal(SourceMatch.Ambiguous,
            Verify("1.5", "overtime at a rate not less than one and one-half times the regular rate").Match);

    [Fact]
    public void Verify_NumberInWordsWithinTable_IsFound() =>
        Assert.Equal(SourceMatch.Found,
            Verify("{ \"threshold\": 1000000, \"rate\": 37, \"limit\": 12345 }", "Limit 12,345; 37 percent above $1 million").Match);
}
