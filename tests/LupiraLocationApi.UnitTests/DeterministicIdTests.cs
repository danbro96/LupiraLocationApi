using LupiraLocationApi.Core.Domain;
using LupiraLocationApi.Core.Domain.Telemetry;
using Xunit;

namespace LupiraLocationApi.UnitTests;

public class DeterministicIdTests
{
    [Fact]
    public void DailyLocationSummary_id_is_stable_and_distinct()
    {
        var p = Guid.NewGuid();
        var d = Guid.NewGuid();
        var day = new DateOnly(2026, 6, 18);
        Assert.Equal(DailyLocationSummary.MakeId(p, d, day), DailyLocationSummary.MakeId(p, d, day));
        Assert.NotEqual(DailyLocationSummary.MakeId(p, d, day), DailyLocationSummary.MakeId(p, d, day.AddDays(1)));
    }
}
