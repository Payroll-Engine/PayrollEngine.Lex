namespace PayrollEngine.Lex;

/// <summary>
/// Result of the cycle query of a rollover (<see cref="RolloverCycle.GetAsync"/>)
/// </summary>
public sealed class RolloverCycleResult
{
    /// <summary>Cycle that contains the rollover date, null on error</summary>
    public RolloverCycle Cycle { get; internal init; }

    /// <summary>Unknown tenant or no cycle for the date</summary>
    public string Error { get; internal init; }
}
