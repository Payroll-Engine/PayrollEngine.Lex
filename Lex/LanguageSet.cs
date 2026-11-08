using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
// ReSharper disable GrammarMistakeInComment
// ReSharper disable CommentTypo

namespace PayrollEngine.Lex;

/// <summary>
/// Languages of the source verification: the words for percentages, scales, fractions, year groups, validity
/// starts, unchanged values and citations, read from language files <c>{language}.json</c>.
/// The language files of the package lie in the folder <see cref="FolderName"/> next to the library; a file of
/// an additional folder, given by the caller, adds a language or replaces the package file with the same language
/// code, so a new language needs no new library version. A manifest selects its languages with <c>languages</c>;
/// without it all available languages apply as one union. <see cref="Label"/> names the languages and the hash of
/// their profiles and is part of the automatic verification method.
/// </summary>
public sealed class LanguageSet
{
    /// <summary>Folder of the language files next to the library</summary>
    public const string FolderName = "LexVocabulary";

    private const int HashLength = 8;

    private static readonly Lock Sync = new();
    private static readonly Dictionary<string, Dictionary<string, LanguageProfile>> Loaded = new(StringComparer.Ordinal);
    private static readonly Dictionary<string, LanguageSet> Sets = new(StringComparer.Ordinal);

    /// <summary>Folder of the language files of the package, next to the library</summary>
    public static string PackageDirectory
    {
        get
        {
            var location = typeof(LanguageSet).Assembly.Location;
            var directory = string.IsNullOrEmpty(location) ? AppContext.BaseDirectory : Path.GetDirectoryName(location);
            return Path.Combine(directory ?? string.Empty, FolderName);
        }
    }

    /// <summary>Codes of the available languages, sorted</summary>
    /// <param name="additionalDirectory">Folder of additional language files, null for the package languages only</param>
    public static IReadOnlyList<string> Available(string additionalDirectory = null) =>
        LoadProfiles(PackageDirectory, Normalize(additionalDirectory)).Keys.Order(StringComparer.Ordinal).ToList();

    /// <summary>Union of all package languages</summary>
    public static LanguageSet All => Get(null);

    /// <summary>
    /// Language set of language codes; null or empty for all available languages
    /// </summary>
    /// <param name="languages">Language codes (ISO 639-1 or 639-3, lower case)</param>
    /// <param name="additionalDirectory">Folder of additional language files: a file adds a language or replaces the
    /// package file with the same language code; null for the package languages only</param>
    /// <exception cref="ArgumentException">A language without language file</exception>
    /// <exception cref="InvalidOperationException">Missing or invalid language files</exception>
    public static LanguageSet Get(IEnumerable<string> languages, string additionalDirectory = null) =>
        Get(languages, PackageDirectory, Normalize(additionalDirectory));

    private static string Normalize(string directory) =>
        string.IsNullOrWhiteSpace(directory) ? null : Path.GetFullPath(directory);

    /// <summary>
    /// Language set of language codes from the language files of a package folder and an additional folder
    /// </summary>
    /// <param name="languages">Language codes, null or empty for all available languages</param>
    /// <param name="packageDirectory">Folder of the package language files</param>
    /// <param name="additional">Folder of additional language files, null for none</param>
    internal static LanguageSet Get(IEnumerable<string> languages, string packageDirectory, string additional)
    {
        var profiles = LoadProfiles(packageDirectory, additional);
        var codes = (languages ?? []).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (codes.Count == 0)
        {
            codes = profiles.Keys.Order(StringComparer.Ordinal).ToList();
        }
        var unknown = codes.Where(x => !profiles.ContainsKey(x)).ToList();
        if (unknown.Count > 0)
        {
            throw new ArgumentException($"No language file for {string.Join(", ", unknown)} " +
                                        $"(available: {string.Join(", ", profiles.Keys.Order(StringComparer.Ordinal))})",
                nameof(languages));
        }
        lock (Sync)
        {
            var key = DirectoryKey(packageDirectory, additional) + "|" + string.Join(",", codes);
            if (!Sets.TryGetValue(key, out var set))
            {
                Sets[key] = set = new LanguageSet(codes.Select(x => profiles[x]).ToList());
            }
            return set;
        }
    }

    private static string DirectoryKey(string packageDirectory, string additional) =>
        packageDirectory + "|" + additional;

    /// <summary>Language set of profiles, without language files</summary>
    // ReSharper disable once UnusedMember.Global
    internal static LanguageSet Create(IEnumerable<LanguageProfile> profiles) =>
        new(profiles.OrderBy(x => x.Language, StringComparer.Ordinal).ToList());

