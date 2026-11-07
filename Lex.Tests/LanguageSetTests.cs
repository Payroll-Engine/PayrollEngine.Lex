using Xunit;
// ReSharper disable StringLiteralTypo

namespace PayrollEngine.Lex.Tests;

public class LanguageSetTests
{
    private const string Danish = """
        {
          "language": "da",
          "name": "Danish",
          "percentWords": ["procent"],
          "enumerationWords": ["og", "eller"],
          "rangeWords": ["til"],
          "validityBefore": ["med virkning fra", "fra og med"],
          "months": [["januar"], ["februar"], ["marts"], ["april"], ["maj"], ["juni"], ["juli"], ["august"],
            ["september"], ["oktober"], ["november"], ["december"]]
        }
        """;

    /// <summary>Temporary folder of additional language files</summary>
    private sealed class LanguageFolder : IDisposable
    {
        internal string Path { get; } =
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lexlang-" + Guid.NewGuid().ToString("N"));

        internal LanguageFolder(params (string Name, string Json)[] files)
        {
            Directory.CreateDirectory(Path);
            foreach (var (name, json) in files)
            {
                File.WriteAllText(System.IO.Path.Combine(Path, name), json);
            }
        }

        internal LanguageSet Get(params string[] languages) => LanguageSet.Get(languages, Path);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }

    // package -----------------------------------------------------------------

    [Fact]
    public void Available_PackageLanguages() =>
        Assert.Equal(["de", "en", "es", "fr", "it", "nl", "pt"], LanguageSet.Available());

    [Fact]
    public void Get_SortedWithoutDuplicates_IsCached()
    {
        var set = LanguageSet.Get(["en", "de", "en"]);
        Assert.Equal(["de", "en"], set.Languages);
        Assert.Same(set, LanguageSet.Get(["de", "en"]));
        Assert.Matches("^de,en [0-9a-f]{8}$", set.Label);
    }

    [Fact]
    public void Get_WithoutLanguages_IsAll()
    {
        Assert.Same(LanguageSet.All, LanguageSet.Get(null));
        Assert.Same(LanguageSet.All, LanguageSet.Get([]));
        Assert.Equal(LanguageSet.Available(), LanguageSet.All.Languages);
    }

    [Fact]
    public void Get_UnknownLanguage_Throws()
    {
        var exception = Assert.Throws<ArgumentException>(() => LanguageSet.Get(["en", "xx"]));
        Assert.Contains("xx", exception.Message);
        Assert.Contains("available: de, en", exception.Message);
    }

    [Fact]
    public void Hash_DependsOnLanguagesAndWords()
    {
        Assert.NotEqual(LanguageSet.Get(["de"]).Hash, LanguageSet.Get(["en"]).Hash);
        using var folder = new LanguageFolder(("en.json", """{ "language": "en", "percentWords": ["percent"] }"""));
        Assert.NotEqual(LanguageSet.Get(["en"]).Hash, folder.Get("en").Hash);
    }

    // selection -----------------------------------------------------------------

    [Fact]
    public void Selection_OnlyItsWordsApply()
    {
        var english = LanguageSet.Get(["en"]);
        var german = LanguageSet.Get(["de"]);
        Assert.Single(SourceVerifier.YearHits("Werte 2024 und 2025", german));
        Assert.Equal(2, SourceVerifier.YearHits("Werte 2024 und 2025", english).Count);
        Assert.True(SourceVerifier.YearHits("gültig ab 1. Januar 2025", german).Single().Validity);
        Assert.False(SourceVerifier.YearHits("gültig ab 1. Januar 2025", english).Single().Validity);
        Assert.Contains(5m, SourceNumbers.Scan("5 Prozent", german).Percents);
        Assert.DoesNotContain(5m, SourceNumbers.Scan("5 Prozent", english).Percents);
    }

    [Fact]
    public void Selection_CitationOfOtherLanguage_IsYear()
    {
        Assert.Empty(SourceVerifier.Years("BGBl. I 2024 Nr. 12", LanguageSet.Get(["de"])));
        Assert.Equal([2024], SourceVerifier.Years("BGBl. I 2024 Nr. 12", LanguageSet.Get(["fr"])));
    }

    // additional folder -----------------------------------------------------------

    [Fact]
    public void AdditionalFolder_AddsLanguage()
    {
        using var folder = new LanguageFolder(("da.json", Danish));
        var danish = folder.Get("da");
        Assert.Equal(["da"], danish.Languages);
        Assert.Single(SourceVerifier.YearHits("satser 2024 og 2025", danish));
        Assert.True(SourceVerifier.YearHits("med virkning fra 1. januar 2025", danish).Single().Validity);
        Assert.Contains(4.5m, SourceNumbers.Scan("4,5 procent", danish).Percents);
        Assert.Contains("da", folder.Get().Languages);
        Assert.Contains("en", folder.Get().Languages);
        Assert.Contains("da", LanguageSet.Available(folder.Path));
        Assert.DoesNotContain("da", LanguageSet.Available());
    }

    [Fact]
    public void AdditionalFolder_ReplacesPackageLanguage()
    {
        using var folder = new LanguageFolder(("en.json", """{ "language": "en", "percentWords": ["percent"] }"""));
        var english = folder.Get("en");
        Assert.Equal(2, SourceVerifier.YearHits("rates 2024 and 2025", english).Count);
        Assert.Single(SourceVerifier.YearHits("rates 2024 and 2025", LanguageSet.Get(["en"])));
    }

