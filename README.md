# Payroll Engine Lex

> Part of the [Payroll Engine](https://github.com/Payroll-Engine/PayrollEngine) open-source payroll automation framework.
> Full documentation at [payrollengine.org](https://payrollengine.org).

Library for the provenance of compliance values in data regulations. A `lex.json` manifest documents where each
value comes from (source URLs, location, legal reference, evidence files); a `snapshot.json` freezes the manifest
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
- Lookup of the entry numbers in the source document, with rating of weak matches, source quotes and sampling of
  large tables (`SourceVerifier`, `SourceDocument`)
- Release gate: entry-specific locations, official main sources, no weak automatic matches, no zero values
  without justification (`Checker`)
- Transfer of the lex values into the lookup values of the regulation (`Applier`)
- Rollover of a satellite to the next cycle (`Rollover`)

Besides the .NET base class library, the library uses [PdfPig](https://github.com/UglyToad/PdfPig) (Apache-2.0)
to read PDF sources and [ExcelDataReader](https://github.com/ExcelDataReader/ExcelDataReader) (MIT) to read
Excel workbooks (see [Third-Party Notices](THIRD-PARTY-NOTICES.md)). It is used by the Lex commands of the
[Payroll Console](https://github.com/Payroll-Engine/PayrollEngine.PayrollConsole)
(`LexFetch`, `LexAdd`, `LexVerifySource`, `LexAttest`, `LexSnapshot`, `LexVerify`, `LexCheck`, `LexApply`,
`LexRollover`).

---

## Sources

An entry lists its sources in `sourceUrls`; the first URL is the main source. A value whose numbers are spread over
several documents (e.g. a rate table and a separate ceiling notice) lists all of them. `SourceVerifier` reads every
source and counts a number as found when any readable source holds it; an unreadable source is reported, but does
not hide the numbers found in the others. A file in `files[]` also lists its `sourceUrls`: `SnapshotWriter`
downloads it from the first URL that answers. A source with `"official": false` is a secondary source; as main
source it is reported by the checker (a finding in the release mode).

## Evidence Quality

`SourceVerifier.Verify` returns a `SourceMatch`. A single number is rated `Ambiguous` instead of `Found` when the
source writes it with at most two digits (`22%`, `1.5`), writes it with at most three digits in more than three
sections, or holds it in words only (`$1 million`, `one and one-half`, `2.5 lakh`). A value below 1 is also looked
up as percentage when the source writes `%`, `percent`, `per cent` or `per centum` after the number, except for units that are no rate
(`factor`, `hours`, `days`, currency codes). The unit `percent points` (also `percentage points`) declares a
source that writes the rate as percentage without percent sign, a rate table under a heading
(`Tipos de cotización … 2,70` for `0.027`): the value is then also looked up as the plain number. A `sourceQuote` anchors the numbers it holds; the other numbers of
the entry are looked up in all sources like without quote, so a bundle over several sources can anchor single
values. A quote without a number of the entry locates the section that must hold the numbers. A quote missing in
the source gives `QuoteNotFound`.

Sources often hold tables over several years, so a number of another year may be "found". With the year of the
entry — its `referenceYear`, otherwise the year of the manifest `validFrom` — each hit of a number is rated by the
**closest year** in the text: the nearest four-digit year (1990–2100, not part of a longer number, an amount or a
percentage) within 120 characters before or after the hit, considering only the years from the entry year minus 3
to plus 1. An enumeration or range of years counts as one year group: `2024, 2025, or 2026`, `2024 and 2025`,
`2024-2026`, `2024 through 2026`. A hit is

- **positive** when the closest year (group) holds the entry year (at the same distance this group wins),
- **weak** when the closest year is another year (`$52,400 in 2024` for validFrom 2025),
- **neutral** without a year in the window; other years elsewhere on the page (`forms filed for 2019 or earlier`)
  do not matter. A validity start up to the entry year is neutral too, it neither weakens nor confirms:
  `effective January 1, 2025`, `beginning on or after January 1, 2025`, `2025 and after`; as is the year a value is
  unchanged from: `unchanged from 2024`, `same as 2024`.

A year of the section heading (`2024 Rates`) counts as a year at the start of the section text.

A number with a weak and without a positive hit rates the entry `Ambiguous` with
`multi-year section: add sourceQuote with the year` — a neutral hit does not confirm it, a value of a wrong year
is the greater risk. For a bundle the reason lists the affected numbers, e.g. `(0.0031, 148200)`. With a
`sourceQuote` only the quote is rated for the numbers it holds: a quote with the entry year or a year group holding it
(`2025: employee rate 0.19%`) or without any year (statute text) anchors them, a quote whose number stands
next to another year is year-weak; the other numbers of the entry are rated in the sources. A four-digit number within a number row is no year: another number within
12 characters on both sides without a word, colon or parenthesis in between (wage bracket table
`1965 1995 32.50`), as is a number after a currency sign (`$ 2025`). A value that legally belongs to another year than the cycle (a
surcharge `for 2025` in the 2026 satellite) sets `referenceYear` (e.g. `2025`): it replaces the validFrom year in
the year rating and for the quote, and `LexCheck` lists such entries as information. `referenceYear` is part of
the snapshot hash and is kept by `LexRollover`. Years of a legal source reference are ignored: directly
after `Notice`, `Pub.` or `Rev.` (also `Rev. Guide 2024`, `Rev. 12-2025`), in a document number with hyphen or slash
(`2024-40`, `2025/1234`, also ISO dates), in parentheses after a statute reference (`Bill 951 (2022)`) and in the
citations listed in [Languages, Scripts and Periods](#languages-scripts-and-periods).
Print and typesetting marks are ignored too, a date with a time (`15:14 - 3-Dec-2025`, `16:58 Oct 31, 2025`):
a document is printed before the year it is valid for. A numeric date with dots (`17.09.2026`, `ab 01.01.2026`)
counts with its year, also in a table row; after an enactment word it is a citation (`Verordnung vom 20.12.2024`).

### Languages, Scripts and Periods

The year rating and the number lookup read the words of a language from a language file `{language}.json`
(`LanguageSet`). The package ships English, German, French, Spanish, Italian, Dutch and Portuguese in the folder
`LexVocabulary` next to the library. A folder of additional language files, given by the caller
(`SourceVerifier.VerifyManifest(…, languageDirectory)`, `LanguageSet.Get(languages, additionalDirectory)`; in
pecmd `LexVerifySource Vocabulary:<folder>` or the environment variable `PAYROLL_ENGINE_LEX_VOCABULARY`), adds a
language or replaces a package file with the same language code, so a new language needs no new library
version. The library reads no environment variable or configuration of its own. A manifest selects the languages of its sources with `languages` (`["de", "fr"]`); without it all
available languages apply as one union, so a source mixing languages (an English summary of a Dutch decree) is
read the same way. Words of a language apply only in their position (a connector between two years, a validity
phrase before a year), so a short word of one language does not change a text of another language.

A language file lists word arrays; every array is optional, an unknown property is an error:

```json
{
  "language": "da",
  "name": "Danish",
  "percentWords": ["procent"],
  "scaleWords": [{ "word": "mio.", "factor": 1000000 }],
  "halfWords": [], "oneAndHalfWords": [], "doubleWords": [],
  "enumerationWords": ["og", "eller"],
  "rangeWords": ["til"],
  "validityBefore": ["med virkning fra"], "datedValidityBefore": [], "validityAfter": [],
  "unchanged": ["uændret"],
  "enactmentWords": ["lov", "bekendtgørelse"], "enactmentEndings": ["loven"],
  "titleWords": [], "gazettes": [], "dateLinks": ["af"],
  "months": ["januar", "februar", "marts"]
}
```

`titleWords` and `gazettes` are case-sensitive, all other words are not. Each result names the languages and
the hash of their words (`SourceResult.Languages`, e.g. `de,en 3f2a9c1b`); the automatic verification record
writes them into its method (`LexVerifySource 1.2.0; languages de,en 3f2a9c1b`), so a changed word list
renews the record on the next run and every result can be reproduced with the same language files.
The manifest validation (`LexCheck`, `LexSnapshot`, `LexApply`) reports an invalid or duplicate language code;
a language without language file stops `LexVerifySource` for the manifest.

| Topic | Examples |
|:--|:--|
| Percent words | `percent`, `Prozent`, `pour cent`, `por ciento`, `per cento`, `procent`, `por cento` |
| Scale words | `million`, `Mio.`, `Mrd.`, `millions`, `millones`, `milioni`, `miljoen`, `milhões`, `lakh`, `crore` |
| Year groups | `2024 und 2025`, `2024 et 2025`, `2024 bis 2026`, `de 2024 a 2026`, `2024 t/m 2026`, `2024 até 2026` |
| Validity starts | `ab 2025`, `gültig ab 1. Januar 2025`, `à compter du 1er janvier 2025`, `a partir de 2025`, `a decorrere dal 2025`, `met ingang van 2025`, `with effect from 1 April 2025`, `w.e.f.`, `from 1 April 2025` |
| Unchanged values | `unverändert seit 2024`, `inchangé depuis 2024`, `sin cambios desde 2024`, `invariato dal 2024`, `ongewijzigd sinds 2024`, `inalterado desde 2024` |

Citations whose year is the year of the enactment, not of the value, are ignored:

- numbers with a slash after a number mark or enactment word: `Gesetz Nr. 12/2025`, `Loi n° 2024-1234`, `Real Decreto 1/2025`
- `No. 30 of 2025`, `Act 43 of 1991`, `(30 of 2025)` and margin notes `30 of 2025.`
- enactment titles with their year: `Finance Act, 2025`, `Income-tax Rules, 1962`, `INCOME-TAX ACT, 2025`
- gazettes: `BGBl. I 2025`, `BOE 2025`, `Stb. 2025`, `G.U. 2025`
- enactments with their date: `Gesetz vom 23. Dezember 2024`, `Lei nº 15.191, de 11 de agosto de 2025`, `décret du 30 décembre 2024`

A date after a validity phrase stays a validity start, not a citation.

**Tax years.** A manifest whose `validFrom` is not 1 January covers a tax year with the start year of `validFrom`
(`2025-04-01` → tax year 2025, India, Australia, United Kingdom); `referenceYear` is the start year too. In such a
manifest periods are rated: `2025-26`, `2025/26`, `2025-2026`, `2025 to 2026`, `FY 2025-26`, `FY26`,
`financial year 2025-26`, `income year 2025–26`, and the assessment year as the following year
(`AY 2026-27`, `YA 2026`, `assessment year 2026-27` = tax year 2025). A period starting in the entry year is
positive, a period of another start year is weak. A single year or a year group with the start year or the year
after it is neutral (a tax year 2025-26 ends in 2026), any other year is weak. A validity start up to the start
year is neutral as for calendar years. In a manifest from 1 January periods are no years, except two full
consecutive years without prefix (`2024-2025`) which count as a year group.

**Scripts.** Digits of other scripts (Devanagari, Arabic-Indic, Bengali, fullwidth) and the Arabic decimal and
thousands separators and percent signs are mapped to ASCII before the lookup. The mapping keeps the length of
the text, so the hit positions and the section names refer to the original text; the normalized text of a source
keeps the original script. A four-digit amount with a currency sign is no year: before (`$`, `€`, `£`, `¥`, `₹`,
`Rs.`, `R$`, `CHF`, `Fr.`, `kr`) or after it (`2025 €`, `2025 EUR`, `2025 CHF`, `2025 ₹`).

Limits: words of a language without language file (Arabic, Hindi, Chinese) are not recognized: add a language
file, use the official English version of the source if there is one, otherwise a `sourceQuote` or `LexAttest`. Amendment
histories ("Amendments 2025 — substituted $6,400") and bare years of a legislative session stay year context.

Tables with more than 40 numbers are sampled evenly. Entries with an
`autoVerification` exclusion are never written (`SourceResult.CanWrite`). A `derived` exclusion
(`Lookup.Key.Property`, formula and legal reference as reason) removes the numbers of a computed property from the
lookup; the other numbers are verified and the `auto` record lists the property (`Verification.Derived`,
`SourceResult.Derived`). A property of a bundle with the value 0 (the rule does not apply, e.g. `"DisabilityRate": 0`)
holds no number to look up: `SourceResult.Zero` lists these properties. A `notApplicable` exclusion
(`Lookup.Key.Property`, the reason says why the rule does not apply) documents such a property
(`SourceResult.NotApplicable`); a zero property without it is listed in `SourceResult.UnjustifiedZero`, so a rate
set to 0 by mistake does not stay unnoticed (the checker reports it, a finding in the release mode).
`SourceVerifier.VerifyManifest` checks
all entries of a manifest, inline content and JSON `contentFile` tables, against the local evidence or a live loader.
The local evidence of an entry is its `documentRef`, otherwise the declared file with the same source URL.
`Checker.UnusedEvidence` lists the evidence files that back no entry: a declared file without `documentRef`, source
URL, `contentFile` or verification evidence pointing to it, and a file in `.lex` that is not declared in `files[]`.
Such a file is never read, its entries count as without local evidence (the checker reports it, a finding in the
release mode).

`SourceResult.Location` names at most three sections per source. Each source is named by its evidence file, or by
the host when the document has no file name (live source): `XX-2026-guide.pdf: Sections "Page 24", "Page 25";
XX-2026-limits.pdf: Section "Page 1"`. A location longer than 500 characters is not proposed.
`SourceVerifier.IsToolLocation` recognizes these locations, so `/refreshlocations` may replace them.

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
var result = Checker.Check(manifest, lexPath, requireLocation: false, requireVerification: false);
CheckReport.Write([result], "LexCheck.md");

// release gate: verify the entries against the local evidence
var release = Checker.Check(manifest, lexPath, requireLocation: true, requireVerification: true, release: true);
await Checker.CheckSources(manifest, release);

// look up the numbers of all entries in the local evidence
var results = await SourceVerifier.VerifyManifest(manifest, lexDirectory);

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
editor.AddValue(new Entry
{
    Key = "Rates.Standard", Content = "0.081",
    SourceUrls = ["https://example.org/rates", "https://example.org/rates-notice-2026.pdf"]
});
editor.SetManualVerification("Rates.Standard", new Verification
{
    Mode = "manual", VerifiedOn = "2026-11-03", VerifiedBy = "Jane Doe", Evidence = "tax-rates-2026.pdf"
}, overwrite: false);
await editor.SaveAsync(lexPath);
```

### Source Documents

`SourceDocument` detects the format from the content, not from the file extension or the media type, and splits
the text into sections:

| Format | Sections |
|:--|:--|
| HTML | One per document heading |
| Text (`.txt`, `.csv`) | One section |
| Spreadsheets (`.xls`, `.xlsx`, `.xlsb`, `.ods`) | One per sheet, numbers in invariant format |
| PDF | One per page (text layer), plus the values of the form fields |
| Text documents (`.docx`, `.odt`) | One per heading; a table row is one line |

A PDF without text layer (scan) or with a user password is unreadable — there is no OCR. Other binary content is
unsupported. Readers for further formats are passed by file extension; a passed reader replaces the built-in reader
of the same format. A reader returns the text sections of the document:

```csharp
var readers = new Dictionary<string, Func<Stream, List<(string Heading, string Text)>>>
{
    [".rtf"] = MyRtfReader.Read
};
var sources = new List<(string Url, SourceDocument Document)>();
foreach (var url in entry.Urls)
{
    sources.Add((url, await SourceDocument.LoadLive(url, httpClient, readers)));
}
var match = SourceVerifier.Verify(entry, sources);
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

This project is licensed under the [MIT License](LICENSE). Third-party components and their licenses are listed in
[THIRD-PARTY-NOTICES.md](THIRD-PARTY-NOTICES.md).

---

## See Also

- [Payroll Console](https://github.com/Payroll-Engine/PayrollEngine.PayrollConsole) — Lex commands built on top of this library
- [Core](https://github.com/Payroll-Engine/PayrollEngine.Core) — Payroll Engine core library
