using System.Diagnostics;
using Xunit;

namespace PayrollEngine.Lex.Tests;

/// <summary>
/// The git listing of the evidence check must never stall the caller (an MCP server runs it in-process)
/// </summary>
public class EvidenceCheckGitTests
{
    [Fact]
    public void ListFiles_ProcessThatNeverAnswers_EndsAtTheTimeout_AndReturnsEmptySet()
    {
        using var satellite = new TestSatellite();
        var stopwatch = Stopwatch.StartNew();

        var files = EvidenceCheck.ListFiles(NeverAnswering(), satellite.Root, TimeSpan.FromSeconds(1));

        stopwatch.Stop();
        Assert.Empty(files);
        Assert.True(stopwatch.Elapsed < TimeSpan.FromSeconds(15), $"Timeout did not apply: {stopwatch.Elapsed}");
    }

    [Fact]
    public void ListFiles_WithoutExecutable_ReturnsEmptySet()
    {
        using var satellite = new TestSatellite();
        var info = new ProcessStartInfo("lex-test-missing-" + Guid.NewGuid().ToString("N"));

        var files = EvidenceCheck.ListFiles(info, satellite.Root, TimeSpan.FromSeconds(5));

        Assert.Empty(files);
    }

    [Fact]
    public void ListFiles_WithoutDirectory_ReturnsEmptySet()
    {
        // ReSharper disable once StringLiteralTypo
        var missing = Path.Combine(Path.GetTempPath(), "lextest-missing-" + Guid.NewGuid().ToString("N"));

        var files = EvidenceCheck.ListFiles(NeverAnswering(), missing, TimeSpan.FromSeconds(1));

        Assert.Empty(files);
    }

    [Fact]
    public void ListFiles_ReadsOneFullPathPerLine()
    {
        using var satellite = new TestSatellite();
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd", "/c echo a.txt& echo b.txt")
            : new ProcessStartInfo("sh", "-c \"echo a.txt; echo b.txt\"");

        var files = EvidenceCheck.ListFiles(info, satellite.Root, TimeSpan.FromSeconds(30));

        Assert.Equal(2, files.Count);
        Assert.Contains(Path.Combine(satellite.Root, "a.txt"), files);
        Assert.Contains(Path.Combine(satellite.Root, "b.txt"), files);
    }

    [Fact]
    public void RunListing_WithoutExecutable_NamesTheProblem()
    {
        using var satellite = new TestSatellite();
        var info = new ProcessStartInfo("lex-test-missing-" + Guid.NewGuid().ToString("N"));

        var listing = EvidenceCheck.RunListing(info, satellite.Root, TimeSpan.FromSeconds(5));

        Assert.Empty(listing.Files);
        Assert.Equal("git not available", listing.Problem);
    }

    [Fact]
    public void RunListing_Timeout_NamesTheProblem()
    {
        using var satellite = new TestSatellite();

        var listing = EvidenceCheck.RunListing(NeverAnswering(), satellite.Root, TimeSpan.FromSeconds(1));

        Assert.Empty(listing.Files);
        Assert.Contains("did not answer", listing.Problem);
    }

    [Fact]
    public void RunListing_NotARepository_NamesTheProblem()
    {
        using var satellite = new TestSatellite();
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd", "/c echo fatal: not a git repository 1>&2 & exit 128")
            : new ProcessStartInfo("sh", "-c \"echo 'fatal: not a git repository' >&2; exit 128\"");

        var listing = EvidenceCheck.RunListing(info, satellite.Root, TimeSpan.FromSeconds(30));

        Assert.Empty(listing.Files);
        Assert.Equal("not inside a git repository", listing.Problem);
    }

    [Fact]
    public void RunListing_Success_HasNoProblem()
    {
        using var satellite = new TestSatellite();
        var info = OperatingSystem.IsWindows()
            ? new ProcessStartInfo("cmd", "/c echo a.txt")
            : new ProcessStartInfo("sh", "-c \"echo a.txt\"");

        var listing = EvidenceCheck.RunListing(info, satellite.Root, TimeSpan.FromSeconds(30));

        Assert.Null(listing.Problem);
        Assert.Single(listing.Files);
    }

    [Fact]
    public void Check_OutsideARepository_WarnsInsteadOfPassingSilently()
    {
        // the test satellite lives in the temp folder: no repository (or no git at all)
        using var satellite = new TestSatellite();
        satellite.WriteRegulation("[ { \"name\": \"Rates\", \"values\": [ { \"key\": \"A\", \"value\": \"1\" } ] } ]");
        var manifest = TestSatellite.Manifest();

        var result = Checker.Check(manifest, satellite.LexPath);

        Assert.Contains(result.Warnings, x => x.EndsWith(": evidence versioning not checked", StringComparison.Ordinal) &&
                                              (x.StartsWith("git not available", StringComparison.Ordinal) ||
                                               x.StartsWith("not inside a git repository", StringComparison.Ordinal)));
        // a missing git is a warning, not a finding
        Assert.DoesNotContain(result.FileProblems, x => x.Contains("git", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A child process that stays alive without writing anything</summary>
    private static ProcessStartInfo NeverAnswering() =>
        OperatingSystem.IsWindows()
            ? new ProcessStartInfo("ping", "-n 60 127.0.0.1")
            : new ProcessStartInfo("sleep", "60");
}
