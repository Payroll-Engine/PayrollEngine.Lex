using System.Collections.Generic;
using System.Linq;

// ReSharper disable CommentTypo
// ReSharper disable GrammarMistakeInComment

namespace PayrollEngine.Lex;

/// <summary>Text section of a source document with its numbers</summary>
internal sealed class SourceSection(string url, string name, string heading, string text, LanguageSet languages)
{
    /// <summary>URL of the source</summary>
    internal string Url => url;
    /// <summary>Document name</summary>
    internal string Name => name;
    /// <summary>Section heading</summary>
    internal string Heading => heading;
    /// <summary>Numbers of the section text</summary>
    internal SourceNumbers Numbers { get; } = SourceNumbers.Scan(text, languages);
    /// <summary>Normalized text for quote matching, built on first use</summary>
    internal string CanonicalText => field ??= SourceLocation.Canonical(text);
    private List<YearHit> TextYears => field ??=
        [.. YearAnalyzer.YearHits(heading, languages).Select(x => x.AtStart()), .. YearAnalyzer.YearHits(text, languages)];

    /// <summary>Year context of a hit in the section text</summary>
    internal HitYear YearContext((int Index, int Length) hit, RelevantPeriod period) =>
        YearAnalyzer.Classify(hit, TextYears, period);
}

// manifest -----------------------------------------------------------------
