using System.Text.Json;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public class ManifestEditorExclusionTests
{
    private const string Json = """
        {
          "authority": "XX",
          "values": [
            { "key": "Rates.A", "content": "8.5" },
            { "key": "Bundle.B", "content": "{ \"Rate\": 0, \"Total\": 5 }" }
          ],
          "exclusions": [ { "kind": "orphan", "pattern": "Old.*", "reason": "Implemented in code" } ]
        }
        """;

    private static JsonElement[] Exclusions(ManifestEditor editor)
    {
        using var document = JsonDocument.Parse(editor.ToJson());
        return document.RootElement.GetProperty("exclusions").EnumerateArray().Select(x => x.Clone()).ToArray();
    }

    [Theory]
    [InlineData(Exclusion.KindCoverage, "Other.*")]
    [InlineData(Exclusion.KindSourceLocation, "Rates.A")]
    [InlineData(Exclusion.KindOrphan, "Rates.*")]
    [InlineData(Exclusion.KindAutoVerification, "Rates.A")]
    [InlineData(Exclusion.KindDerived, "Bundle.B.Total")]
    [InlineData(Exclusion.KindNotApplicable, "Bundle.B.Rate")]
    public void Add_AppendsExclusionWithKindPatternReason(string kind, string pattern)
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.True(editor.AddExclusion(new Exclusion { Kind = kind, Pattern = pattern, Reason = "Reason" }));

        var added = Exclusions(editor)[1];
        Assert.Equal(["kind", "pattern", "reason"], added.EnumerateObject().Select(x => x.Name).ToArray());
        Assert.Equal(kind, added.GetProperty("kind").GetString());
        Assert.Equal(pattern, added.GetProperty("pattern").GetString());
        Assert.Equal("Reason", added.GetProperty("reason").GetString());
        Assert.Equal(1, editor.Changes);
        Assert.True(editor.HasExclusion(kind, pattern));
    }

    [Fact]
    public void Add_CreatesExclusionsArray()
    {
        var editor = ManifestEditor.Parse("{ \"authority\": \"XX\", \"values\": [] }");

        Assert.True(editor.AddExclusion(new Exclusion { Kind = Exclusion.KindOrphan, Pattern = "Old.*", Reason = "Reason" }));

        Assert.Single(Exclusions(editor));
    }

    [Fact]
    public void Add_PatternIsTrimmed()
    {
        var editor = ManifestEditor.Parse(Json);

        editor.AddExclusion(new Exclusion { Kind = Exclusion.KindDerived, Pattern = " Bundle.B.Total ", Reason = "Reason" });

        Assert.Equal("Bundle.B.Total", Exclusions(editor)[1].GetProperty("pattern").GetString());
    }

    [Fact]
    public void Add_SameKindAndPattern_IsNotAddedTwice()
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.False(editor.AddExclusion(new Exclusion { Kind = Exclusion.KindOrphan, Pattern = "Old.*", Reason = "Other reason" }));

        Assert.Single(Exclusions(editor));
        Assert.Equal("Implemented in code", Exclusions(editor)[0].GetProperty("reason").GetString());
        Assert.Equal(0, editor.Changes);
    }

    [Fact]
    public void Add_SamePatternOtherKind_IsAdded() =>
        Assert.True(ManifestEditor.Parse(Json).AddExclusion(
            new Exclusion { Kind = Exclusion.KindCoverage, Pattern = "Old.*", Reason = "Reason" }));

    [Theory]
    [InlineData("unknown", "Rates.A", "Reason")]
    [InlineData(null, "Rates.A", "Reason")]
    [InlineData(Exclusion.KindOrphan, "", "Reason")]
    [InlineData(Exclusion.KindOrphan, " ", "Reason")]
    [InlineData(Exclusion.KindOrphan, "Rates.A", "")]
    [InlineData(Exclusion.KindOrphan, "Rates.A", " ")]
    [InlineData(Exclusion.KindDerived, "Bundle.B", "Reason")]
    [InlineData(Exclusion.KindNotApplicable, "Bundle.B", "Reason")]
    [InlineData(Exclusion.KindDerived, "Missing.Key.Total", "Reason")]
    [InlineData(Exclusion.KindNotApplicable, "Missing.Key.Rate", "Reason")]
    public void Add_InvalidExclusion_Throws(string kind, string pattern, string reason)
    {
        var editor = ManifestEditor.Parse(Json);

        Assert.Throws<ArgumentException>(() =>
            editor.AddExclusion(new Exclusion { Kind = kind, Pattern = pattern, Reason = reason }));

        Assert.Single(Exclusions(editor));
        Assert.Equal(0, editor.Changes);
    }

    [Fact]
    public void Add_Result_PassesValidation()
    {
        var editor = ManifestEditor.Parse(Json);
        editor.AddExclusion(new Exclusion { Kind = Exclusion.KindDerived, Pattern = "Bundle.B.Total", Reason = "Sum of the rates" });

        var manifest = JsonSerializer.Deserialize<Manifest>(editor.ToJson(),
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        var errors = Validator.Validate(manifest, Path.GetTempPath());

        Assert.DoesNotContain(errors, x => x.Contains("Exclusion", StringComparison.Ordinal));
    }
}
