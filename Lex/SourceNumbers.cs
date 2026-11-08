using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
// ReSharper disable CommentTypo

namespace PayrollEngine.Lex;

/// <summary>
/// Numbers of a source text: numerals (every separator interpretation), numerals followed by a percent sign
/// (<c>%</c> or a percent word of a language: <c>percent</c>, <c>per centum</c>, <c>Prozent</c>,
/// <c>pour cent</c>) and numbers in words (scales and fractions of the languages, see
/// <see cref="LanguageSet"/>). Digits of other scripts are read as ASCII digits (see <see cref="SourceScript"/>).
/// </summary>
internal sealed class SourceNumbers
{
    /// <summary>Numerals as normalized decimal values</summary>
    internal HashSet<decimal> Numerals { get; } = [];

    /// <summary>Numerals followed by <c>%</c> or a percent word (<c>percent</c>, <c>Prozent</c>, <c>por ciento</c>)</summary>
    internal HashSet<decimal> Percents { get; } = [];

    /// <summary>Numbers in words: <c>$1 million</c>, <c>2.5 lakh</c>, <c>1,5 Mio.</c>, <c>one-half</c>, <c>time and a half</c></summary>
    internal HashSet<decimal> Words { get; } = [];

    // fewest digits of a numeral as written in the text, without separators: 4,300 has 4, 1.2% has 2
    private readonly Dictionary<decimal, int> numeralDigits = [];
    private readonly Dictionary<decimal, int> percentDigits = [];

    // text positions (index, length) of the hits per value
    private readonly Dictionary<decimal, List<(int Index, int Length)>> numeralHits = [];
    private readonly Dictionary<decimal, List<(int Index, int Length)>> percentHits = [];
    private readonly Dictionary<decimal, List<(int Index, int Length)>> wordHits = [];

    /// <summary>
    /// Text positions of the hits of a number: as numeral, in words or, below 1, as percentage (0.085 as 8.5%)
    /// </summary>
    /// <param name="number">Entry number</param>
    /// <param name="percent">Lookup of a value below 1 as percentage</param>
    internal IEnumerable<(int Index, int Length)> Hits(decimal number, PercentLookup percent)
    {
        var value = Math.Round(number, 6);
        var hits = Enumerable.Empty<(int Index, int Length)>();
        if (numeralHits.TryGetValue(value, out var numerals))
        {
            hits = hits.Concat(numerals);
        }
        if (wordHits.TryGetValue(value, out var words))
        {
            hits = hits.Concat(words);
        }
        if (percent != PercentLookup.None && number < 1m)
        {
            var points = Math.Round(number * 100m, 6);
            if (percentHits.TryGetValue(points, out var percents))
            {
                hits = hits.Concat(percents);
            }
            if (percent == PercentLookup.Points && numeralHits.TryGetValue(points, out var plain))
            {
                hits = hits.Concat(plain);
            }
        }
        return hits.Distinct();
    }

    private static void AddHit(Dictionary<decimal, List<(int Index, int Length)>> hits, decimal value, Match match)
    {
        if (!hits.TryGetValue(value, out var list))
        {
            hits[value] = list = [];
        }
        list.Add((match.Index, match.Length));
    }

    /// <summary>
    /// Numerals and numbers in words
    /// </summary>
    internal HashSet<decimal> All() => [.. Numerals, .. Words];

    /// <summary>
    /// Test if the text holds a number: as numeral, in words or, below 1, as percentage (0.085 as 8.5%)
    /// </summary>
    /// <param name="number">Entry number</param>
    /// <param name="percent">Lookup of a value below 1 as percentage</param>
    internal bool Contains(decimal number, PercentLookup percent) =>
        WrittenDigits(number, percent) != null || Words.Contains(Math.Round(number, 6));

    /// <summary>
    /// Test if the text holds a number in words only, not as numeral or percentage
    /// </summary>
    internal bool IsWordOnly(decimal number, PercentLookup percent) =>
        WrittenDigits(number, percent) == null && Words.Contains(Math.Round(number, 6));

    /// <summary>
    /// Fewest digits of the number as written in the text (numeral or percentage), null when the text
    /// holds no numeral of it
    /// </summary>
    internal int? WrittenDigits(decimal number, PercentLookup percent)
    {
        int? digits = numeralDigits.TryGetValue(Math.Round(number, 6), out var numeral) ? numeral : null;
        if (percent == PercentLookup.None || number >= 1m)
        {
            return digits;
        }
        var points = Math.Round(number * 100m, 6);
        if (percentDigits.TryGetValue(points, out var written))
        {
            digits = Math.Min(digits ?? written, written);
        }
        if (percent == PercentLookup.Points && numeralDigits.TryGetValue(points, out var plain))
        {
            digits = Math.Min(digits ?? plain, plain);
        }
        return digits;
    }

