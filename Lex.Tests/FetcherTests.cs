using System.Net;
using System.Net.Http.Headers;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public sealed class FetcherTests : IDisposable
{
    private readonly string directory = Path.Combine(Path.GetTempPath(), "lexfetch-" + Guid.NewGuid().ToString("N"));

    public FetcherTests() => Directory.CreateDirectory(directory);

    private sealed class Handler(HttpStatusCode status, string body, string mediaType) : HttpMessageHandler
    {
        private int Requests { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests++;
            var content = new ByteArrayContent(System.Text.Encoding.UTF8.GetBytes(body));
            content.Headers.ContentType = new MediaTypeHeaderValue(mediaType);
            return Task.FromResult(new HttpResponseMessage(status) { Content = content });
        }
    }

    private static HttpClient Client(string body = "rates 8.5", HttpStatusCode status = HttpStatusCode.OK,
        string mediaType = "application/pdf") => new(new Handler(status, body, mediaType));

    [Fact]
    public async Task Fetch_CreatesFileNamedAfterUrl()
    {
        var result = await Fetcher.FetchAsync(Client(), "https://example.org/docs/tax-rates-2026.pdf", directory);

        Assert.Equal(FetchState.Created, result.State);
        Assert.Equal("tax-rates-2026.pdf", result.FileName);
        Assert.Equal("rates 8.5", await File.ReadAllTextAsync(Path.Combine(directory, "tax-rates-2026.pdf")));
        Assert.Equal(9, result.Size);
        Assert.Equal(64, result.Sha256.Length);
    }

    [Fact]
    public async Task Fetch_SameContent_IsUnchanged()
    {
        await Fetcher.FetchAsync(Client(), "https://example.org/rates.pdf", directory);
        var result = await Fetcher.FetchAsync(Client(), "https://example.org/rates.pdf", directory);
        Assert.Equal(FetchState.Unchanged, result.State);
    }

    [Fact]
    public async Task Fetch_DifferentContent_RequiresOverwrite()
    {
        await Fetcher.FetchAsync(Client(), "https://example.org/rates.pdf", directory);

        var kept = await Fetcher.FetchAsync(Client("rates 9.0"), "https://example.org/rates.pdf", directory);
        Assert.Equal(FetchState.Differs, kept.State);
        Assert.Equal("rates 8.5", await File.ReadAllTextAsync(Path.Combine(directory, "rates.pdf")));

        var replaced = await Fetcher.FetchAsync(Client("rates 9.0"), "https://example.org/rates.pdf", directory, overwrite: true);
        Assert.Equal(FetchState.Replaced, replaced.State);
        Assert.Equal("rates 9.0", await File.ReadAllTextAsync(Path.Combine(directory, "rates.pdf")));
    }

    [Fact]
    public async Task Fetch_ExplicitFileName()
    {
        var result = await Fetcher.FetchAsync(Client(), "https://example.org/rates", directory, "rates-2026.html");
        Assert.Equal("rates-2026.html", result.FileName);
        Assert.True(File.Exists(Path.Combine(directory, "rates-2026.html")));
    }

    [Fact]
    public async Task Fetch_FileNameWithFolder_IsRejected() =>
        await Assert.ThrowsAsync<ArgumentException>(() =>
            Fetcher.FetchAsync(Client(), "https://example.org/rates", directory, Path.Combine("..", "rates.pdf")));

    [Fact]
    public async Task Fetch_HttpError_ThrowsAndWritesNothing()
    {
        var exception = await Assert.ThrowsAsync<HttpRequestException>(() =>
            Fetcher.FetchAsync(Client(status: HttpStatusCode.Forbidden), "https://example.org/rates.pdf", directory));

        Assert.Equal(HttpStatusCode.Forbidden, exception.StatusCode);
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("https://example.org/docs/rates", "text/html", "rates.html")]
    [InlineData("https://example.org/docs/rates", "application/pdf", "rates.pdf")]
    [InlineData("https://example.org/docs/rates%202026.pdf", "application/pdf", "rates-2026.pdf")]
    [InlineData("https://example.org/", "text/html", "example.org.html")]
    [InlineData("https://example.org/page.aspx?id=1", "text/html", "page.aspx")]
    public void FileNameFromUrl(string url, string mediaType, string expected) =>
        Assert.Equal(expected, Fetcher.FileNameFromUrl(new Uri(url), mediaType));

    public void Dispose()
    {
        try { Directory.Delete(directory, true); }
        catch (IOException) { /* best effort */ }
    }
}