    [Theory]
    [InlineData("da.json", """{ "language": "de" }""", "differs from the file name")]
    [InlineData("da.json", """{ "language": "da", "percentWord": ["procent"] }""", "percentWord")]
    [InlineData("da.json", """{ "language": "da", "rangeWords": [""] }""", "empty word")]
    [InlineData("da.json", """{ "language": "da", "scaleWords": [{ "word": "mio.", "factor": 0.5 }] }""", "below 1")]
    [InlineData("da.json", """{ "language": "da", "months": [["januar"], ["februar"]] }""", "months has 2 entries")]
    [InlineData("da.json", """{ "language": "da", "months": [[], [], [], [], [], [], [], [], [], [], [], []] }""",
        "a month without name")]
    [InlineData("da.json", """
        { "language": "da", "months": [["januar"], ["jan"], ["marts"], ["april"], ["maj"], ["juni"], ["juli"],
          ["august"], ["september"], ["oktober"], ["november"], ["december", "jan"]] }
        """, "in several months")]
    [InlineData("danish.json", """{ "language": "da" }""", "language code")]
    [InlineData("da.json", "{ ", "Invalid language file")]
    public void AdditionalFolder_InvalidFile_Throws(string name, string json, string message)
    {
        using var folder = new LanguageFolder((name, json));
        var exception = Assert.Throws<InvalidOperationException>(() => folder.Get());
        Assert.Contains(message, exception.Message);
    }

    [Fact]
    public void MissingFolder_Throws()
    {
        var missing = Path.Combine(Path.GetTempPath(), "lexlang-missing-" + Guid.NewGuid().ToString("N"));
        Assert.Throws<InvalidOperationException>(() => LanguageSet.Get(null, missing, null));
        Assert.Throws<InvalidOperationException>(() => LanguageSet.Get(null, missing));
    }

    // manifest and verification -------------------------------------------------

    private static Manifest Manifest(List<string> languages) => new()
    {
        Authority = "XX",
        Regulation = "Data.Test",
        ValidFrom = "2026-01-01",
        Languages = languages,
        Values = [new Entry { Key = "Rates.A", Content = "0.081", SourceUrls = ["https://example.org/rates"] }]
    };

    [Fact]
    public async Task VerifyManifest_UsesManifestLanguages()
    {
        var results = await SourceVerifier.VerifyManifest(Manifest(["de"]), Path.GetTempPath());
        Assert.Equal(LanguageSet.Get(["de"]).Label, results.Single().Languages);
        results = await SourceVerifier.VerifyManifest(Manifest(null), Path.GetTempPath());
        Assert.Equal(LanguageSet.All.Label, results.Single().Languages);
    }

    [Fact]
    public async Task VerifyManifest_UnknownLanguage_Throws() =>
        await Assert.ThrowsAsync<ArgumentException>(() => SourceVerifier.VerifyManifest(Manifest(["xx"]), Path.GetTempPath()));

    [Fact]
    public async Task VerifyManifest_LanguageOfAdditionalFolder()
    {
        using var folder = new LanguageFolder(("da.json", Danish));
        await Assert.ThrowsAsync<ArgumentException>(() => SourceVerifier.VerifyManifest(Manifest(["da"]), Path.GetTempPath()));
        var results = await SourceVerifier.VerifyManifest(Manifest(["da"]), Path.GetTempPath(), languageDirectory: folder.Path);
        Assert.Equal(folder.Get("da").Label, results.Single().Languages);
    }

    [Fact]
    public void Verify_ResultNamesLanguages()
    {
        var entry = new Entry { Key = "Rates.A", Content = "0.081", SourceUrls = ["https://example.org/rates"] };
        var document = SourceDocument.FromText("Rate 8.1% for 2026");
        Assert.Equal(LanguageSet.All.Label, SourceVerifier.Verify(entry, document).Languages);
        Assert.Equal(LanguageSet.Get(["en"]).Label,
            SourceVerifier.Verify(entry, document, languages: LanguageSet.Get(["en"])).Languages);
    }

    [Fact]
    public void AutoVerification_MethodNamesLanguages()
    {
        var label = LanguageSet.Get(["de", "en"]).Label;
        var verification = SourceVerifier.AutoVerification(new DateOnly(2026, 11, 7), "rates.html", languages: label);
        Assert.Equal($"{SourceVerifier.Method}; languages {label}", verification.Method);
        Assert.Equal(SourceVerifier.Method, SourceVerifier.AutoVerification(new DateOnly(2026, 11, 7), "rates.html").Method);
    }

    [Theory]
    [InlineData(new[] { "de", "fr" }, null)]
    [InlineData(new[] { "DE" }, "Invalid language code")]
    [InlineData(new[] { "de", "de" }, "Duplicate language")]
    [InlineData(new[] { "xx" }, null)]
    public void Validator_Languages(string[] languages, string error)
    {
        var errors = Validator.Validate(Manifest([.. languages]), Path.GetTempPath());
        if (error == null)
        {
            Assert.DoesNotContain(errors, x => x.Contains("anguage"));
        }
        else
        {
            Assert.Contains(errors, x => x.Contains(error));
        }
    }
}
