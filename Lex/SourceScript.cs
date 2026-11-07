namespace PayrollEngine.Lex;

/// <summary>
/// Script normalization of a source text for the number and year search: digits of other scripts (Arabic-Indic,
/// Persian, Devanagari, Bengali, Gujarati, Tamil, Kannada, full width) become ASCII digits, the Arabic decimal and
/// thousands separators become <c>.</c> and <c>,</c>, the Arabic and the full width percent sign become <c>%</c>.
/// Every replacement is one character for one character, so text positions remain valid.
/// </summary>
internal static class SourceScript
{
    /// <summary>
    /// Normalize the script of a text; a text without such characters is returned unchanged
    /// </summary>
    internal static string Normalize(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return text;
        }
        char[] chars = null;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c < '\u0080')
            {
                continue;
            }
            var mapped = Map(c);
            if (mapped == c)
            {
                continue;
            }
            chars ??= text.ToCharArray();
            chars[i] = mapped;
        }
        return chars == null ? text : new string(chars);
    }

    private static char Map(char c) =>
        c switch
        {
            '\u066b' => '.',
            '\u066c' => ',',
            '\u066a' or '\uff05' => '%',
            _ when char.IsDigit(c) => (char)('0' + (int)char.GetNumericValue(c)),
            _ => c
        };
}
