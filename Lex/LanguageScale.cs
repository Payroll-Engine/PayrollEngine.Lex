
// ReSharper disable CommentTypo

namespace PayrollEngine.Lex;

/// <summary>Scale word of a language with its factor (<c>Mio.</c> = 1,000,000)</summary>
internal sealed class LanguageScale
{
    /// <summary>Scale word</summary>
    public string Word { get; init; }

    /// <summary>Factor of the scale word</summary>
    public decimal Factor { get; init; }
}
