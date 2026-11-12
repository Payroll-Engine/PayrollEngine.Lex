using Xunit;

namespace PayrollEngine.Lex.Tests;

public class AddEntryOperationTests
{
    private const string Url = "https://example.org/rates";

    private const string Json = """
        {
          "authority": "XX",
          "sources": [ { "url": "https://example.org/rates" } ],
          "values": [ { "key": "Rates.A", "content": "8.5" } ],
          "files": [ { "fileName": "rates.pdf" } ]
        }
        """;

    private static TestSatellite Satellite()
    {
        var s = new TestSatellite();
        File.WriteAllText(s.LexPath, Json);
        return s;
    }

    private static Entry Entry(string key = "Rates.B", string url = Url, string documentRef = null) => new()
    {
        Key = key,
        Content = "9.5",
        SourceUrls = url != null ? [url] : [],
        SourceLocation = "Page 1",
        LegalReference = "Test Act s. 1",
        DocumentRef = documentRef
    };

    [Fact]
    public async Task NewEntry_IsAdded()
    {
        using var s = Satellite();

        var result = await AddEntryOperation.AddAsync(s.LexDirectory, Entry(documentRef: "rates.pdf"));

        Assert.True(result.Added);
        Assert.Equal(s.LexPath, result.LexPath);
        Assert.Empty(result.Errors);
        Assert.Empty(result.Missing);
        Assert.True(ManifestEditor.Parse(await File.ReadAllTextAsync(s.LexPath)).HasKey("Rates.B"));
    }

    [Fact]
    public async Task ExistingKey_IsRejected()
    {
        using var s = Satellite();

        var result = await AddEntryOperation.AddAsync(s.LexPath, Entry("Rates.A"));

        Assert.False(result.Added);
        Assert.Equal(["Entry Rates.A exists"], result.Errors);
        Assert.Equal(Json, await File.ReadAllTextAsync(s.LexPath));
    }

    [Fact]
    public async Task UndeclaredSourceUrl_IsRejected()
    {
        using var s = Satellite();

        var result = await AddEntryOperation.AddAsync(s.LexPath, Entry(url: "https://example.org/other"));

        Assert.Equal(["Source URL is not declared in sources[]: https://example.org/other"], result.Errors);
        Assert.Equal(Json, await File.ReadAllTextAsync(s.LexPath));
    }

    [Fact]
    public async Task UndeclaredDocument_IsRejected()
    {
        using var s = Satellite();

        var result = await AddEntryOperation.AddAsync(s.LexPath, Entry(documentRef: "missing.pdf"));

        Assert.Equal(["Document is not declared in files[] (LexFetch): missing.pdf"], result.Errors);
    }

    [Fact]
    public async Task AllErrors_AreReported()
    {
        using var s = Satellite();

        var result = await AddEntryOperation.AddAsync(s.LexPath, Entry("Rates.A", "https://example.org/other", "missing.pdf"));

        Assert.Equal(3, result.Errors.Count);
    }

    [Theory]
    [InlineData("Rates", null, "must have the format Lookup.Key")]
    [InlineData("Rates.*", null, "must not contain whitespace or *")]
    [InlineData("Rates.B", "{ \"Rate\": ", "Content is not valid JSON")]
    public void InvalidFormat_IsRejected(string key, string content, string expected)
    {
        var editor = ManifestEditor.Parse(Json);

        var errors = AddEntryOperation.Validate(editor, new Entry { Key = key, Content = content });

        Assert.Contains(expected, Assert.Single(errors));
    }

    [Fact]
    public async Task MissingFields_AreWrittenEmptyAndListed()
    {
        using var s = Satellite();

        var result = await AddEntryOperation.AddAsync(s.LexPath, new Entry { Key = "Rates.B" });

        Assert.True(result.Added);
        Assert.Equal(["content", "sourceUrls", "sourceLocation", "legalReference"], result.Missing);
    }

    [Fact]
    public async Task MissingManifest_IsAnError()
    {
        using var s = new TestSatellite();
        var folder = Path.Combine(s.Root, "Regulation");

        var result = await AddEntryOperation.AddAsync(folder, Entry());

        Assert.Null(result.LexPath);
        Assert.Equal([$"lex.json not found: {folder}"], result.Errors);
    }
}
