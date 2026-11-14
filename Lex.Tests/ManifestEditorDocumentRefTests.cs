using System.Text.Json;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class ManifestEditorDocumentRefTests
{
    private const string Json = """
        {
          "authority": "XX",
          "sources": [ { "url": "https://example.org/doc", "description": "Doc" } ],
          "values": [
            { "key": "Rates.A", "content": "8.5", "sourceUrls": [ "https://example.org/doc" ],
              "sourceLocation": "p. 1", "legalReference": "Test Act s. 1", "description": "Rate A", "note": "keep me",
              "verification": { "mode": "auto", "verifiedOn": "2026-01-10", "evidence": "old.pdf", "method": "x" } },
            { "key": "Rates.B", "content": "10", "sourceUrls": [ "https://example.org/doc" ], "sourceLocation": "p. 2",
              "legalReference": "Test Act s. 2", "documentRef": "old.pdf" }
          ],
          "files": [ { "fileName": "old.pdf" }, { "fileName": "new.pdf" } ]
        }
        """;

    private static JsonElement Entry(ManifestEditor editor, string key)
    {
        using var document = JsonDocument.Parse(editor.ToJson());
        return document.RootElement.GetProperty("values").EnumerateArray()
            .Single(x => x.GetProperty("key").GetString() == key).Clone();
    }

    private static string[] Properties(ManifestEditor editor, string key) =>
        Entry(editor, key).EnumerateObject().Select(x => x.Name).ToArray();

    [Fact]
    public void Update_NewDocumentRef_IsInsertedAfterLegalReference()
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.Equal(EntryUpdateResult.Updated, editor.UpdateEntry("Rates.A", new EntryUpdate { DocumentRef = " new.pdf " }));

        Assert.Equal(["key", "content", "sourceUrls", "sourceLocation", "legalReference", "documentRef", "description", "note",
                "verification"],
            Properties(editor, "Rates.A"));
        Assert.Equal("new.pdf", Entry(editor, "Rates.A").GetProperty("documentRef").GetString());
        Assert.Equal(1, editor.Changes);
    }

    [Fact]
    public void Update_ChangedDocumentRef_KeepsPositionAndVerificationRecord()
    {
        var editor = ManifestEditor.Parse(Json);
        var before = Properties(editor, "Rates.B");

        editor.UpdateEntry("Rates.B", new EntryUpdate { DocumentRef = "new.pdf" });

        Assert.Equal(before, Properties(editor, "Rates.B"));
        Assert.Equal("new.pdf", Entry(editor, "Rates.B").GetProperty("documentRef").GetString());
        // the editor never touches the verification record: the caller decides
        Assert.Equal("old.pdf", Entry(editor, "Rates.A").GetProperty("verification").GetProperty("evidence").GetString());
    }

    [Fact]
    public void Update_SameDocumentRef_IsUnchanged()
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.Equal(EntryUpdateResult.Unchanged, editor.UpdateEntry("Rates.B", new EntryUpdate { DocumentRef = "old.pdf" }));
        Assert.Equal(0, editor.Changes);
    }

    [Fact]
    public void Update_EmptyDocumentRef_RemovesTheReference()
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.Equal(EntryUpdateResult.Updated, editor.UpdateEntry("Rates.B", new EntryUpdate { DocumentRef = "" }));

        Assert.DoesNotContain("documentRef", Properties(editor, "Rates.B"));
    }

    [Fact]
    public void Update_EmptyDocumentRefWithoutReference_IsUnchanged()
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.Equal(EntryUpdateResult.Unchanged, editor.UpdateEntry("Rates.A", new EntryUpdate { DocumentRef = "" }));
    }

    [Fact]
    public void EntryUpdate_DocumentRef_ChangesEvidenceButNotTheVerifiedClaim()
    {
        var update = new EntryUpdate { DocumentRef = "new.pdf" };

        Assert.True(update.ChangesEvidence);
        Assert.False(update.InvalidatesVerification);
        Assert.False(new EntryUpdate { Description = "x" }.ChangesEvidence);
    }
}
