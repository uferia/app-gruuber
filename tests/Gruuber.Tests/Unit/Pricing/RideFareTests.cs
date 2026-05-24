using Gruuber.Rides.Domain;
using Xunit;

public class RideFareTests
{
    [Fact]
    public void Create_SetsBaseFareAndFinalFareFromSurgeResolution()
    {
        var ride = Ride.Create(Guid.NewGuid(), "solo", 1, 1.0, 1.0, 51.5, -0.1,
            baseFare: 10.00m, surgeMultiplier: 1.5m, finalFare: 15.00m, surgeReason: "demand");

        Assert.Equal(10.00m, ride.BaseFare);
        Assert.Equal(1.5m, ride.SurgeMultiplier);
        Assert.Equal(15.00m, ride.FinalFare);
        Assert.Equal("demand", ride.SurgeReason);
    }

    [Fact]
    public void Create_DefaultsSurgeMultiplierToOne_WhenNoSurge()
    {
        var ride = Ride.Create(Guid.NewGuid(), "solo", 1, 1.0, 1.0, 51.5, -0.1,
            baseFare: 10.00m, surgeMultiplier: 1.0m, finalFare: 10.00m, surgeReason: null);

        Assert.Equal(1.0m, ride.SurgeMultiplier);
        Assert.Null(ride.SurgeReason);
    }
}
