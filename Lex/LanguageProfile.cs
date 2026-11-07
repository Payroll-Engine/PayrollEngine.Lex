using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
// ReSharper disable CommentTypo

namespace PayrollEngine.Lex;

/// <summary>
/// Words of one language for the number and year rules of the source verification, loaded from a language file
/// <c>{language}.json</c> (see <see cref="LanguageSet"/>). Short words are used in a narrow position only:
/// enumeration and range words between two years, validity words directly before a year or a date.
/// </summary>
internal sealed class LanguageProfile
{
    /// <summary>Language code (ISO 639-1 or 639-3, lower case), equals the file name</summary>
    public string Language { get; init; }

    /// <summary>Language name (information only)</summary>
    public string Name { get; init; }

    /// <summary>Percent words after a numeral (<c>percent</c>, <c>Prozent</c>, <c>pour cent</c>)</summary>
    public List<string> PercentWords { get; init; } = [];

    /// <summary>Scale words after a numeral with their factor (<c>million</c>, <c>Mio.</c>, <c>lakh</c>)</summary>
    public List<LanguageScale> ScaleWords { get; init; } = [];

    /// <summary>Words for one half (<c>one-half</c>, <c>die Hälfte</c>)</summary>
    public List<string> HalfWords { get; init; } = [];

    /// <summary>Words for one and a half (<c>time and a half</c>, <c>anderthalb</c>)</summary>
    public List<string> OneAndHalfWords { get; init; } = [];

    /// <summary>Words for two times (<c>double time</c>)</summary>
    public List<string> DoubleWords { get; init; } = [];

    /// <summary>Connectors of a year enumeration (and, or), only between two years</summary>
    public List<string> EnumerationWords { get; init; } = [];

    /// <summary>Connectors of a year range (<c>through</c>, <c>bis</c>), only between two years</summary>
    public List<string> RangeWords { get; init; } = [];

    /// <summary>Validity start directly before a year or a date (<c>effective</c>, <c>ab dem</c>, <c>à compter du</c>)</summary>
    public List<string> ValidityBefore { get; init; } = [];

    /// <summary>Validity start directly before a full date only (<c>from 1 July 2025</c>; <c>from 2024</c> is no validity)</summary>
    public List<string> DatedValidityBefore { get; init; } = [];

    /// <summary>Validity start directly after the year (<c>and after</c>, <c>und folgende</c>)</summary>
    public List<string> ValidityAfter { get; init; } = [];

    /// <summary>A value unchanged from the year directly after (<c>unchanged from</c>, <c>unverändert seit</c>)</summary>
    public List<string> Unchanged { get; init; } = [];

    /// <summary>Words of an enactment before its number or its date (<c>Act</c>, <c>Gesetz</c>, <c>loi</c>, <c>Real Decreto</c>)</summary>
    public List<string> EnactmentWords { get; init; } = [];

    /// <summary>Endings of compound enactment words (<c>Einkommensteuergesetz</c>)</summary>
    public List<string> EnactmentEndings { get; init; } = [];

    /// <summary>Title words of an enactment followed by its year (<c>Finance Act, 2025</c>), case-sensitive</summary>
    public List<string> TitleWords { get; init; } = [];

    /// <summary>Official gazettes followed by the year of the issue (<c>BGBl. I 2025</c>), case-sensitive</summary>
    public List<string> Gazettes { get; init; } = [];

    /// <summary>Connectors between an enactment and its date (<c>Gesetz vom</c>, <c>loi du</c>, <c>Real Decreto … de</c>)</summary>
    public List<string> DateLinks { get; init; } = [];

    /// <summary>Month names and abbreviations</summary>
    public List<string> Months { get; init; } = [];

    private static readonly Regex LanguageCode = new("^[a-z]{2,3}$", RegexOptions.Compiled);

    /// <summary>Test if a text is a language code: two or three lower-case letters (ISO 639-1 or 639-3)</summary>
    internal static bool IsLanguageCode(string code) => code != null && LanguageCode.IsMatch(code);

    private static readonly JsonSerializerOptions ReadOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow
    };

    private static readonly JsonSerializerOptions CanonicalOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    /// <summary>
    /// Parse and validate a language file; an unknown property, a language other than the file name, an empty word
    /// or a scale factor below 1 is an error
    /// </summary>
    /// <param name="json">Text of the language file</param>
    /// <param name="language">Language code of the file name</param>
    /// <param name="path">Path of the file, for the error message</param>
    internal static LanguageProfile Parse(string json, string language, string path)
    {
        LanguageProfile profile;
        try
        {
            profile = JsonSerializer.Deserialize<LanguageProfile>(json, ReadOptions);
        }
        catch (JsonException exception)
        {
            throw new InvalidOperationException($"Invalid language file {path}: {exception.Message}", exception);
        }
        var error = profile == null ? "empty file" : profile.Problem(language);
        if (error != null)
        {
            throw new InvalidOperationException($"Invalid language file {path}: {error}");
        }
        return profile;
    }

    private string Problem(string language)
    {
        if (!string.Equals(Language, language, StringComparison.Ordinal))
        {
            return $"language '{Language}' differs from the file name '{language}'";
        }
        var words = new[]
        {
            PercentWords, HalfWords, OneAndHalfWords, DoubleWords, EnumerationWords, RangeWords, ValidityBefore,
            DatedValidityBefore, ValidityAfter, Unchanged, EnactmentWords, EnactmentEndings, TitleWords, Gazettes,
            DateLinks, Months
        };
        if (words.Any(x => x == null) || ScaleWords == null)
        {
            return "a word list is null";
        }
        if (words.SelectMany(x => x).Concat(ScaleWords.Select(x => x?.Word)).Any(string.IsNullOrWhiteSpace))
        {
            return "empty word";
        }
        var scale = ScaleWords.FirstOrDefault(x => x.Factor < 1m);
        return scale != null ? $"scale word '{scale.Word}' with factor {scale.Factor} below 1" : null;
    }

    /// <summary>Canonical text of the profile, input of the hash of a language set</summary>
    internal string Canonical() => JsonSerializer.Serialize(this, CanonicalOptions);

    /// <summary>
    /// Regex alternation of the words of profiles: longest first, regex characters escaped,
    /// a space matches any whitespace
    /// </summary>
    /// <param name="profiles">Language profiles</param>
    /// <param name="words">Words of a profile</param>
    /// <param name="ignoreCase">Words that differ in case only are one word</param>
    internal static string Alternation(IEnumerable<LanguageProfile> profiles, Func<LanguageProfile, IEnumerable<string>> words,
        bool ignoreCase = true) =>
        Alternation(profiles.SelectMany(words), ignoreCase);

    /// <summary>
    /// Regex alternation of words: longest first, regex characters escaped, a space matches any whitespace;
    /// without words an alternation that never matches
    /// </summary>
    internal static string Alternation(IEnumerable<string> words, bool ignoreCase = true)
    {
        var alternation = string.Join("|", words
            .Where(w => !string.IsNullOrWhiteSpace(w))
            .Distinct(ignoreCase ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
            .OrderByDescending(w => w.Length)
            .ThenBy(w => w, StringComparer.Ordinal)
            .Select(w => Regex.Escape(w).Replace(@"\ ", @"\s+")));
        return alternation.Length > 0 ? alternation : "(?!)";
    }
}

/// <summary>Scale word of a language with its factor (<c>Mio.</c> = 1,000,000)</summary>
internal sealed class LanguageScale
{
    /// <summary>Scale word</summary>
    public string Word { get; init; }

    /// <summary>Factor of the scale word</summary>
    public decimal Factor { get; init; }
}
