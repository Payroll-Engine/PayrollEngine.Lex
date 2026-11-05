using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;

namespace PayrollEngine.Lex;

/// <summary>
/// Numbers of a source text: numerals (every separator interpretation), numerals followed by a percent sign
/// and numbers in words (English and Indian scales, fractions)
/// </summary>
internal sealed class SourceNumbers
{
    /// <summary>Numerals as normalized decimal values</summary>
    internal HashSet<decimal> Numerals { get; } = [];

    /// <summary>Numerals followed by <c>%</c>, <c>percent</c> or <c>per cent</c></summary>
    internal HashSet<decimal> Percents { get; } = [];

    /// <summary>Numbers in words: <c>$1 million</c>, <c>2.5 lakh</c>, <c>one-half</c>, <c>time and a half</c></summary>
    internal HashSet<decimal> Words { get; } = [];

    // fewest digits of a numeral as written in the text, without separators: 4,300 has 4, 1.2% has 2
    private readonly Dictionary<decimal, int> numeralDigits = [];
    private readonly Dictionary<decimal, int> percentDigits = [];

    /// <summary>
    /// Numerals and numbers in words
    /// </summary>
    internal HashSet<decimal> All() => [.. Numerals, .. Words];

    /// <summary>
    /// Test if the text holds a number: as numeral, in words or, below 1, as percentage (0.085 as 8.5%)
    /// </summary>
    /// <param name="number">Entry number</param>
    /// <param name="percent">A value below 1 may be written as percentage</param>
    internal bool Contains(decimal number, bool percent) =>
        WrittenDigits(number, percent) != null || Words.Contains(Math.Round(number, 6));

    /// <summary>
    /// Test if the text holds a number in words only, not as numeral or percentage
    /// </summary>
    internal bool IsWordOnly(decimal number, bool percent) =>
        WrittenDigits(number, percent) == null && Words.Contains(Math.Round(number, 6));

    /// <summary>
    /// Fewest digits of the number as written in the text (numeral or percentage), null when the text
    /// holds no numeral of it
    /// </summary>
    internal int? WrittenDigits(decimal number, bool percent)
    {
        int? digits = numeralDigits.TryGetValue(Math.Round(number, 6), out var numeral) ? numeral : null;
        if (percent && number < 1m && percentDigits.TryGetValue(Math.Round(number * 100m, 6), out var written))
        {
            digits = Math.Min(digits ?? written, written);
        }
        return digits;
    }

    private static void AddDigits(Dictionary<decimal, int> digits, decimal value, int count) =>
        digits[value] = digits.TryGetValue(value, out var existing) ? Math.Min(existing, count) : count;

    /// <summary>
    /// Scan a text
    /// </summary>
    internal static SourceNumbers Scan(string text)
    {
        var numbers = new SourceNumbers();
        if (string.IsNullOrEmpty(text))
        {
            return numbers;
        }
        foreach (Match match in NumberToken.Matches(text))
        {
            var percent = PercentSign.IsMatch(text, match.Index + match.Length);
            var digits = match.Value.Count(char.IsDigit);
            foreach (var value in Interpretations(match))
            {
                numbers.Numerals.Add(value);
                AddDigits(numbers.numeralDigits, value, digits);
                if (percent)
                {
                    numbers.Percents.Add(value);
                    AddDigits(numbers.percentDigits, value, digits);
                }
            }
        }
        foreach (Match match in ScaleWord.Matches(text))
        {
            if (decimal.TryParse(match.Groups["number"].Value, NumberStyles.AllowDecimalPoint,
                    CultureInfo.InvariantCulture, out var number))
            {
                numbers.Words.Add(Math.Round(number * Scale(match.Groups["scale"].Value), 6));
            }
        }
        foreach (Match match in FractionWord.Matches(text))
        {
            numbers.Words.Add(match.Groups["half"].Success ? 0.5m : match.Groups["double"].Success ? 2m : 1.5m);
        }
        return numbers;
    }

    // lakh grouping (1,50,000 and 20,00,000.50): groups of two digits before the last group of three;
    // a group of three is followed by no digit: 0.0475 is one number, not 0.047 and 5
    private static readonly Regex NumberToken = new(
        @"(?<lakh>\d{1,2}(?:,\d{2})+,\d{3}(?:\.\d+)?)(?!\d|,\d)|\d{1,3}(?:[ \u00a0\u202f.,'’]\d{3})+(?!\d)(?:[.,]\d+)?|\d+(?:[.,]\d+)?",
        RegexOptions.Compiled);

    private static readonly Regex PercentSign = new(
        @"\G[ \u00a0\u202f]?(?:%|percent\b|per cent\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // a numeral directly before the scale word: "a million" holds no number
    private static readonly Regex ScaleWord = new(
        @"(?<![\d.,])(?<number>\d+(?:\.\d+)?)[ \u00a0]?(?<scale>million|billion|lakhs?|crores?)\b",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    // the longer phrases come first: "one and one-half" is 1.5, not 0.5
    private static readonly Regex FractionWord = new(
        @"\bone and (?:one[- ]|a )half\b|\btime and (?:one[- ]|a )half\b|(?<![\d.,])1 ?½|(?<half>\bone[- ]half\b|½)|(?<double>\bdouble time\b)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private static decimal Scale(string word) =>
        word.ToLowerInvariant() switch
        {
            "million" => 1_000_000m,
            "billion" => 1_000_000_000m,
            "lakh" or "lakhs" => 100_000m,
            _ => 10_000_000m
        };

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