    /// <summary>
    /// Load the language files of the package folder and the additional folder; loaded once per folder pair
    /// </summary>
    private static Dictionary<string, LanguageProfile> LoadProfiles(string packageDirectory, string additional)
    {
        lock (Sync)
        {
            var key = DirectoryKey(packageDirectory, additional);
            if (Loaded.TryGetValue(key, out var loaded))
            {
                return loaded;
            }
            if (!Directory.Exists(packageDirectory))
            {
                throw new InvalidOperationException($"Missing language files of the package: {packageDirectory}");
            }
            var profiles = new Dictionary<string, LanguageProfile>(StringComparer.Ordinal);
            Load(profiles, packageDirectory);
            if (additional != null)
            {
                if (!Directory.Exists(additional))
                {
                    throw new InvalidOperationException($"Missing folder of additional language files: {additional}");
                }
                Load(profiles, additional);
            }
            if (profiles.Count == 0)
            {
                throw new InvalidOperationException($"No language files in {packageDirectory}");
            }
            Loaded[key] = profiles;
            return profiles;
        }
    }

    private static void Load(Dictionary<string, LanguageProfile> profiles, string directory)
    {
        foreach (var path in Directory.GetFiles(directory, "*.json").Order(StringComparer.Ordinal))
        {
            var language = Path.GetFileNameWithoutExtension(path);
            if (!LanguageProfile.IsLanguageCode(language))
            {
                throw new InvalidOperationException(
                    $"Invalid language file {path}: the file name must be a language code (e.g. de.json)");
            }
            profiles[language] = LanguageProfile.Parse(File.ReadAllText(path), language, path);
        }
    }

