using System;
using System.Threading.Tasks;

namespace PayrollEngine.Lex;

/// <summary>
/// Cycle of the payroll calendar that contains the rollover date: its start is the validFrom of the new
/// satellite (<see cref="Rollover.Run"/>)
/// </summary>
public sealed class RolloverCycle
{
    /// <summary>Cycle start</summary>
    public DateTime Start { get; init; }

    /// <summary>Cycle end</summary>
    public DateTime End { get; init; }

    /// <summary>
    /// Query the cycle of a tenant calendar that contains the date
    /// </summary>
    /// <param name="tenant">Tenant identifier</param>
    /// <param name="date">Date within the new cycle</param>
    /// <param name="getTenantId">Returns the id of a tenant identifier, null when unknown</param>
    /// <param name="getCycle">Returns the calendar cycle of the tenant id that contains the date, null without cycle</param>
    /// <returns>The cycle, or an error for an unknown tenant or a missing cycle</returns>
    public static async Task<RolloverCycleResult> GetAsync(string tenant, DateTime date,
        Func<string, Task<int?>> getTenantId, Func<int, DateTime, Task<RolloverCycle>> getCycle)
    {
        ArgumentNullException.ThrowIfNull(getTenantId);
        ArgumentNullException.ThrowIfNull(getCycle);
        var tenantId = await getTenantId(tenant);
        if (tenantId == null)
        {
            return new RolloverCycleResult { Error = $"Unknown tenant {tenant}" };
        }
        var cycle = await getCycle(tenantId.Value, date);
        if (cycle == null || cycle.Start > date || cycle.End < date)
        {
            return new RolloverCycleResult
            {
                Error = $"The calendar of tenant {tenant} returns no cycle for {date:yyyy-MM-dd}"
            };
        }
        return new RolloverCycleResult { Cycle = cycle };
    }
}