    private static void AddDigits(Dictionary<decimal, int> digits, decimal value, int count) =>
        digits[value] = digits.TryGetValue(value, out var existing) ? Math.Min(existing, count) : count;

    /// <summary>
    /// Scan a text; digits of other scripts are normalized first (positions remain valid)
    /// </summary>
    /// <param name="text">Source text</param>
    /// <param name="languages">Languages of the percent, scale and fraction words, default: all languages</param>
    internal static SourceNumbers Scan(string text, LanguageSet languages = null)
    {
        var numbers = new SourceNumbers();
        if (string.IsNullOrEmpty(text))
        {
            return numbers;
        }
        languages ??= LanguageSet.All;
        text = SourceScript.Normalize(text);
        foreach (Match match in NumberToken.Matches(text))
        {
            var percent = languages.PercentSign.IsMatch(text, match.Index + match.Length);
            var digits = match.Value.Count(char.IsDigit);
            foreach (var value in Interpretations(match))
            {
                numbers.Numerals.Add(value);
                AddDigits(numbers.numeralDigits, value, digits);
                AddHit(numbers.numeralHits, value, match);
                if (percent)
                {
                    numbers.Percents.Add(value);
                    AddDigits(numbers.percentDigits, value, digits);
                    AddHit(numbers.percentHits, value, match);
                }
            }
        }
        foreach (Match match in languages.ScaleWord.Matches(text))
        {
            if (decimal.TryParse(match.Groups["number"].Value.Replace(',', '.'), NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var number) &&
                languages.Scales.TryGetValue(match.Groups["scale"].Value.ToLowerInvariant(), out var scale))
            {
                var value = Math.Round(number * scale, 6);
                numbers.Words.Add(value);
                AddHit(numbers.wordHits, value, match);
            }
        }
        foreach (Match match in languages.FractionWord.Matches(text))
        {
            var value = match.Groups["half"].Success ? 0.5m : match.Groups["double"].Success ? 2m : 1.5m;
            numbers.Words.Add(value);
            AddHit(numbers.wordHits, value, match);
        }
        return numbers;
    }

    // lakh grouping (1,50,000 and 20,00,000.50): groups of two digits before the last group of three;
    // a group of three is followed by no digit: 0.0475 is one number, not 0.047 and 5
    private static readonly Regex NumberToken = new(
        @"(?<lakh>\d{1,2}(?:,\d{2})+,\d{3}(?:\.\d+)?)(?!\d|,\d)|\d{1,3}(?:[ \u00a0\u202f.,'’]\d{3})+(?!\d)(?:[.,]\d+)?|\d+(?:[.,]\d+)?",
        RegexOptions.Compiled);

    private static IEnumerable<decimal> Interpretations(Match match)
    {
        if (match.Groups["lakh"].Success)
        {
            // the grouping is unambiguous: one reading only
            if (decimal.TryParse(match.Value.Replace(",", string.Empty), NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var lakh))
            {
                yield return Math.Round(lakh, 6);
            }
            yield break;
        }
        var token = match.Value;
        // integer reading: every separator is a group separator
        var digits = new string(token.Where(char.IsDigit).ToArray());
        if (digits.Length is > 0 and <= 15 &&
            decimal.TryParse(digits, NumberStyles.None, CultureInfo.InvariantCulture, out var integer))
        {
            yield return integer;
        }
        // decimal reading: the last '.' or ',' is the decimal separator when 1-4 digits follow,
        // or any number of digits when it is the only separator (0.01675)
        var last = token.LastIndexOfAny(['.', ',']);
        var fractionDigits = token.Length - last - 1;
        if (last > 0 && fractionDigits >= 1 &&
            (fractionDigits <= 4 || token.IndexOfAny(['.', ',', ' ', '\u00a0', '\u202f', '\'', '’']) == last))
        {
            var whole = new string(token[..last].Where(char.IsDigit).ToArray());
            var fraction = token[(last + 1)..];
            if (decimal.TryParse($"{whole}.{fraction}", NumberStyles.Number, CultureInfo.InvariantCulture, out var number))
            {
                yield return Math.Round(number, 6);
            }
        }
    }
}
