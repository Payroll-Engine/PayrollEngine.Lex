# Payroll Engine Lex

> Part of the [Payroll Engine](https://github.com/Payroll-Engine/PayrollEngine) open-source payroll automation framework.
> Full documentation at [payrollengine.org](https://payrollengine.org).

Library for the provenance of compliance values in data regulations. A `lex.json` manifest documents where each
value comes from (source URL, location, legal reference, evidence files); a `snapshot.json` freezes the manifest
and its evidence with hashes.

The library provides:
- Manifest and snapshot model (`Manifest`, `Entry`, `Source`, `EvidenceFile`, `Exclusion`, `Verification`, `Snapshot`)
- JSON schema of `lex.json` (`Schema`) and structural validation (`Validator`)
- Editing of `lex.json` that keeps unknown properties and the property order (`ManifestEditor`): add entries and
  files, set source locations, automatic and manual verification records and reviews
- Download of source documents into the `.lex` folder (`Fetcher`)
- Snapshot creation (`SnapshotWriter`) and verification against the manifest and the evidence files (`Verifier`)
- Check of a manifest against the lookup values of its regulation: coverage, value differences, traceability fields,
  evidence files, `validFrom`, verification records and open entries (`Checker`, `CheckReport`)
- Availability check of the source URLs and detection of changed sources (`UrlChecker`, `LiveSource`)
- Lookup of the entry numbers in the source document (`SourceVerifier`, `SourceDocument`)
- Transfer of the lex values into the lookup values of the regulation (`Applier`)
- Rollover of a satellite to the next cycle (`Rollover`)

The library depends on the .NET base class library only. It is used by the Lex commands of the
[Payroll Console](https://github.com/Payroll-Engine/PayrollEngine.PayrollConsole)
(`LexFetch`, `LexAdd`, `LexVerifySource`, `LexAttest`, `LexSnapshot`, `LexVerify`, `LexCheck`, `LexApply`,
`LexRollover`).

---

## Verification and Open Entries

Each entry can carry a `verification` record: `auto` when a tool found the value in the source
(`SourceVerifier`, method and date), `manual` when a person confirmed it against an evidence document
(person, date, evidence). A source with review rule `FourEyes` requires a second person (`reviewedBy`,
`reviewedOn`) for manual records.

An entry taken from the previous cycle by `Rollover` is **open** (`"open": true`): it has no verification
record until it is verified again, and a new record removes the flag. The checker reports open entries,
the applier skips them. The flag is part of the snapshot hash.

## Rollover

`Rollover.Run` copies the satellite of the previous cycle into a new folder for the cycle start:
the year tag of folder and file names is replaced (a mid-year edition such as `Data.Tax.2026b` rolls to
`Data.Tax.2027`), references in the text files are updated, `validFrom` of manifest and regulation and a
year-based version are set, and all entries become open. Build output and `snapshot.json` are not copied,
an existing target is never overwritten. Values are not changed: lines still mentioning the previous year
are returned for review (`RolloverResult.YearMentions`). The cycle start is passed by the caller, e.g.
from the payroll calendar of a tenant.

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
│   ├── snapshot.json             # Snapshot (created by SnapshotWriter)│   └── *.pdf, *.xlsx, ...        # Evidence files
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
var result = Checker.Check(manifest, lexPath, requireLocation: false, requireVerification: false);
CheckReport.Write([result], "LexCheck.md");

// write the lex values into the regulation lookups (dry run: differences only)
var applied = Applier.Apply(manifest, lexPath, dryRun: true);

// download a source document into the .lex folder
using var client = SourceDocument.CreateHttpClient();
var fetched = await Fetcher.FetchAsync(client, "https://example.org/tax-rates-2026.pdf", lexDirectory);

// next cycle: new satellite folder with open entries
var rollover = Rollover.Run(satelliteDirectory, new DateTime(2027, 1, 1), dryRun: false);
```

### Editing lex.json

`ManifestEditor` changes the manifest document in place and keeps everything it does not touch:

```csharp
var editor = ManifestEditor.Parse(await File.ReadAllTextAsync(lexPath));
editor.AddValue(new Entry { Key = "Rates.Standard", Content = "0.081", SourceUrl = "https://example.org/rates" });
editor.SetManualVerification("Rates.Standard", new Verification
{
    Mode = "manual", VerifiedOn = "2026-11-03", VerifiedBy = "Jane Doe", Evidence = "tax-rates-2026.pdf"
}, overwrite: false);
await editor.SaveAsync(lexPath);
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
