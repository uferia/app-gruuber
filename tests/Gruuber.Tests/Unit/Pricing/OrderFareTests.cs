using Gruuber.Orders.Domain;
using Xunit;

namespace Gruuber.Tests.Unit.Pricing;

public class OrderFareTests
{
    [Fact]
    public void ApplySurge_SetsFinalFareAndLocksIt()
    {
        var order = Order.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1);
        order.ApplySurge(baseFare: 20.00m, multiplier: 2.0m, reason: "time_rule");

        Assert.Equal(20.00m, order.BaseFare);
        Assert.Equal(2.0m, order.SurgeMultiplier);
        Assert.Equal(40.00m, order.FinalFare);
        Assert.Equal("time_rule", order.SurgeReason);
    }
}
