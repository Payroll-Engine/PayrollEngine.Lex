using System;

namespace PayrollEngine.Lex;

/// <summary>
/// Key pattern of lex entries: an exact key, a name that also matches all keys below it (<c>Name.*</c>),
/// or a prefix with a trailing <c>*</c>
/// </summary>
public static class KeyPattern
{
    /// <summary>
    /// Test if a key is matched by a pattern
    /// </summary>
    /// <param name="pattern">Key, name or prefix with trailing *</param>
    /// <param name="key">Entry key</param>
    public static bool Matches(string pattern, string key)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrWhiteSpace(pattern))
        {
            return false;
        }
        if (pattern.EndsWith('*'))
        {
            return key.StartsWith(pattern[..^1], StringComparison.Ordinal);
        }
        return string.Equals(key, pattern, StringComparison.Ordinal) ||
               key.StartsWith(pattern + ".", StringComparison.Ordinal);
    }
}
