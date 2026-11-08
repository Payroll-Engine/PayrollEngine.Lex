
// ReSharper disable CommentTypo
// ReSharper disable GrammarMistakeInComment

namespace PayrollEngine.Lex;

/// <summary>
/// Year context of a number hit (see <see cref="SourceVerifier.YearContext"/>)
/// </summary>
internal enum HitYear
{
    /// <summary>No year within the window</summary>
    Neutral,

    /// <summary>The closest year is the relevant year, or a year group that holds it</summary>
    Positive,

    /// <summary>The closest year is another year</summary>
    Weak
}
