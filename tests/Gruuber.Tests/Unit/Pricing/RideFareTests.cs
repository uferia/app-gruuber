using FluentAssertions;
using Gruuber.Rides.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class RideFareTests
{
    [TestMethod]
    public void Create_SetsBaseFareAndFinalFareFromSurgeResolution()
    {
        var ride = Ride.Create(Guid.NewGuid(), "solo", 1, 1.0, 1.0, 51.5, -0.1,
            baseFare: 10.00m, surgeMultiplier: 1.5m, finalFare: 15.00m, surgeReason: "demand");

        ride.BaseFare.Should().Be(10.00m);
        ride.SurgeMultiplier.Should().Be(1.5m);
        ride.FinalFare.Should().Be(15.00m);
        ride.SurgeReason.Should().Be("demand");
    }

    [TestMethod]
    public void Create_DefaultsSurgeMultiplierToOne_WhenNoSurge()
    {
        var ride = Ride.Create(Guid.NewGuid(), "solo", 1, 1.0, 1.0, 51.5, -0.1,
            baseFare: 10.00m, surgeMultiplier: 1.0m, finalFare: 10.00m, surgeReason: null);

        ride.SurgeMultiplier.Should().Be(1.0m);
        ride.SurgeReason.Should().BeNull();
    }
}
