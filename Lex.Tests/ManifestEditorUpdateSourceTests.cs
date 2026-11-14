using System.Text.Json;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class ManifestEditorUpdateSourceTests
{
    private const string Json = """
        {
          "authority": "XX",
          "sources": [
            { "url": "https://example.org/act", "description": "Example Act", "review": "FourEyes", "extra": 1 },
            { "url": "https://example.org/guide", "official": false, "description": "Commercial summary, not the authority" },
            { "url": "https://example.org/plain" }
          ],
          "values": []
        }
        """;

    private static JsonElement Source(ManifestEditor editor, int index)
    {
        using var document = JsonDocument.Parse(editor.ToJson());
        return document.RootElement.GetProperty("sources")[index].Clone();
    }

    private static string[] Properties(ManifestEditor editor, int index) =>
        Source(editor, index).EnumerateObject().Select(x => x.Name).ToArray();

    [Fact]
    public void UpdateSource_Description_KeepsPositionAndOtherProperties()
    {
        var editor = ManifestEditor.Parse(Json);
        var before = Properties(editor, 0);

        Assert.Equal(SourceUpdateResult.Updated,
            editor.UpdateSource("https://example.org/act", new SourceUpdate { Description = " Example Act, s. 1 " }));

        Assert.Equal(before, Properties(editor, 0));
        var source = Source(editor, 0);
        Assert.Equal("Example Act, s. 1", source.GetProperty("description").GetString());
        Assert.Equal("FourEyes", source.GetProperty("review").GetString());
        Assert.Equal(1, source.GetProperty("extra").GetInt32());
        Assert.Equal(1, editor.Changes);
    }

    [Fact]
    public void UpdateSource_NewProperties_AreInsertedInSchemaOrder()
    {
        var editor = ManifestEditor.Parse(Json);

        editor.UpdateSource("https://example.org/act", new SourceUpdate { LegalBasis = "Example Act s. 3" });
        editor.UpdateSource("https://example.org/plain", new SourceUpdate { LegalBasis = "Basis", Description = "Plain" });

        Assert.Equal(["url", "description", "legalBasis", "review", "extra"], Properties(editor, 0));
        Assert.Equal(["url", "description", "legalBasis"], Properties(editor, 2));
    }

    [Fact]
    public void UpdateSource_SameValues_AreUnchanged()
    {
        var editor = ManifestEditor.Parse(Json);
        var before = editor.ToJson();

        Assert.Equal(SourceUpdateResult.Unchanged,
            editor.UpdateSource("https://example.org/act", new SourceUpdate { Description = "Example Act" }));

        Assert.Equal(0, editor.Changes);
        Assert.Equal(before, editor.ToJson());
    }

    [Fact]
    public void UpdateSource_EmptyText_RemovesTheProperty()
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.Equal(SourceUpdateResult.Updated,
            editor.UpdateSource("https://example.org/act", new SourceUpdate { Description = "" }));

        Assert.DoesNotContain("description", Properties(editor, 0));
    }

    [Fact]
    public void UpdateSource_RemovingTheDescriptionOfASecondarySource_Throws()
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.Throws<ArgumentException>(() =>
            editor.UpdateSource("https://example.org/guide", new SourceUpdate { Description = "  " }));
        Assert.Equal(0, editor.Changes);
    }

    [Fact]
    public void UpdateSource_ChangingTheDescriptionOfASecondarySource_Works()
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.Equal(SourceUpdateResult.Updated,
            editor.UpdateSource("https://example.org/guide", new SourceUpdate { Description = "Summary, not the authority" }));
        Assert.False(Source(editor, 1).GetProperty("official").GetBoolean());
    }

    [Fact]
    public void UpdateSource_UndeclaredUrl_IsNotFound()
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.Equal(SourceUpdateResult.NotFound,
            editor.UpdateSource("https://example.org/other", new SourceUpdate { Description = "x" }));
        Assert.Equal(0, editor.Changes);
    }
}
