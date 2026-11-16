using System.Net;
using Xunit;

namespace PayrollEngine.Lex.Tests;

public sealed class FetcherTests : IDisposable
{
    private const string ReservedMessageStart = "Reserved file name";

    private readonly string root = Path.Combine(Path.GetTempPath(), "lexfetch-" + Guid.NewGuid().ToString("N"));
    private readonly string directory;

    public FetcherTests()
    {
        directory = Path.Combine(root, ".lex");
        Directory.CreateDirectory(directory);
    }

    private static HttpClient Client(string body = "rates 8.5", HttpStatusCode status = HttpStatusCode.OK,
        string mediaType = "application/pdf") => new(new BodyHandler(status, body, mediaType));

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
    [InlineData("lex.json")]
    [InlineData("snapshot.json")]
    [InlineData("LEX.JSON")]
    [InlineData("Snapshot.Json")]
    [InlineData("lex.json.")]
    [InlineData("lex.json ")]
    [InlineData("snapshot.json. ")]
    public async Task Fetch_ReservedExplicitFileName_IsRejectedBeforeTheRequest(string fileName)
    {
        var manifest = WriteManifest();
        var handler = new BodyHandler(HttpStatusCode.OK, "{ \"replaced\": true }", "application/json");

        var exception = await Assert.ThrowsAsync<ReservedFileNameException>(() =>
            Fetcher.FetchAsync(new HttpClient(handler), "https://example.org/rates.pdf", directory, fileName, overwrite: true));

        Assert.Equal("fileName", exception.ParamName);
        Assert.Equal(fileName, exception.FileName);
        Assert.StartsWith(ReservedMessageStart, exception.Message, StringComparison.Ordinal);
        Assert.Equal(0, handler.Requests);
        Assert.Equal(manifest, await File.ReadAllBytesAsync(Path.Combine(directory, "lex.json")));
        Assert.False(File.Exists(Path.Combine(directory, "snapshot.json")));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("https://example.org/data/snapshot.json", "snapshot.json")]
    [InlineData("https://example.org/data/lex", "lex.json")]
    [InlineData("https://example.org/data/LEX.JSON", "LEX.JSON")]
    public async Task Fetch_ReservedDerivedFileName_IsRejectedBeforeWriting(string url, string derivedName)
    {
        var manifest = WriteManifest();
        var handler = new BodyHandler(HttpStatusCode.OK, "{ \"replaced\": true }", "application/json");

        var exception = await Assert.ThrowsAsync<ReservedFileNameException>(() =>
            Fetcher.FetchAsync(new HttpClient(handler), url, directory, overwrite: true));

        Assert.Equal("fileName", exception.ParamName);
        Assert.Equal(derivedName, exception.FileName);
        Assert.StartsWith(ReservedMessageStart, exception.Message, StringComparison.Ordinal);
        Assert.Equal(1, handler.Requests);
        Assert.Equal(manifest, await File.ReadAllBytesAsync(Path.Combine(directory, "lex.json")));
        Assert.False(File.Exists(Path.Combine(directory, "snapshot.json")));
        Assert.Single(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("rates.pdf.")]
    [InlineData("rates.pdf ")]
    public async Task Fetch_ExplicitFileNameWithTrailingDotOrSpace_IsRejected(string fileName)
    {
        var handler = new BodyHandler(HttpStatusCode.OK, "rates 8.5", "application/pdf");

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            Fetcher.FetchAsync(new HttpClient(handler), "https://example.org/rates.pdf", directory, fileName));

        Assert.Equal("fileName", exception.ParamName);
        Assert.Equal(0, handler.Requests);
        Assert.Empty(Directory.GetFiles(directory));
    }

    [Theory]
    [InlineData("lex.json", true)]
    [InlineData("SNAPSHOT.JSON", true)]
    [InlineData("lex.json. .", true)]
    [InlineData("lex.json.pdf", false)]
    [InlineData("snapshot.json.bak", false)]
    [InlineData("lex", false)]
    [InlineData("rates.pdf", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsReservedFileName(string fileName, bool expected) =>
        Assert.Equal(expected, Fetcher.IsReservedFileName(fileName));

    [Fact]
    public async Task Fetch_TargetGuard_GetsTheFullPathBeforeWriting()
    {
        string guarded = null;
        var existedAtGuard = true;

        var result = await Fetcher.FetchAsync(Client(), "https://example.org/rates.pdf", directory,
            fileName: null, overwrite: false, targetGuard: path =>
            {
                guarded = path;
                existedAtGuard = File.Exists(path);
            });

        Assert.Equal(FetchState.Created, result.State);
        Assert.Equal(Path.GetFullPath(Path.Combine(directory, "rates.pdf")), guarded);
        Assert.False(existedAtGuard);
    }

    [Fact]
    public async Task Fetch_TargetGuardThrows_WritesNothing()
    {
        await Fetcher.FetchAsync(Client(), "https://example.org/rates.pdf", directory);

        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Fetcher.FetchAsync(Client("rates 9.0"), "https://example.org/rates.pdf", directory, null, true,
                _ => throw new UnauthorizedAccessException("outside")));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() =>
            Fetcher.FetchAsync(Client(), "https://example.org/other.pdf", directory, fileName: null, overwrite: false,
                targetGuard: _ => throw new UnauthorizedAccessException("outside")));

        Assert.Equal("rates 8.5", await File.ReadAllTextAsync(Path.Combine(directory, "rates.pdf")));
        Assert.False(File.Exists(Path.Combine(directory, "other.pdf")));
    }

