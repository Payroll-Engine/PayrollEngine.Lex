# Payroll Engine Lex

> Part of the [Payroll Engine](https://github.com/Payroll-Engine/PayrollEngine) open-source payroll automation framework.
> Full documentation at [payrollengine.org](https://payrollengine.org).

Library for the provenance of compliance values in data regulations. A `lex.json` manifest documents where each
value comes from (source URL, location, legal reference, evidence files); a `snapshot.json` freezes the manifest
and its evidence with hashes.

The library provides:
- Manifest and snapshot model (`Manifest`, `Entry`, `Source`, `EvidenceFile`, `Exclusion`, `Snapshot`)
- JSON schema of `lex.json` (`Schema`) and structural validation (`Validator`)
- Snapshot creation (`SnapshotWriter`) and verification against the manifest and the evidence files (`Verifier`)
- Check of a manifest against the lookup values of its regulation: coverage, value differences, traceability fields,
  evidence files and `validFrom` (`Checker`, `CheckReport`)
- Availability check of the source URLs (`UrlChecker`)
- Lookup of the entry numbers in the source document (`SourceVerifier`, `SourceDocument`)

The library depends on the .NET base class library only. It is used by the Lex commands of the
[Payroll Console](https://github.com/Payroll-Engine/PayrollEngine.PayrollConsole)
(`LexSnapshot`, `LexVerify`, `LexCheck`, `LexVerifySource`).

---

## Project Structure

```
PayrollEngine.Lex/
├── Lex/                          # Library source
├── Lex.Tests/                    # Unit tests
├── Directory.Build.props         # Shared build properties
├── PayrollEngine.Lex.sln         # Solution file
└── README.md
```

---

## Data Regulation Layout

The checks expect the manifest in a `.lex` folder next to the `Regulation` folder of the data regulation:

```
Data.Tax/
├── .lex/
│   ├── lex.json                  # Manifest
│   ├── snapshot.json             # Snapshot (created by SnapshotWriter)
│   └── *.pdf, *.xlsx, ...        # Evidence files
└── Regulation/
    └── *.json                    # Regulation with lookups
```

---

## Usage

```csharp
using PayrollEngine.Lex;

// structural validation
var errors = Validator.Validate(manifest, lexDirectory);

// snapshot: create and verify
var snapshot = await new SnapshotWriter().WriteAsync(manifest, lexDirectory, lexDirectory, dryRun: false);
var differences = Verifier.Verify(manifest, snapshot, lexDirectory);

// check against the regulation lookups
var result = Checker.Check(manifest, lexPath, requireLocation: false);
CheckReport.Write([result], "LexCheck.md");
```

### Source Documents

`SourceDocument` reads HTML and text sources. Readers for further formats are passed by file extension;
a reader returns the text sections of the document:

```csharp
var readers = new Dictionary<string, Func<Stream, List<(string Heading, string Text)>>>
{
    [".xlsx"] = MySpreadsheetReader.Read
};
var document = await SourceDocument.Load(entry.SourceUrl, localPath, httpClient, readers);
var match = SourceVerifier.Verify(entry, document);
```

---

## NuGet Package

```sh
dotnet add package PayrollEngine.Lex
```

---

## Build

Environment variable used during build:

| Variable | Description |
|:--|:--|
| `PayrollEnginePackageDir` | Output directory for the NuGet package (optional) |

---

## License

This project is licensed under the [MIT License](LICENSE).

---

## See Also

- [Payroll Console](https://github.com/Payroll-Engine/PayrollEngine.PayrollConsole) — Lex commands built on top of this library
- [Core](https://github.com/Payroll-Engine/PayrollEngine.Core) — Payroll Engine core library
