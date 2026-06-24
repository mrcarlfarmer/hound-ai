using Hound.Grocery.Config;
using Hound.Grocery.Services;
using Microsoft.Extensions.Options;

namespace Hound.Grocery.Tests.Services;

[TestClass]
public class BudgetLedgerServiceTests
{
    private static BudgetLedgerService CreateService(int cycleStartDay = 25, decimal weeklyTarget = 140.00m, int flexPercent = 10)
    {
        var options = Options.Create(new BudgetSettings
        {
            CycleStartDay = cycleStartDay,
            WeeklyTarget = weeklyTarget,
            WeeklyFlexPercent = flexPercent,
        });
        return new BudgetLedgerService(options);
    }

    [TestMethod]
    public void CurrentCycleStart_OnOrAfterAnchor_ReturnsThisMonthAnchor()
    {
        var service = CreateService(cycleStartDay: 25);

        var result = service.CurrentCycleStart(new DateOnly(2026, 6, 28));

        Assert.AreEqual(new DateOnly(2026, 6, 25), result);
    }

    [TestMethod]
    public void CurrentCycleStart_BeforeAnchor_ReturnsPreviousMonthAnchor()
    {
        var service = CreateService(cycleStartDay: 25);

        var result = service.CurrentCycleStart(new DateOnly(2026, 6, 10));

        Assert.AreEqual(new DateOnly(2026, 5, 25), result);
    }

    [TestMethod]
    public void WeeklyFlexAmount_IsTenPercentOfTarget()
    {
        var service = CreateService(weeklyTarget: 140.00m, flexPercent: 10);

        Assert.AreEqual(14.00m, service.WeeklyFlexAmount());
    }

    [TestMethod]
    public void WeeklyUpperThreshold_IsTargetPlusFlex()
    {
        var service = CreateService(weeklyTarget: 140.00m, flexPercent: 10);

        Assert.AreEqual(154.00m, service.WeeklyUpperThreshold());
    }
}