    [Fact]
    public async Task Fetch_NullTargetGuard_Writes()
    {
        var result = await Fetcher.FetchAsync(Client(), "https://example.org/rates.pdf", directory, null, false, null);

        Assert.Equal(FetchState.Created, result.State);
        Assert.Equal("rates 8.5", await File.ReadAllTextAsync(Path.Combine(directory, "rates.pdf")));
    }

    [Fact]
    public void FetchAsync_SignatureWithoutTargetGuard_IsPublic()
    {
        var method = typeof(Fetcher).GetMethod(nameof(Fetcher.FetchAsync),
            [typeof(HttpClient), typeof(string), typeof(string), typeof(string), typeof(bool)]);

        Assert.NotNull(method);
        Assert.True(method.IsPublic);
        Assert.True(method.IsStatic);
        Assert.Equal(typeof(Task<FetchResult>), method.ReturnType);
        Assert.True(method.GetParameters()[3].IsOptional);
        Assert.True(method.GetParameters()[4].IsOptional);
    }

    [Fact]
    public void FetchAsync_SignatureWithTargetGuard_IsPublic()
    {
        var method = typeof(Fetcher).GetMethod(nameof(Fetcher.FetchAsync),
            [typeof(HttpClient), typeof(string), typeof(string), typeof(string), typeof(bool), typeof(Action<string>)]);

        Assert.NotNull(method);
        Assert.True(method.IsPublic);
        Assert.All(method.GetParameters(), parameter => Assert.False(parameter.IsOptional));
    }

    [Fact]
    public async Task Fetch_SignatureWithoutTargetGuard_KeepsBehavior()
    {
        var manifest = WriteManifest();
        var handler = new BodyHandler(HttpStatusCode.OK, "rates 8.5", "application/pdf");
        var client = new HttpClient(handler);
        const string fileName = "rates.pdf";
        const bool overwrite = false;

        // ReSharper disable once RedundantArgumentDefaultValue
        var created = await Fetcher.FetchAsync(client, "https://example.org/x", directory, fileName, overwrite);
        // ReSharper disable once RedundantArgumentDefaultValue
        var unchanged = await Fetcher.FetchAsync(client, "https://example.org/x", directory, fileName, overwrite);
        var exception = await Assert.ThrowsAsync<ReservedFileNameException>(() =>
            Fetcher.FetchAsync(client, "https://example.org/x", directory, "lex.json", true));

        Assert.Equal(FetchState.Created, created.State);
        Assert.Equal(Path.Combine(directory, fileName), created.FilePath);
        Assert.Equal(FetchState.Unchanged, unchanged.State);
        Assert.Equal("fileName", exception.ParamName);
        Assert.Equal(2, handler.Requests);
        Assert.Equal(manifest, await File.ReadAllBytesAsync(Path.Combine(directory, "lex.json")));
    }

    [Fact]
    public async Task Fetch_ReservedFileName_IsCaughtAsArgumentException()
    {
        var exception = await Assert.ThrowsAnyAsync<ArgumentException>(() =>
            Fetcher.FetchAsync(Client(), "https://example.org/rates.pdf", directory, "snapshot.json", false, null));

        Assert.IsType<ReservedFileNameException>(exception);
        Assert.Equal("fileName", exception.ParamName);
        Assert.StartsWith(ReservedMessageStart, exception.Message, StringComparison.Ordinal);
    }

    [SymbolicLinkFact]
    public Task Fetch_LinkToManifest_IsRejected() =>
        AssertLinkToManifestIsRejected(overwrite: false);

    [SymbolicLinkFact]
    public Task Fetch_LinkToManifest_WithOverwrite_IsRejected() =>
        AssertLinkToManifestIsRejected(overwrite: true);

