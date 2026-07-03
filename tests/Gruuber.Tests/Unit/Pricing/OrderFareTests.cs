using FluentAssertions;
using Gruuber.Orders.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gruuber.Tests.Unit.Pricing;

[TestClass]
public class OrderFareTests
{
    [TestMethod]
    public void ApplySurge_SetsFinalFareAndLocksIt()
    {
        var order = Order.Create(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), 1);
        order.ApplySurge(baseFare: 20.00m, multiplier: 2.0m, reason: "time_rule");

        order.BaseFare.Should().Be(20.00m);
        order.SurgeMultiplier.Should().Be(2.0m);
        order.FinalFare.Should().Be(40.00m);
        order.SurgeReason.Should().Be("time_rule");
    }
}
