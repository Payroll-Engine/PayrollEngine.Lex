using System.Text.Json;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class ManifestEditorRemoveVerificationTests
{
    private const string Json = """
        {
          "authority": "XX",
          "values": [
            { "key": "Rates.A", "content": "8.5", "description": "Rate A", "legalReference": "Test Act s. 1",
              "verification": { "mode": "manual", "verifiedOn": "2026-01-10", "verifiedBy": "Jane Doe", "evidence": "doc.pdf" } },
            { "key": "Rates.B", "content": "10", "sourceUrls": [ "https://example.org/doc" ],
              "verification": { "mode": "auto", "verifiedOn": "2026-02-01", "method": "LexVerifySource 1.2.0", "evidence": "doc.html" } },
            { "key": "Rates.C", "content": "12", "description": "Open rate", "legalReference": "Test Act s. 3" }
          ]
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
    public void Remove_ManualRecord_ReturnsModePersonAndDate()
    {
        var editor = ManifestEditor.Parse(Json);

        var removed = editor.RemoveVerification("Rates.A");

        Assert.NotNull(removed);
        Assert.Equal(Verification.ModeManual, removed.Mode);
        Assert.Equal("Jane Doe", removed.VerifiedBy);
        Assert.Equal("2026-01-10", removed.VerifiedOn);
        Assert.False(Entry(editor, "Rates.A").TryGetProperty("verification", out _));
        Assert.Equal(1, editor.Changes);
    }

    [Fact]
    public void Remove_AutoRecord_ReturnsModeAndDateWithoutPerson()
    {
        var editor = ManifestEditor.Parse(Json);

        var removed = editor.RemoveVerification("Rates.B");

        Assert.NotNull(removed);
        Assert.Equal(Verification.ModeAuto, removed.Mode);
        Assert.Null(removed.VerifiedBy);
        Assert.Equal("2026-02-01", removed.VerifiedOn);
        Assert.False(Entry(editor, "Rates.B").TryGetProperty("verification", out _));
        Assert.Equal(1, editor.Changes);
    }

    [Fact]
    public void Remove_NoRecord_IsNotAnError()
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.Null(editor.RemoveVerification("Rates.C"));
        Assert.Null(editor.RemoveVerification("Rates.Z"));
        Assert.Equal(0, editor.Changes);
    }

    [Fact]
    public void Remove_KeepsPropertyOrder()
    {
        var editor = ManifestEditor.Parse(Json);
        var before = Properties(editor, "Rates.A");

        editor.RemoveVerification("Rates.A");

        Assert.Equal(before.Where(x => x != "verification").ToArray(), Properties(editor, "Rates.A"));
        Assert.Equal("Rate A", Entry(editor, "Rates.A").GetProperty("description").GetString());
    }

    [Theory]
    [InlineData(true, "content")]
    [InlineData(true, "sourceUrls")]
    [InlineData(true, "sourceQuote")]
    [InlineData(true, "referenceYear")]
    [InlineData(true, "removeReferenceYear")]
    [InlineData(false, "description")]
    [InlineData(false, "legalReference")]
    [InlineData(false, "none")]
    public void EntryUpdate_InvalidatesVerification(bool expected, string field)
    {
        var update = field switch
        {
            "content" => new EntryUpdate { Content = "9" },
            "sourceUrls" => new EntryUpdate { SourceUrls = ["https://example.org/doc"] },
            "sourceQuote" => new EntryUpdate { SourceQuote = "quote" },
            "referenceYear" => new EntryUpdate { ReferenceYear = 2025 },
            "removeReferenceYear" => new EntryUpdate { RemoveReferenceYear = true },
            "description" => new EntryUpdate { Description = "note" },
            "legalReference" => new EntryUpdate { LegalReference = "Test Act s. 2" },
            _ => new EntryUpdate()
        };

        Assert.Equal(expected, update.InvalidatesVerification);
    }
}