    private async Task AssertLinkToManifestIsRejected(bool overwrite)
    {
        var manifest = WriteManifest();
        var link = Path.Combine(directory, "evidence.pdf");
        File.CreateSymbolicLink(link, Path.Combine(directory, "lex.json"));
        var handler = new BodyHandler(HttpStatusCode.OK, "{ \"replaced\": true }", "application/json");
        var guarded = false;

        // explicit name: refused before the request
        var explicitName = await Assert.ThrowsAsync<FetchTargetLinkException>(() =>
            Fetcher.FetchAsync(new HttpClient(handler), "https://example.org/rates.pdf", directory,
                "evidence.pdf", overwrite, _ => guarded = true));
        // name from the URL: refused after the request, before the target is read
        var derivedName = await Assert.ThrowsAsync<FetchTargetLinkException>(() =>
            Fetcher.FetchAsync(new HttpClient(handler), "https://example.org/docs/evidence.pdf", directory,
                null, overwrite, _ => guarded = true));

        Assert.All(new[] { explicitName, derivedName }, exception =>
        {
            Assert.Equal("evidence.pdf", exception.FileName);
            Assert.Equal("lex.json", Path.GetFileName(exception.ResolvedPath));
        });
        Assert.Equal(1, handler.Requests);
        Assert.False(guarded);
        Assert.Equal(manifest, await File.ReadAllBytesAsync(Path.Combine(directory, "lex.json")));
        Assert.NotNull(new FileInfo(link).LinkTarget);
        Assert.False(File.Exists(Path.Combine(directory, "snapshot.json")));
        Assert.Equal(2, Directory.GetFileSystemEntries(directory).Length);
    }

    [SymbolicLinkFact]
    public async Task Fetch_RelativeLinkToSnapshot_IsRejected()
    {
        var snapshot = Path.Combine(directory, "snapshot.json");
        await File.WriteAllTextAsync(snapshot, "{}");
        File.CreateSymbolicLink(Path.Combine(directory, "evidence.pdf"), "snapshot.json");

        var exception = await Assert.ThrowsAsync<FetchTargetLinkException>(() =>
            Fetcher.FetchAsync(Client("replaced"), "https://example.org/evidence.pdf", directory, null, true, null));

        Assert.Equal("snapshot.json", Path.GetFileName(exception.ResolvedPath));
        Assert.Equal("{}", await File.ReadAllTextAsync(snapshot));
    }

    [SymbolicLinkFact]
    public async Task Fetch_LinkToFileOutsideTheFolder_IsRejected()
    {
        var outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
        var target = Path.Combine(outside, "secret.txt");
        await File.WriteAllTextAsync(target, "secret");
        File.CreateSymbolicLink(Path.Combine(directory, "evidence.pdf"), target);

        foreach (var overwrite in new[] { false, true })
        {
            var exception = await Assert.ThrowsAsync<FetchTargetLinkException>(() =>
                Fetcher.FetchAsync(Client(), "https://example.org/evidence.pdf", directory, null, overwrite, null));
            Assert.Equal("evidence.pdf", exception.FileName);
            Assert.Equal("secret.txt", Path.GetFileName(exception.ResolvedPath));
        }
        Assert.Equal("secret", await File.ReadAllTextAsync(target));
    }

    [SymbolicLinkFact]
    public async Task Fetch_LinkToSiblingFolderWithSamePrefix_IsRejected()
    {
        // '.lex-other' starts with the name of the '.lex' folder
        var sibling = Directory.CreateDirectory(directory + "-other").FullName;
        var target = Path.Combine(sibling, "rates.pdf");
        await File.WriteAllTextAsync(target, "other");
        File.CreateSymbolicLink(Path.Combine(directory, "rates.pdf"), target);

        await Assert.ThrowsAsync<FetchTargetLinkException>(() =>
            Fetcher.FetchAsync(Client(), "https://example.org/rates.pdf", directory, "rates.pdf", true, null));

        Assert.Equal("other", await File.ReadAllTextAsync(target));
    }

    [SymbolicLinkFact(UnixOnly = true)]
    public async Task Fetch_LinkToFolderDifferingInCase_IsRejected()
    {
        // on a case-sensitive file system '.LEX' is another folder
        var upper = Directory.CreateDirectory(Path.Combine(root, ".LEX")).FullName;
        var target = Path.Combine(upper, "rates.pdf");
        await File.WriteAllTextAsync(target, "other");
        File.CreateSymbolicLink(Path.Combine(directory, "rates.pdf"), target);

        await Assert.ThrowsAsync<FetchTargetLinkException>(() =>
            Fetcher.FetchAsync(Client(), "https://example.org/rates.pdf", directory, "rates.pdf", true, null));

        Assert.Equal("other", await File.ReadAllTextAsync(target));
    }

