namespace PayrollEngine.Lex;

/// <summary>
/// Totals of the apply results of several manifests (<see cref="ApplyOperation"/>)
/// </summary>
public sealed class ApplySummary
{
    /// <summary>Regulation values replaced</summary>
    public int Changed { get; private set; }

    /// <summary>Regulation values added</summary>
    public int Added { get; private set; }

    /// <summary>Lookups replaced by a text table</summary>
    public int Tables { get; private set; }

    /// <summary>Regulation values equal to the lex content</summary>
    public int Unchanged { get; private set; }

    /// <summary>Lex entries that could not be applied</summary>
    public int Skipped { get; private set; }

    /// <summary>Manifests that could not be applied (invalid lex.json or regulation)</summary>
    public int Failed { get; private set; }

    /// <summary>Changes of the regulation values (changed, added and tables)</summary>
    public int Applied => Changed + Added + Tables;

    /// <summary>
    /// Add the result of one manifest
    /// </summary>
    /// <param name="result">Apply result</param>
    public void Add(ApplyResult result)
    {
        if (result.Error != null)
        {
            Failed++;
            return;
        }
        foreach (var change in result.Changes)
        {
            switch (change.Kind)
            {
                case ApplyChangeKind.Added:
                    Added++;
                    break;
                case ApplyChangeKind.Table:
                    Tables++;
                    break;
                default:
                    Changed++;
                    break;
            }
        }
        Unchanged += result.Unchanged;
        Skipped += result.Skipped.Count;
    }
}
