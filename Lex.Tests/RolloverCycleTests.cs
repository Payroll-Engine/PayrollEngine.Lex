using Xunit;

namespace PayrollEngine.Lex.Tests;

public class RolloverCycleTests
{
    private static readonly DateTime Date = new(2027, 3, 15);

    private static Task<RolloverCycle> Cycle(DateTime start, DateTime end) =>
        Task.FromResult(new RolloverCycle { Start = start, End = end });

    [Fact]
    public async Task Cycle_IsQueriedForTheTenantAndDate()
    {
        string queriedTenant = null;
        (int Id, DateTime Moment)? queriedCycle = null;

        var result = await RolloverCycle.GetAsync("XX.Test", Date,
            identifier =>
            {
                queriedTenant = identifier;
                return Task.FromResult<int?>(7);
            },
            (id, moment) =>
            {
                queriedCycle = (id, moment);
                return Cycle(new DateTime(2027, 1, 1), new DateTime(2027, 12, 31, 23, 59, 59));
            });

        Assert.Null(result.Error);
        Assert.Equal("XX.Test", queriedTenant);
        Assert.Equal((7, Date), queriedCycle);
        Assert.Equal(new DateTime(2027, 1, 1), result.Cycle.Start);
    }

    [Fact]
    public async Task UnknownTenant_IsAnError()
    {
        var result = await RolloverCycle.GetAsync("XX.Unknown", Date,
            _ => Task.FromResult<int?>(null),
            (_, _) => throw new InvalidOperationException("no cycle query expected"));

        Assert.Null(result.Cycle);
        Assert.Equal("Unknown tenant XX.Unknown", result.Error);
    }

    [Fact]
    public async Task MissingCycle_IsAnError()
    {
        var result = await RolloverCycle.GetAsync("XX.Test", Date,
            _ => Task.FromResult<int?>(7),
            (_, _) => Task.FromResult<RolloverCycle>(null));

        Assert.Null(result.Cycle);
        Assert.Equal("The calendar of tenant XX.Test returns no cycle for 2027-03-15", result.Error);
    }

    [Theory]
    [InlineData(2026, 1, 1, 2026, 12, 31)]
    [InlineData(2027, 3, 16, 2028, 3, 15)]
    public async Task CycleWithoutTheDate_IsAnError(int startYear, int startMonth, int startDay, int endYear, int endMonth, int endDay)
    {
        var result = await RolloverCycle.GetAsync("XX.Test", Date,
            _ => Task.FromResult<int?>(7),
            (_, _) => Cycle(new DateTime(startYear, startMonth, startDay), new DateTime(endYear, endMonth, endDay)));

        Assert.Null(result.Cycle);
        Assert.Contains("no cycle", result.Error);
    }

    [Fact]
    public async Task CycleBoundaries_ContainTheDate()
    {
        var result = await RolloverCycle.GetAsync("XX.Test", Date,
            _ => Task.FromResult<int?>(7),
            (_, _) => Cycle(Date, Date));

        Assert.Null(result.Error);
        Assert.Equal(Date, result.Cycle.End);
    }
}
