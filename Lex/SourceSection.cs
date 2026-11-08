using System.Collections.Generic;
using System.Linq;

// ReSharper disable CommentTypo
// ReSharper disable GrammarMistakeInComment

namespace PayrollEngine.Lex;

internal sealed class SourceSection(string url, string name, string heading, string text, LanguageSet languages)
{
    internal string Url => url;
    internal string Name => name;
    internal string Heading => heading;
    internal SourceNumbers Numbers { get; } = SourceNumbers.Scan(text, languages);
    internal string CanonicalText => field ??= SourceLocation.Canonical(text);
    private List<YearHit> TextYears => field ??=
        [.. YearAnalyzer.YearHits(heading, languages).Select(x => x.AtStart()), .. YearAnalyzer.YearHits(text, languages)];

    /// <summary>Year context of a hit in the section text</summary>
    internal HitYear YearContext((int Index, int Length) hit, RelevantPeriod period) =>
        YearAnalyzer.Classify(hit, TextYears, period);
}

// manifest -----------------------------------------------------------------
