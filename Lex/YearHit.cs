using System.Collections.Generic;

// ReSharper disable CommentTypo
// ReSharper disable GrammarMistakeInComment

namespace PayrollEngine.Lex;

/// <summary>
/// Year, year group (enumeration, range) or tax year period of a text with its position
/// </summary>
internal sealed class YearHit(List<int> years, int index, int length, bool validity = false,
    int? periodStart = null, bool plain = false, int? month = null, int? day = null)
{
    /// <summary>Month of a date with the year (<c>January 2026</c>, <c>17.09.2026</c>), null without date</summary>
    internal int? Month => month;

    /// <summary>Day of a date with the year, null without day</summary>
    internal int? Day => day;

    /// <summary>Years of the hit, one year or the years of a group; start and end year of a period</summary>
    internal List<int> Years => years;

    /// <summary>Text position of the first year</summary>
    internal int Index => index;

    /// <summary>Text length up to the end of the last year</summary>
    internal int Length => length;

    /// <summary>Start of a validity (<c>effective January 1, 2025</c>, <c>2025 and after</c>) or a value
    /// unchanged from the year (<c>unchanged from 2024</c>)</summary>
    internal bool Validity => validity;

    /// <summary>Start year of a tax year period (<c>2025-26</c>, <c>FY 2025-26</c>, <c>AY 2026-27</c>: 2025),
    /// null for a year or a year group</summary>
    internal int? PeriodStart => periodStart;

    /// <summary>A period of two full consecutive years without prefix (<c>2025-2026</c>, <c>2025/2026</c>,
    /// <c>2025 to 2026</c>): a year group for a calendar year</summary>
    internal bool Plain => plain;

    /// <summary>Group with a further year that ends at the text position</summary>
    internal YearHit Join(int year, int end) => new([.. years, year], index, end - index, validity);

    /// <summary>The year at the start of the section text (year of the section heading)</summary>
    internal YearHit AtStart() => new(years, 0, 0, validity, periodStart, plain, month, day);
}
