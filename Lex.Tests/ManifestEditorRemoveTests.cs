using System.Text.Json;
using Xunit;

namespace PayrollEngine.Lex.Tests;

/// <summary>Change and removal of exclusions, removal of entries and source declarations</summary>
public class ManifestEditorRemoveTests
{
    private const string Json = """
        {
          "authority": "XX",
          "customProperty": 1,
          "sources": [
            { "url": "https://example.org/a", "description": "Source A" },
            { "url": "https://example.org/b", "description": "Source B" }
          ],
          "values": [
            { "key": "Rates.A", "content": "8.5", "sourceUrls": [ "https://example.org/a" ] },
            { "key": "Rates.B", "content": "9.5", "sourceUrls": [ "https://example.org/a", "https://example.org/b" ], "unknown": true },
            { "key": "Rates.C", "content": "1", "sourceUrls": [ "https://example.org/a" ] }
          ],
          "exclusions": [
            { "kind": "orphan", "pattern": "Rates.A", "reason": "Reason one", "extra": "kept" },
            { "kind": "notApplicable", "pattern": "Rates.B.Rate", "reason": "Reason two" }
          ]
        }
        """;

    private static JsonElement Root(ManifestEditor editor)
    {
        using var document = JsonDocument.Parse(editor.ToJson());
        return document.RootElement.Clone();
    }

    private static string[] Keys(ManifestEditor editor) =>
        Root(editor).GetProperty("values").EnumerateArray().Select(x => x.GetProperty("key").GetString()!).ToArray();

    [Fact]
    public void UpdateExclusion_ChangesOnlyTheReason_KeepsPositionAndUnknownProperties()
    {
        var editor = ManifestEditor.Parse(Json);

        var result = editor.UpdateExclusion(Exclusion.KindOrphan, "Rates.A", new ExclusionUpdate { Reason = "  Corrected reason " });

        Assert.Equal(ExclusionUpdateResult.Updated, result);
        Assert.Equal(1, editor.Changes);
        var exclusions = Root(editor).GetProperty("exclusions").EnumerateArray().ToArray();
        Assert.Equal(2, exclusions.Length);
        Assert.Equal(["kind", "pattern", "reason", "extra"], exclusions[0].EnumerateObject().Select(x => x.Name).ToArray());
        Assert.Equal("Corrected reason", exclusions[0].GetProperty("reason").GetString());
        Assert.Equal("Rates.A", exclusions[0].GetProperty("pattern").GetString());
        Assert.Equal("Reason two", exclusions[1].GetProperty("reason").GetString());
    }

    [Fact]
    public void UpdateExclusion_SameReason_IsUnchanged()
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.Equal(ExclusionUpdateResult.Unchanged,
            editor.UpdateExclusion(Exclusion.KindOrphan, "Rates.A", new ExclusionUpdate { Reason = "Reason one" }));
        Assert.Equal(ExclusionUpdateResult.Unchanged,
            editor.UpdateExclusion(Exclusion.KindOrphan, "Rates.A", new ExclusionUpdate()));
        Assert.Equal(0, editor.Changes);
    }

    [Fact]
    public void UpdateExclusion_KindIsPartOfTheIdentity()
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.Equal(ExclusionUpdateResult.NotFound,
            editor.UpdateExclusion(Exclusion.KindDerived, "Rates.A", new ExclusionUpdate { Reason = "x" }));
        Assert.Equal(ExclusionUpdateResult.NotFound,
            editor.UpdateExclusion(Exclusion.KindOrphan, "Rates.Z", new ExclusionUpdate { Reason = "x" }));
        Assert.Equal(0, editor.Changes);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void UpdateExclusion_EmptyReason_Throws(string reason)
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.Throws<ArgumentException>(() =>
            editor.UpdateExclusion(Exclusion.KindOrphan, "Rates.A", new ExclusionUpdate { Reason = reason }));
    }

    [Fact]
    public void RemoveExclusion_RemovesOnlyThatExclusion()
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.True(editor.RemoveExclusion(Exclusion.KindNotApplicable, "Rates.B.Rate"));

        var exclusions = Root(editor).GetProperty("exclusions").EnumerateArray().ToArray();
        Assert.Single(exclusions);
        Assert.Equal("Rates.A", exclusions[0].GetProperty("pattern").GetString());
        Assert.False(editor.HasExclusion(Exclusion.KindNotApplicable, "Rates.B.Rate"));
        Assert.Equal(1, editor.Changes);
    }

    [Fact]
    public void RemoveExclusion_Unknown_ChangesNothing()
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.False(editor.RemoveExclusion(Exclusion.KindOrphan, "Rates.Z"));
        Assert.False(editor.RemoveExclusion(Exclusion.KindDerived, "Rates.A"));
        Assert.Equal(0, editor.Changes);
    }

    [Fact]
    public void RemoveEntry_RemovesTheEntryOnly_AndKeepsTheRest()
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.True(editor.RemoveEntry("Rates.B"));

        Assert.Equal(["Rates.A", "Rates.C"], Keys(editor));
        var root = Root(editor);
        Assert.Equal(1, root.GetProperty("customProperty").GetInt32());
        Assert.Equal(2, root.GetProperty("sources").GetArrayLength());
        Assert.Equal(2, root.GetProperty("exclusions").GetArrayLength());
        Assert.Equal(1, editor.Changes);
        Assert.False(editor.HasKey("Rates.B"));
    }

    [Fact]
    public void RemoveEntry_UnknownKey_ChangesNothing()
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.False(editor.RemoveEntry("Rates.Z"));
        Assert.False(editor.RemoveEntry("Rates"));
        Assert.Equal(0, editor.Changes);
    }

    [Fact]
    public void RemoveSource_Unused_RemovesTheDeclaration()
    {
        var editor = ManifestEditor.Parse(Json);
        editor.RemoveEntry("Rates.B");

        Assert.True(editor.RemoveSource("https://example.org/b"));

        var sources = Root(editor).GetProperty("sources").EnumerateArray().ToArray();
        Assert.Single(sources);
        Assert.Equal("https://example.org/a", sources[0].GetProperty("url").GetString());
        Assert.False(editor.HasSource("https://example.org/b"));
        Assert.Equal(2, editor.Changes);
    }

    [Fact]
    public void RemoveSource_StillUsedByAnEntry_Throws()
    {
        var editor = ManifestEditor.Parse(Json);

        var exception = Assert.Throws<InvalidOperationException>(() => editor.RemoveSource("https://example.org/b"));

        Assert.Contains("Rates.B", exception.Message, StringComparison.Ordinal);
        Assert.True(editor.HasSource("https://example.org/b"));
        Assert.Equal(0, editor.Changes);
    }

    [Fact]
    public void RemoveSource_Undeclared_ReturnsFalse()
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.False(editor.RemoveSource("https://example.org/none"));
        Assert.Equal(0, editor.Changes);
    }
}
