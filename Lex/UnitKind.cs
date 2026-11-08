using System;
using System.Linq;
using System.Text.RegularExpressions;

// ReSharper disable CommentTypo
// ReSharper disable GrammarMistakeInComment

namespace PayrollEngine.Lex;

/// <summary>
/// Percent lookup mode of a lex entry unit
/// </summary>
internal static class UnitKind
{
    private static readonly string[] NonRateTokens = ["factor", "hour", "hours", "day", "days"];

    private static readonly Regex UnitToken = new("%|[A-Za-z]+", RegexOptions.Compiled);

    private static readonly Regex CurrencyCode = new("^[A-Z]{3}$", RegexOptions.Compiled);

    private static readonly string[] PercentTokens = ["%", "percent", "percentage"];

    /// <summary>
    /// Lookup of a value below 1 as percentage (0.085 as 8.5%, the source writes a percent sign): always, except
    /// for units that are no rate — <c>factor</c>, <c>hours</c>, <c>days</c> or a currency code (<c>USD</c>,
    /// <c>EUR</c>); a combined unit like <c>USD / rate</c> allows it. The unit <c>percent points</c> (also
    /// <c>percentage points</c>, <c>% points</c>) declares a source that writes the rate as percentage without
    /// percent sign, a rate table under a heading (<c>Tipos de cotización</c>: 2,60 for 0.026)
    /// </summary>
    internal static PercentLookup Percent(string unit)
    {
        if (string.IsNullOrWhiteSpace(unit))
        {
            return PercentLookup.Sign;
        }
        var tokens = UnitToken.Matches(unit).Select(x => x.Value).ToList();
        if (tokens.Contains("points", StringComparer.OrdinalIgnoreCase) &&
            tokens.Any(t => PercentTokens.Contains(t, StringComparer.OrdinalIgnoreCase)))
        {
            return PercentLookup.Points;
        }
        return tokens.Count == 0 || !tokens.All(t =>
            CurrencyCode.IsMatch(t) || NonRateTokens.Contains(t, StringComparer.OrdinalIgnoreCase))
            ? PercentLookup.Sign
            : PercentLookup.None;
    }
}
