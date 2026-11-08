using System.Text;
using System.Text.Json;

namespace PayrollEngine.Lex.Tests;

internal sealed class RolloverSatellite : IDisposable
{
    internal const string RegulationJson = """
        {
          "createdObjectDate": "2026-01-01T00:00:00.0Z",
          "tenants": [
            {
              "identifier": "XX.Test",
              "regulations": [
                {
                  "name": "XX.Test.Data.Test",
                  "description": "Test rates 2026",
                  "validFrom": "2026-01-01T00:00:00Z",
                  "lookups": [
                    {
                      "name": "Rates",
                      "values": [
                        { "key": "A", "value": "8.5" }
                      ]
                    },
                    {
                      "name": "Table",
                      "values": [
                        { "key": "A", "valueObject": { "Max": 100 } }
                      ]
                    }
                  ]
                }
              ]
            }
          ]
        }
        """;

    internal const string LexJson = """
        {
          "authority": "XX",
          "regulation": "Data.Test",
          "validFrom": "2026-01-01",
          "provider": "Test Authority",
          "sources": [ { "url": "https://example.org/doc", "description": "Test source 2026" } ],
          "values": [
            {
              "key": "Rates.A",
              "content": "8.5",
              "sourceUrls": ["https://example.org/doc"],
              "sourceLocation": "p. 1",
              "legalReference": "Test Act s. 1",
              "verification": { "mode": "manual", "verifiedOn": "2026-01-02", "verifiedBy": "Tester", "evidence": "evidence-2026.pdf" }
            },
            {
              "key": "Table.A.2026",
              "contentFile": "Table.A.2026.json",
              "sourceUrls": ["https://example.org/doc"],
              "sourceLocation": "p. 2",
              "legalReference": "Test Act s. 2"
            }
          ],
          "files": [ { "fileName": "evidence-2026.pdf" } ]
        }
        """;

    internal const string Props = """
        <Project>
          <PropertyGroup>
            <Version>2026.1-beta.dev</Version>
            <FileVersion>2026.1.0</FileVersion>
            <Product>Test Data 2026</Product>
          </PropertyGroup>
        </Project>
        """;

    internal const string Package = """
        {
          "packageId": "PayrollEngine.Regulation.XX.Test.Data.Test",
          "version": "2026.1-beta.dev",
          "installFiles": [
            "Regulation/XX.Test.Data.Test.2026.json"
          ]
        }
        """;

    internal static readonly byte[] Evidence = [0x25, 0x50, 0x44, 0x46, 0x00, 0xFF];

    private static readonly JsonSerializerOptions ReadOptions = new() { PropertyNameCaseInsensitive = true };

    private readonly string parent = Path.Combine(Path.GetTempPath(), "lexroll-" + Guid.NewGuid().ToString("N"));

    internal RolloverSatellite(string name = "Data.Test.2026", string regulation = null)
    {
        Root = Path.Combine(parent, name);
        Write("Regulation/XX.Test.Data.Test.2026.json", regulation ?? RegulationJson);
        Write("Directory.Build.props", Props);
        Write("regulation-package.json", Package);
        Write("Setup.pecmd", "PayrollImport Regulation/XX.Test.Data.Test.2026.json\n");
        Write("Regulation.XX.Test.Data.Test.2026.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />\n");
        Write(".lex/lex.json", LexJson);
        Write(".lex/Table.A.2026.json", "{ \"Max\": 100 }");
        Write(".lex/snapshot.json", "{}");
        Write("bin/Debug/Test.2026.dll", "binary");
        Write("obj/project.assets.json", "{}");
        File.WriteAllBytes(Path.Combine(Root, ".lex", "evidence-2026.pdf"), Evidence);
    }

    internal string Root { get; }
    internal string Target => Path.Combine(parent, "Data.Test.2027");

    internal void Write(string relative, string text, bool bom = false, bool crlf = false)
    {
        var path = Path.Combine(Root, relative);
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var lf = text.Replace("\r\n", "\n");
        File.WriteAllText(path, crlf ? lf.Replace("\n", "\r\n") : lf, new UTF8Encoding(bom));
    }

    internal string ReadTarget(string relative) => File.ReadAllText(Path.Combine(Target, relative));

    internal Manifest TargetManifest() =>
        JsonSerializer.Deserialize<Manifest>(ReadTarget(".lex/lex.json"), ReadOptions);

    public void Dispose()
    {
        try { Directory.Delete(parent, true); }
        catch (IOException) { /* best effort */ }
    }
}
