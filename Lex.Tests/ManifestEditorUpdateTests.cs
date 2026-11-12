using System.Text.Json;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class ManifestEditorUpdateTests
{
    // description before legalReference and a comment: order and unknown properties must survive
    private const string Json = """
        {
          "authority": "XX",
          "sources": [ { "url": "https://example.org/doc", "description": "Doc" } ],
          "values": [
            { "key": "Rates.A", "content": "8.5", "unit": "rate", "sourceUrls": [ "https://example.org/doc" ],
              "sourceLocation": "p. 1", "legalReference": "Test Act s. 1", "note": "keep me",
              "verification": { "mode": "manual", "verifiedOn": "2026-01-10", "verifiedBy": "Jane Doe", "evidence": "doc.pdf" } },
            { "key": "Rates.B", "content": "10", "sourceUrls": [ "https://example.org/doc" ], "sourceLocation": "p. 2",
              "sourceQuote": "rate of 10", "referenceYear": 2025, "legalReference": "Test Act s. 2",
              "description": "Rate B", "documentRef": "doc.pdf" },
            { "key": "Rates.C", "contentFile": "rates.csv", "sourceUrls": [ "https://example.org/doc" ] }
          ],
          "files": [ { "fileName": "doc.pdf", "description": "Doc" } ]
        }
        """;

    private static string[] Properties(ManifestEditor editor, string key)
    {
        using var document = JsonDocument.Parse(editor.ToJson());
        return document.RootElement.GetProperty("values").EnumerateArray()
            .Single(x => x.GetProperty("key").GetString() == key)
            .EnumerateObject().Select(x => x.Name).ToArray();
    }

    private static JsonElement Entry(ManifestEditor editor, string key)
    {
        using var document = JsonDocument.Parse(editor.ToJson());
        return document.RootElement.GetProperty("values").EnumerateArray()
            .Single(x => x.GetProperty("key").GetString() == key).Clone();
    }

    [Fact]
    public void Update_Content_KeepsPositionAndOtherProperties()
    {
        var editor = ManifestEditor.Parse(Json);
        var before = Properties(editor, "Rates.A");

        Assert.Equal(EntryUpdateResult.Updated, editor.UpdateEntry("Rates.A", new EntryUpdate { Content = "9.5" }));

        Assert.Equal(before, Properties(editor, "Rates.A"));
        var entry = Entry(editor, "Rates.A");
        Assert.Equal("9.5", entry.GetProperty("content").GetString());
        Assert.Equal("keep me", entry.GetProperty("note").GetString());
        Assert.Equal("Jane Doe", entry.GetProperty("verification").GetProperty("verifiedBy").GetString());
        Assert.Equal(1, editor.Changes);
    }

    [Fact]
    public void Update_NewProperties_AreInsertedAtTheirPlace()
    {
        var editor = ManifestEditor.Parse(Json);

        editor.UpdateEntry("Rates.A", new EntryUpdate
        {
            SourceQuote = "rate of 8.5", ReferenceYear = 2025, Description = "Rate A"
        });

        Assert.Equal(
            ["key", "content", "unit", "sourceUrls", "sourceLocation", "sourceQuote", "referenceYear", "legalReference",
             "description", "note", "verification"],
            Properties(editor, "Rates.A"));
    }

    [Fact]
    public void Update_LegalReferenceAndSourceUrls_ReplaceValues()
    {
        var editor = ManifestEditor.Parse(Json);

        editor.UpdateEntry("Rates.B", new EntryUpdate
        {
            LegalReference = "Test Act s. 3",
            SourceUrls = ["https://example.org/doc", " https://example.org/other "]
        });

        var entry = Entry(editor, "Rates.B");
        Assert.Equal("Test Act s. 3", entry.GetProperty("legalReference").GetString());
        Assert.Equal(["https://example.org/doc", "https://example.org/other"],
            entry.GetProperty("sourceUrls").EnumerateArray().Select(x => x.GetString()).ToArray());
    }

    [Fact]
    public void Update_EmptyQuoteAndDescription_RemoveProperties()
    {
        var editor = ManifestEditor.Parse(Json);

        editor.UpdateEntry("Rates.B", new EntryUpdate { SourceQuote = "", Description = "" });

        var entry = Entry(editor, "Rates.B");
        Assert.False(entry.TryGetProperty("sourceQuote", out _));
        Assert.False(entry.TryGetProperty("description", out _));
        Assert.Equal(2025, entry.GetProperty("referenceYear").GetInt32());
    }

    [Fact]
    public void Update_ReferenceYear_SetsAndRemoves()
    {
        var editor = ManifestEditor.Parse(Json);

        editor.UpdateEntry("Rates.B", new EntryUpdate { ReferenceYear = 2024 });
        Assert.Equal(2024, Entry(editor, "Rates.B").GetProperty("referenceYear").GetInt32());

        editor.UpdateEntry("Rates.B", new EntryUpdate { RemoveReferenceYear = true });
        Assert.False(Entry(editor, "Rates.B").TryGetProperty("referenceYear", out _));
    }

    [Fact]
    public void Update_SameValues_IsUnchanged()
    {
        var editor = ManifestEditor.Parse(Json);

        var result = editor.UpdateEntry("Rates.B", new EntryUpdate
        {
            Content = "10", LegalReference = "Test Act s. 2", SourceQuote = "rate of 10", ReferenceYear = 2025,
            Description = "Rate B", SourceUrls = ["https://example.org/doc"]
        });

        Assert.Equal(EntryUpdateResult.Unchanged, result);
        Assert.Equal(0, editor.Changes);
    }

    [Fact]
    public void Update_NothingGiven_IsUnchanged() =>
        Assert.Equal(EntryUpdateResult.Unchanged, ManifestEditor.Parse(Json).UpdateEntry("Rates.A", new EntryUpdate()));

    [Fact]
    public void Update_UnknownKey_IsNotFound() =>
        Assert.Equal(EntryUpdateResult.NotFound,
            ManifestEditor.Parse(Json).UpdateEntry("Rates.Z", new EntryUpdate { Content = "1" }));

    [Theory]
    [InlineData("content", "")]
    [InlineData("content", "{ not json")]
    [InlineData("legalReference", " ")]
    [InlineData("sourceUrls", "empty")]
    [InlineData("sourceUrls", "blank")]
    [InlineData("referenceYear", "1800")]
    [InlineData("referenceYear", "2200")]
    public void Update_InvalidValue_Throws(string field, string value)
    {
        var update = field switch
        {
            "content" => new EntryUpdate { Content = value },
            "legalReference" => new EntryUpdate { LegalReference = value },
            "sourceUrls" => new EntryUpdate { SourceUrls = value == "empty" ? [] : [" "] },
            _ => new EntryUpdate { ReferenceYear = int.Parse(value) }
        };
        var editor = ManifestEditor.Parse(Json);

        Assert.Throws<ArgumentException>(() => editor.UpdateEntry("Rates.A", update));
        Assert.Equal(0, editor.Changes);
    }

    [Fact]
    public void Update_SetAndRemoveReferenceYear_Throws() =>
        Assert.Throws<ArgumentException>(() => ManifestEditor.Parse(Json)
            .UpdateEntry("Rates.A", new EntryUpdate { ReferenceYear = 2025, RemoveReferenceYear = true }));

    [Fact]
    public void Update_ContentOfContentFileEntry_Throws() =>
        Assert.Throws<InvalidOperationException>(() => ManifestEditor.Parse(Json)
            .UpdateEntry("Rates.C", new EntryUpdate { Content = "1" }));

    [Fact]
    public void Update_StructuredContent_IsAccepted()
    {
        var editor = ManifestEditor.Parse(Json);

        editor.UpdateEntry("Rates.A", new EntryUpdate { Content = "{ \"Rate\": 0.05 }" });

        Assert.Equal("{ \"Rate\": 0.05 }", Entry(editor, "Rates.A").GetProperty("content").GetString());
    }

    [Fact]
    public void Update_Result_PassesValidation()
    {
        var editor = ManifestEditor.Parse(Json);
        editor.UpdateEntry("Rates.B", new EntryUpdate { ReferenceYear = 2024, SourceQuote = "rate" });

        var manifest = JsonSerializer.Deserialize<Manifest>(editor.ToJson(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var errors = Validator.Validate(manifest, Path.GetTempPath());

        Assert.DoesNotContain(errors, x =>
            x.Contains("referenceYear", StringComparison.Ordinal) || x.Contains("sourceQuote", StringComparison.Ordinal));
    }
}
