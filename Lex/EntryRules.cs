using System;
using System.Globalization;
using System.Linq;
using System.Text.Json;

namespace PayrollEngine.Lex;

/// <summary>
/// Format rules for new lex entries and verification records
/// </summary>
public static class EntryRules
{
    /// <summary>
    /// Test the format of an entry key: <c>Lookup.Key</c>, no whitespace and no pattern character
    /// </summary>
    /// <param name="key">Entry key</param>
    /// <returns>Error message, null if valid</returns>
    public static string KeyError(string key)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            return "Missing entry key";
        }
        if (key.Any(char.IsWhiteSpace) || key.Contains('*'))
        {
            return $"Entry key '{key}' must not contain whitespace or *";
        }
        var dot = key.IndexOf('.');
        if (dot <= 0 || dot == key.Length - 1)
        {
            return $"Entry key '{key}' must have the format Lookup.Key";
        }
        return null;
    }

    // ReSharper disable once GrammarMistakeInComment
    /// <summary>
    /// Test the format of an entry content: structured content (starting with { or [) must be valid JSON
    /// </summary>
    /// <param name="content">Entry content</param>
    /// <returns>Error message, null if valid</returns>
    public static string ContentError(string content)
    {
        var text = content?.TrimStart();
        if (string.IsNullOrEmpty(text) || (text[0] != '{' && text[0] != '['))
        {
            return null;
        }
        try
        {
            using var _ = JsonDocument.Parse(text);
            return null;
        }
        catch (JsonException exception)
        {
            return "Content is not valid JSON: " + exception.Message;
        }
    }

    /// <summary>
    /// Test a date in the format yyyy-MM-dd
    /// </summary>
    /// <param name="date">Date text</param>
    public static bool IsDate(string date) =>
        DateOnly.TryParseExact(date, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>
    /// Today in the format yyyy-MM-dd
    /// </summary>
    // ReSharper disable once UnusedMember.Global
    public static string Today() =>
        DateTime.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
