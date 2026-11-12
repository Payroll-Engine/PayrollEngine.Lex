using System.Text.Json;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class ManifestEditorSourceTests
{
    private const string Json = """
        {
          "authority": "XX",
          "regulation": "Data.Tax",
          "validFrom": "2026-01-01",
          "sources": [
            { "url": "https://example.org/act", "description": "Example Act", "extra": 1 }
          ],
          "values": [
            { "key": "Rates.A", "content": "8.5" }
          ]
        }
        """;

    private static JsonElement[] Sources(ManifestEditor editor)
    {
        using var document = JsonDocument.Parse(editor.ToJson());
        return document.RootElement.GetProperty("sources").EnumerateArray().Select(x => x.Clone()).ToArray();
    }

    private static string[] RootProperties(ManifestEditor editor)
    {
        using var document = JsonDocument.Parse(editor.ToJson());
        return document.RootElement.EnumerateObject().Select(x => x.Name).ToArray();
    }

    [Fact]
    public void AddSource_AppendsInSchemaOrder()
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.True(editor.AddSource(new Source
        {
            Url = " https://example.org/guide ",
            Description = "Guide",
            LegalBasis = "Example Act s. 3",
            Review = Source.ReviewFourEyes,
            Official = false
        }));

        var added = Sources(editor)[1];
        Assert.Equal(["url", "description", "legalBasis", "review", "official"],
            added.EnumerateObject().Select(x => x.Name).ToArray());
        Assert.Equal("https://example.org/guide", added.GetProperty("url").GetString());
        Assert.Equal("FourEyes", added.GetProperty("review").GetString());
        Assert.False(added.GetProperty("official").GetBoolean());
        Assert.Equal(1, editor.Changes);
        Assert.True(editor.HasSource("https://example.org/guide"));
    }

    [Fact]
    public void AddSource_UrlOnly_WritesNoOptionalProperty()
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.True(editor.AddSource(new Source { Url = "https://example.org/other" }));

        Assert.Equal(["url"], Sources(editor)[1].EnumerateObject().Select(x => x.Name).ToArray());
    }

    [Fact]
    public void AddSource_Existing_NoDuplicateNoChange()
    {
        var editor = ManifestEditor.Parse(Json);
        var before = editor.ToJson();

        Assert.False(editor.AddSource(new Source { Url = "https://example.org/act", Description = "Other", Official = false }));

        Assert.Equal(0, editor.Changes);
        Assert.Equal(before, editor.ToJson());
        Assert.Single(Sources(editor));
    }

    [Fact]
    public void AddSource_KeepsUnknownPropertiesAndOrder()
    {
        var editor = ManifestEditor.Parse(Json);

        editor.AddSource(new Source { Url = "https://example.org/other" });

        Assert.Equal(["authority", "regulation", "validFrom", "sources", "values"], RootProperties(editor));
        Assert.Equal(1, Sources(editor)[0].GetProperty("extra").GetInt32());
    }

    [Fact]
    public void AddSource_WithoutSourcesArray_CreatesItBeforeValues()
    {
        var editor = ManifestEditor.Parse("""
            { "authority": "XX", "regulation": "Data.Tax", "validFrom": "2026-01-01", "provider": "Authority",
              "values": [], "files": [] }
            """);

        Assert.True(editor.AddSource(new Source { Url = "https://example.org/act" }));

        Assert.Equal(["authority", "regulation", "validFrom", "provider", "sources", "values", "files"], RootProperties(editor));
        Assert.Single(Sources(editor));
    }

    [Fact]
    public void AddSource_RoundTrip_IsValidForEntryChecks()
    {
        var editor = ManifestEditor.Parse(Json);
        editor.AddSource(new Source { Url = "https://example.org/guide", Review = Source.ReviewFourEyes });

        var reparsed = ManifestEditor.Parse(editor.ToJson());

        Assert.True(reparsed.HasSource("https://example.org/guide"));
        Assert.True(reparsed.HasSource("https://example.org/act"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("example.org/relative")]
    public void AddSource_InvalidUrl_Throws(string url)
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.Throws<ArgumentException>(() => editor.AddSource(new Source { Url = url }));
        Assert.Equal(0, editor.Changes);
    }

    [Fact]
    public void AddSource_UnknownReview_Throws()
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.Throws<ArgumentException>(() =>
            editor.AddSource(new Source { Url = "https://example.org/other", Review = "Triple" }));
        Assert.Equal(0, editor.Changes);
    }

    [Fact]
    public void AddSource_FourEyes_AppliesToEntrySourceReview()
    {
        var editor = ManifestEditor.Parse("""
            { "authority": "XX", "regulation": "Data.Tax", "validFrom": "2026-01-01",
              "values": [ { "key": "Rates.A", "content": "8.5", "sourceUrls": [ "https://example.org/guide" ] } ] }
            """);

        editor.AddSource(new Source { Url = "https://example.org/guide", Review = Source.ReviewFourEyes });

        Assert.Equal(Source.ReviewFourEyes, editor.SourceReview("Rates.A"));
    }
}
