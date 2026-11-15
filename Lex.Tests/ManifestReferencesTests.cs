using Xunit;

namespace PayrollEngine.Lex.Tests;

/// <summary>References between entries, exclusions, sources and the regulation</summary>
public class ManifestReferencesTests
{
    private const string Lookups = """
        [
          { "name": "Rates", "values": [ { "key": "A", "value": "8.5" } ] },
          { "name": "Table", "values": [ { "key": "X", "value": "1" }, { "key": "Y", "value": "2" } ] }
        ]
        """;

    private static Manifest Manifest(params Exclusion[] exclusions)
    {
        var manifest = TestSatellite.Manifest(
            TestSatellite.Value("Rates.A", "8.5"),
            TestSatellite.Value("Rates.B", "9.5"),
            TestSatellite.Value("Bundle.C", "{ \"Rate\": 0, \"Total\": 5 }"),
            TestSatellite.Value("Other.D", "1"));
        return new Manifest
        {
            Authority = manifest.Authority, Regulation = manifest.Regulation, ValidFrom = manifest.ValidFrom,
            Sources = manifest.Sources, Values = manifest.Values, Exclusions = [.. exclusions]
        };
    }

    [Fact]
    public void EntriesOfExclusion_PropertyKind_IsTheEntryOfThePattern()
    {
        var exclusion = new Exclusion { Kind = Exclusion.KindNotApplicable, Pattern = "Bundle.C.Rate", Reason = "r" };

        Assert.Equal(["Bundle.C"], ManifestReferences.EntriesOfExclusion(Manifest(exclusion), exclusion));
    }

    [Fact]
    public void EntriesOfExclusion_KeyPatternKind_UsesKeyPattern()
    {
        var exclusion = new Exclusion { Kind = Exclusion.KindOrphan, Pattern = "Rates.*", Reason = "r" };

        Assert.Equal(["Rates.A", "Rates.B"], ManifestReferences.EntriesOfExclusion(Manifest(exclusion), exclusion));
    }

    [Fact]
    public void EntriesOfExclusion_Coverage_ConcernsNoEntry()
    {
        var exclusion = new Exclusion { Kind = Exclusion.KindCoverage, Pattern = "Rates.*", Reason = "r" };

        Assert.Empty(ManifestReferences.EntriesOfExclusion(Manifest(exclusion), exclusion));
    }

    [Fact]
    public void ExclusionsOfEntry_SplitsExclusiveFromShared()
    {
        var shared = new Exclusion { Kind = Exclusion.KindOrphan, Pattern = "Rates.*", Reason = "r" };
        var own = new Exclusion { Kind = Exclusion.KindSourceLocation, Pattern = "Rates.A", Reason = "r" };
        var property = new Exclusion { Kind = Exclusion.KindNotApplicable, Pattern = "Bundle.C.Rate", Reason = "r" };
        var manifest = Manifest(shared, own, property);

        var (all, exclusive) = ManifestReferences.ExclusionsOfEntry(manifest, "Rates.A");

        Assert.Equal([shared, own], all);
        Assert.Equal([own], exclusive);
        Assert.Equal([property], ManifestReferences.ExclusionsOfEntry(manifest, "Bundle.C").Exclusive);
        Assert.Empty(ManifestReferences.ExclusionsOfEntry(manifest, "Other.D").All);
    }

    [Fact]
    public void EntriesOfSource_ListsEntriesWithTheUrl()
    {
        var manifest = Manifest();

        Assert.Equal(["Rates.A", "Rates.B", "Bundle.C", "Other.D"],
            ManifestReferences.EntriesOfSource(manifest, "https://example.org/doc"));
        Assert.Empty(ManifestReferences.EntriesOfSource(manifest, "https://example.org/none"));
    }

    [Fact]
    public void OtherEntriesOfDocument_IgnoresTheEntryItself()
    {
        var manifest = new Manifest
        {
            Values =
            [
                new Entry { Key = "A", DocumentRef = "doc.pdf" },
                new Entry { Key = "B", DocumentRef = "DOC.pdf" },
                new Entry { Key = "C", DocumentRef = "other.pdf" }
            ]
        };

        Assert.Equal(["B"], ManifestReferences.OtherEntriesOfDocument(manifest, "A", "doc.pdf"));
        Assert.Empty(ManifestReferences.OtherEntriesOfDocument(manifest, "C", "other.pdf"));
        Assert.Empty(ManifestReferences.OtherEntriesOfDocument(manifest, "C", null));
    }

    [Fact]
    public void RegulationLinkOf_ExistingValue_IsLinked()
    {
        using var s = new TestSatellite();
        s.WriteRegulation(Lookups);

        var link = ManifestReferences.RegulationLinkOf(Manifest(), s.LexPath, "Rates.A");

        Assert.True(link.Linked);
        Assert.Equal("Rates.A", link.Target);
        Assert.Equal(1, link.Values);
    }

    [Fact]
    public void RegulationLinkOf_NewValueOfExistingLookup_IsNotLinked()
    {
        using var s = new TestSatellite();
        s.WriteRegulation(Lookups);

        Assert.False(ManifestReferences.RegulationLinkOf(Manifest(), s.LexPath, "Rates.B").Linked);
    }

    [Fact]
    public void RegulationLinkOf_WithoutRegulationTarget_OrUnknownKey_IsNotLinked()
    {
        using var s = new TestSatellite();
        s.WriteRegulation(Lookups);

        Assert.False(ManifestReferences.RegulationLinkOf(Manifest(), s.LexPath, "Other.D").Linked);
        Assert.False(ManifestReferences.RegulationLinkOf(Manifest(), s.LexPath, "Unknown.Key").Linked);
    }
}