    private LanguageSet(IReadOnlyList<LanguageProfile> profiles)
    {
        Profiles = profiles;
        Languages = profiles.Select(x => x.Language).ToList();
        var canonical = string.Join("\n", profiles.Select(x => x.Canonical()));
        Hash = Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(canonical)))[..HashLength];

        string Words(Func<LanguageProfile, IEnumerable<string>> words, bool ignoreCase = true) =>
            LanguageProfile.Alternation(profiles, words, ignoreCase);

        Scales = profiles
            .SelectMany(p => p.ScaleWords)
            .GroupBy(x => x.Word.ToLowerInvariant())
            .ToDictionary(x => x.Key, x => x.First().Factor);

        // numbers ------------------------------------------------------------

        // % or a percent word, not followed by a letter (percentage, Prozentsatz)
        PercentSign = new(
            @"\G[ \u00a0\u202f]?(?:%|(?:" + Words(p => p.PercentWords) + @")(?!\p{L}))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // a numeral directly before the scale word: "a million" holds no number; a decimal comma with one or two
        // digits (1,5 Mio.), a comma before three digits is a group separator (1,000 million holds no number)
        ScaleWord = new(
            @"(?<![\d.,])(?<number>\d+(?:\.\d+|,\d{1,2}(?!\d))?)[ \u00a0]?(?<scale>" +
            Words(p => p.ScaleWords.Select(x => x.Word)) + @")(?!\p{L})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // the longer phrases come first: "one and one-half" is 1.5, not 0.5
        FractionWord = new(
            @"(?<!\p{L})(?:" + Words(p => p.OneAndHalfWords) + @")(?!\p{L})|(?<![\d.,])1 ?½|" +
            @"(?<half>(?<!\p{L})(?:" + Words(p => p.HalfWords) + @")(?!\p{L})|½)|" +
            @"(?<double>(?<!\p{L})(?:" + Words(p => p.DoubleWords) + @")(?!\p{L}))",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // dates and validity -------------------------------------------------

        var monthWords = Words(p => p.Months.SelectMany(x => x));
        var month = "(?:" + monthWords + ")";

        // a month name of several languages counts only when all of them give the same month
        MonthNumbers = profiles
            .SelectMany(p => p.Months.SelectMany((names, index) => names.Select(name => (Name: name, Month: index + 1))))
            .GroupBy(x => x.Name, StringComparer.OrdinalIgnoreCase)
            .Where(g => g.Select(x => x.Month).Distinct().Count() == 1)
            .ToDictionary(g => g.Key, g => g.First().Month, StringComparer.OrdinalIgnoreCase);

        // a date before its year, day first: 1 January 2025, 1st April, 2026, the 1st day of April, 2026,
        // 1. Januar 2025, 1er janvier 2025, 1 de enero de 2025, 1° gennaio 2025, 1º de janeiro de 2025
        var dayLink = "(?:(?:" + Words(p => p.DayLinks) + @")\s+)?";
        var dayMonthDate = @"\d{1,2}(?:st|nd|rd|th|er|\.|°|º|ª)?\s*" + dayLink + @"(?:de\s+)?" + month + @"\.?(?:\s+de)?,?\s+";

        // the month (and day) of a date directly before its year: January 2026, January 1, 2026, 1 January 2026,
        // the 1st day of April, 2026, 1. Januar 2026, 17.09.2026
        DateBefore = new(
            @"(?:(?<!\p{L})(?<month>" + monthWords + @")\.?\s+(?<day>\d{1,2})(?:st|nd|rd|th)?,?\s+|" +
            @"(?<![\d.,])(?<day>\d{1,2})(?:st|nd|rd|th|er|\.|°|º|ª)?\s*" + dayLink + @"(?:de\s+)?(?<!\p{L})(?<month>" +
            monthWords + @")\.?(?:\s+de)?,?\s+|" +
            @"(?<!\p{L})(?<month>" + monthWords + @")\.?,?\s+|" +
            @"(?<![\d.,])(?<day>0?[1-9]|[12]\d|3[01])\.(?<number>0?[1-9]|1[0-2])\.)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // a full date before its year: January 1, 2025; a day-first date; 01.04.2025, 1/1/2025
        var fullDate = "(?:" + month + @"\.?\s+\d{1,2}(?:st|nd|rd|th)?,?\s+|" + dayMonthDate + @"|\d{1,2}[/.\-]\d{1,2}[/.\-])";

        // a validity start directly before the year or a date: effective January 1, 2025; beginning on or after
        // January 1, 2025; with effect from 1st April, 2026; ab dem 1. Januar 2026; à compter du 1er janvier 2026;
        // from only with a full date (from 1 July 2025; increased from $X in 2024 is none)
        ValidityBefore = new(
            @"(?<!\p{L})(?:(?:" + Words(p => p.ValidityBefore) + @")\s+" + fullDate + "?|" +
            "(?:" + Words(p => p.DatedValidityBefore) + @")\s+" + fullDate + ")$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // a validity start directly after the year: 2025 and after, 2025 und folgende, 2025 et suivantes
        ValidityAfter = new(
            @"^\s+(?:" + Words(p => p.ValidityAfter) + @")(?!\p{L})",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // a value unchanged from an earlier year: unchanged from 2024; same as (in, for) 2024; unverändert seit 2024;
        // inchangé depuis 2024; sin cambios desde 2024
        UnchangedBefore = new(
            @"(?<!\p{L})(?:" + Words(p => p.Unchanged) + @")\s+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // citations ----------------------------------------------------------

        // an enactment word (Act, Decree-Law, Gesetz, Einkommensteuergesetz, loi, Real Decreto)
        var enactment = @"(?<!\p{L})(?:" + Words(p => p.EnactmentWords) + @"|\p{L}*(?:" +
                        Words(p => p.EnactmentEndings) + @"))(?!\p{L})";

        // a number with slash before the year: No. 57/2023, Nr. 123/2025, n.º 133/2024, Ley 35/2006, Real Decreto 1/2026
        SlashNumberBefore = new(
            "(?:" + NumberMark + "|" + enactment + @")\s?\d+[A-Za-z-]*/$", RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // a number of a year: Decree-Law No. 33 of 2021, Act No. 5 of 2020, Act 43 of 1961; in parentheses after the
        // enactment title: Income-tax Act, 2025 (30 of 2025)
        NumberOfBefore = new(
            "(?:(?:" + NumberMark + "|" + enactment + @")\s?|\(\s?)\d+[A-Za-z]?\s+of\s+$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // an enactment title with its year: Finance Act, 2025; Income-tax Rules, 1962; Finance (No. 2) Act, 2024
        TitleBefore = new(
            @"(?<!\p{L})(?:" + Words(p => p.TitleWords, ignoreCase: false) + @")\s?,?\s*$",
            RegexOptions.Compiled);

        // an official gazette with the year of the issue: BGBl. I 2025 Nr. 12, BGBl. 2024 I S. 123, Stb. 2024, 123
        var gazette = "(?:" + Words(p => p.Gazettes, ignoreCase: false) + ")";
        GazetteBefore = new(
            @"(?<![\p{L}.])" + gazette + @"\s?(?:(?:[IVX]{1,3}|[A-C])\s)?$", RegexOptions.Compiled);

        // an enactment or a gazette with its date, at most 40 characters before the connector: Gesetz vom 20. Dezember 2024,
        // loi n° 2024-1234 du 29 décembre 2024, Real Decreto 1/2026, de 28 de noviembre de 2025, legge 30 dicembre 2024,
        // Verordnung vom 20.12.2024
        EnactmentDateBefore = new(
            "(?:" + enactment + "|(?-i:" + gazette + @"))(?:[^\n;:]{0,40}?\s(?:" +
            Words(p => p.DateLinks) + "))?\\s+(?:" + dayMonthDate + "|" + YearAnalyzer.NumericDate + ")$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // year groups --------------------------------------------------------

        // connector of a year enumeration, only between two years: 2024, 2025, or 2026; 2024 and 2025; 2024 und 2025
        YearEnumeration = new(
            @"^(?:\s*,\s*(?:(?:" + Words(p => p.EnumerationWords) + @")\s+)?|\s+(?:" +
            Words(p => p.EnumerationWords) + @")\s+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);

        // connector of a year range, only between two years: 2024-2026, 2024 through 2026, 2024 bis 2026, 2024 à 2026
        YearRange = new(
            @"^(?:\s*[-\u2010-\u2015]\s*|\s+(?:" + Words(p => p.RangeWords) + @")\s+)$",
            RegexOptions.Compiled | RegexOptions.IgnoreCase);
    }

    // a number mark: No., Nos., Nr., núm., n°, nº, n.º, n.
    private const string NumberMark = @"(?<!\p{L})(?:[Nn]os?\.?|[Nn]r\.?|[Nn]úm\.?|[Nn]\.?\s?[°º]|n\.)";

    /// <summary>Codes of the languages, sorted</summary>
    public IReadOnlyList<string> Languages { get; }

    /// <summary>Hash of the language profiles (first 8 hex digits of SHA-256): changes with any word</summary>
    public string Hash { get; }

    /// <summary>Languages and hash, e.g. <c>de,en 3f2a9c1b</c></summary>
    public string Label => $"{string.Join(",", Languages)} {Hash}";

    /// <inheritdoc />
    public override string ToString() => Label;

    internal IReadOnlyList<LanguageProfile> Profiles { get; }

    /// <summary>Month number (1–12) of a month name or abbreviation</summary>
    internal IReadOnlyDictionary<string, int> MonthNumbers { get; }

    /// <summary>Month and day of a date directly before a year (see <see cref="Date"/>)</summary>
    internal Regex DateBefore { get; }

    /// <summary>
    /// Month and day of the date that ends at the end of the text: a month name with or without day
    /// (<c>January 2026</c>, <c>January 1, 2026</c>, <c>1 January 2026</c>, <c>the 1st day of April, 2026</c>)
    /// or a numeric date with dots (<c>17.09.2026</c>); null without date or for a month name of several months
    /// </summary>
    /// <param name="before">Text before the year</param>
    /// <param name="start">Text position of the date in <paramref name="before"/></param>
    internal (int Month, int? Day)? Date(string before, out int start)
    {
        start = before.Length;
        var match = DateBefore.Match(before);
        if (!match.Success)
        {
            return null;
        }
        int month;
        if (match.Groups["number"].Success)
        {
            month = int.Parse(match.Groups["number"].Value, CultureInfo.InvariantCulture);
        }
        else if (!MonthNumbers.TryGetValue(match.Groups["month"].Value, out month))
        {
            return null;
        }
        start = match.Index;
        int? day = match.Groups["day"].Success ? int.Parse(match.Groups["day"].Value, CultureInfo.InvariantCulture) : null;
        return day is < 1 or > 31 ? null : (month, day);
    }

    internal IReadOnlyDictionary<string, decimal> Scales { get; }
    internal Regex PercentSign { get; }
    internal Regex ScaleWord { get; }
    internal Regex FractionWord { get; }
    internal Regex ValidityBefore { get; }
    internal Regex ValidityAfter { get; }
    internal Regex UnchangedBefore { get; }
    internal Regex SlashNumberBefore { get; }
    internal Regex NumberOfBefore { get; }
    internal Regex TitleBefore { get; }
    internal Regex GazetteBefore { get; }
    internal Regex EnactmentDateBefore { get; }
    internal Regex YearEnumeration { get; }
    internal Regex YearRange { get; }
}
