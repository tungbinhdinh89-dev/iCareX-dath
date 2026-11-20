using iCareX.Domain.Common;
using iCareX.Domain.Entities;
using iCareX.Domain.Exceptions;
using Xunit;

namespace iCareX.UnitTests;

public sealed class CostRecordRulesTests
{
    [Fact]
    public void FuelAndMultiItemServiceCountMoneyOnce()
    {
        CostRecordRules.Fuel(200_000, 10);
        var total = CostRecordRules.Service([
            new() { Type = "Repair", Title = "Sửa xe", Cost = 100_000 },
            new() { Type = "Maintenance", Title = "Thay dầu", Cost = 150_000 }
        ]);
        Assert.Equal(250_000, total);
        Assert.Equal(450_000, total + 200_000);
    }

    [Theory]
    [InlineData(0, 10)] [InlineData(1.1, 10)] [InlineData(200000, 0)]
    [InlineData(200000, 500.001)] [InlineData(200000, 10.1234)]
    public void FuelRejectsInvalidMoneyAndPrecision(decimal cost, decimal liters) =>
        Assert.Throws<RuleViolation>(() => CostRecordRules.Fuel(cost, liters));

    [Fact]
    public void ServiceRejectsEmptyZeroNegativeUnknownAndRepairPlan()
    {
        Assert.Throws<RuleViolation>(() => CostRecordRules.Service([]));
        foreach (var item in new ServiceItem[] {
            new() { Title = "Thay dầu", Cost = 0 },
            new() { Title = "Thay dầu", Cost = -1 },
            new() { Title = "Thay dầu", Cost = 1.1m },
            new() { Type = "Unknown", Title = "Thay dầu", Cost = 1 },
            new() { Type = "Repair", Title = "Sửa xe", Cost = 1, PlanId = Guid.NewGuid() }
        }) Assert.Throws<RuleViolation>(() => CostRecordRules.Service([item]));
    }
}
