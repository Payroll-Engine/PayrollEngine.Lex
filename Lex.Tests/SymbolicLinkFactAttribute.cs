using Xunit;

namespace PayrollEngine.Lex.Tests;

/// <summary>Fact that is skipped where symbolic links cannot be created (Windows without the privilege),
/// optionally also on Windows (<see cref="UnixOnly"/>)</summary>
internal sealed class SymbolicLinkFactAttribute : FactAttribute
{
    private static readonly Lazy<bool> LinksSupported = new(Probe);

    /// <summary>Run only where paths are case-sensitive (not on Windows)</summary>
    public bool UnixOnly { get; set; }

    public override string Skip
    {
        get
        {
            if (base.Skip != null)
            {
                return base.Skip;
            }
            if (UnixOnly && OperatingSystem.IsWindows())
            {
                return "Case-sensitive paths only (not on Windows).";
            }
            return LinksSupported.Value ? null : "Symbolic links cannot be created in this environment.";
        }
        set => base.Skip = value;
    }

    private static bool Probe()
    {
        var folder = Path.Combine(Path.GetTempPath(), "lexlink-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(folder);
            var target = Path.Combine(folder, "target.txt");
            File.WriteAllText(target, "target");
            File.CreateSymbolicLink(Path.Combine(folder, "link.txt"), target);
            var subFolder = Directory.CreateDirectory(Path.Combine(folder, "sub")).FullName;
            Directory.CreateSymbolicLink(Path.Combine(folder, "folder-link"), subFolder);
            return true;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return false;
        }
        finally
        {
            try { Directory.Delete(folder, true); }
            catch (IOException) { /* best effort */ }
        }
    }
}
