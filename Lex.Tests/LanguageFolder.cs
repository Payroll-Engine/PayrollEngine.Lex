namespace PayrollEngine.Lex.Tests;

/// <summary>Temporary folder of additional language files</summary>
internal sealed class LanguageFolder : IDisposable
{
    internal string Path { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "lexlang-" + Guid.NewGuid().ToString("N"));

    internal LanguageFolder(params (string Name, string Json)[] files)
    {
        Directory.CreateDirectory(Path);
        foreach (var (name, json) in files)
        {
            File.WriteAllText(System.IO.Path.Combine(Path, name), json);
        }
    }

    internal LanguageSet Get(params string[] languages) => LanguageSet.Get(languages, Path);

    public void Dispose() => Directory.Delete(Path, recursive: true);
}