    [SymbolicLinkFact]
    public async Task Fetch_LinkToFileInTheFolder_BehavesAsBefore()
    {
        var real = Path.Combine(directory, "real.pdf");
        await File.WriteAllTextAsync(real, "rates 8.5");
        var alias = Path.Combine(directory, "evidence-alias.pdf");
        File.CreateSymbolicLink(alias, "real.pdf");
        string guarded = null;

        var unchanged = await Fetcher.FetchAsync(Client(), "https://example.org/rates.pdf", directory,
            "evidence-alias.pdf", false, path => guarded = path);
        var differs = await Fetcher.FetchAsync(Client("rates 9.0"), "https://example.org/evidence-alias.pdf", directory,
            null, false, null);
        var replaced = await Fetcher.FetchAsync(Client("rates 9.0"), "https://example.org/rates.pdf", directory,
            "evidence-alias.pdf", true, null);

        Assert.Equal(FetchState.Unchanged, unchanged.State);
        Assert.Equal(Path.GetFullPath(alias), guarded);
        Assert.Equal(FetchState.Differs, differs.State);
        Assert.Equal(FetchState.Replaced, replaced.State);
        Assert.Equal(alias, replaced.FilePath);
        Assert.Equal("rates 9.0", await File.ReadAllTextAsync(real));
        Assert.NotNull(new FileInfo(alias).LinkTarget);
    }

    [SymbolicLinkFact]
    public async Task Fetch_BrokenLink_IsRejected()
    {
        File.CreateSymbolicLink(Path.Combine(directory, "evidence.pdf"), "missing.pdf");

        var exception = await Assert.ThrowsAsync<FetchTargetLinkException>(() =>
            Fetcher.FetchAsync(Client(), "https://example.org/rates.pdf", directory, "evidence.pdf", true, null));

        Assert.Equal("evidence.pdf", exception.FileName);
        Assert.False(File.Exists(Path.Combine(directory, "missing.pdf")));
    }

    [SymbolicLinkFact]
    public async Task Fetch_LexFolderIsLink_TargetsStayInTheRealFolder()
    {
        var manifest = WriteManifest();
        var linkedFolder = Path.Combine(root, "linked");
        Directory.CreateSymbolicLink(linkedFolder, directory);
        var outside = Directory.CreateDirectory(Path.Combine(root, "outside")).FullName;
        await File.WriteAllTextAsync(Path.Combine(outside, "secret.txt"), "secret");
        await File.WriteAllTextAsync(Path.Combine(directory, "real.pdf"), "rates 8.5");
        // link targets through the folder link
        File.CreateSymbolicLink(Path.Combine(directory, "evidence-alias.pdf"), Path.Combine(linkedFolder, "real.pdf"));
        File.CreateSymbolicLink(Path.Combine(directory, "evidence.pdf"), Path.Combine(linkedFolder, "lex.json"));
        File.CreateSymbolicLink(Path.Combine(directory, "evidence-out.pdf"), Path.Combine(outside, "secret.txt"));

        var created = await Fetcher.FetchAsync(Client(), "https://example.org/rates.pdf", linkedFolder);
        var alias = await Fetcher.FetchAsync(Client(), "https://example.org/rates.pdf", linkedFolder,
            "evidence-alias.pdf", false, null);
        var manifestLink = await Assert.ThrowsAsync<FetchTargetLinkException>(() =>
            Fetcher.FetchAsync(Client("replaced"), "https://example.org/rates.pdf", linkedFolder, "evidence.pdf", true, null));
        var outsideLink = await Assert.ThrowsAsync<FetchTargetLinkException>(() =>
            Fetcher.FetchAsync(Client("replaced"), "https://example.org/rates.pdf", linkedFolder, "evidence-out.pdf", true, null));

        Assert.Equal(FetchState.Created, created.State);
        Assert.True(File.Exists(Path.Combine(directory, "rates.pdf")));
        Assert.Equal(FetchState.Unchanged, alias.State);
        Assert.Equal("lex.json", Path.GetFileName(manifestLink.ResolvedPath));
        Assert.Equal("secret.txt", Path.GetFileName(outsideLink.ResolvedPath));
        Assert.Equal(manifest, await File.ReadAllBytesAsync(Path.Combine(directory, "lex.json")));
        Assert.Equal("secret", await File.ReadAllTextAsync(Path.Combine(outside, "secret.txt")));
    }

    /// <summary>Writes a manifest into the test folder and returns its bytes</summary>
    private byte[] WriteManifest()
    {
        var bytes = "{ \"validFrom\": \"2026-01-01\", \"values\": [] }"u8.ToArray();
        File.WriteAllBytes(Path.Combine(directory, "lex.json"), bytes);
        return bytes;
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
        try { Directory.Delete(root, true); }
        catch (IOException) { /* best effort */ }
    }
}
