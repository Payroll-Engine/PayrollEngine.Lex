
// ReSharper disable CommentTypo
// ReSharper disable GrammarMistakeInComment

namespace PayrollEngine.Lex;

/// <summary>
/// Relevant period of the year context: a calendar year, or a tax year that starts in the year (validFrom not on
/// the 1st of January: IN 2026-04-01 is the tax year 2026-27)
/// </summary>
/// <param name="Year">Relevant year: the reference year of the entry or the year of validFrom; the start year of a tax year</param>
/// <param name="Fiscal">The period is a tax year starting in <paramref name="Year"/></param>
/// <param name="StartMonth">Month of the tax year start (validFrom)</param>
/// <param name="StartDay">Day of the tax year start (validFrom)</param>
internal readonly record struct RelevantPeriod(int Year, bool Fiscal, int StartMonth = 1, int StartDay = 1)
{
    /// <summary>
    /// Start year of the tax year that holds a date of the year: the year from the start day on, otherwise the year
    /// before; null when the date has no day in the start month and the tax year does not start on the 1st
    /// </summary>
    internal int? TaxYearOf(int year, int month, int? day)
    {
        if (month != StartMonth)
        {
            return month > StartMonth ? year : year - 1;
        }
        if (StartDay == 1)
        {
            return year;
        }
        return day == null ? null : day >= StartDay ? year : year - 1;
    }
}
