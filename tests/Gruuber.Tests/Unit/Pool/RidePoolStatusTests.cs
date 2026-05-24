using Gruuber.Rides.Domain;
using Xunit;

public class RidePoolStatusTests
{
    [Fact]
    public void RideStatus_HasPoolStates()
    {
        _ = RideStatus.PoolQueued;
        _ = RideStatus.PoolMatched;
        _ = RideStatus.PartialDropoff;
    }

    [Fact]
    public void RideStatus_IntegerValues_MatchExpectedOrdinals()
    {
        // Guard against accidental reordering when enum values are persisted as ints in the DB
        Assert.Equal(0, (int)RideStatus.Requested);
        Assert.Equal(1, (int)RideStatus.PoolQueued);
        Assert.Equal(2, (int)RideStatus.PoolMatched);
        Assert.Equal(3, (int)RideStatus.Matched);
        Assert.Equal(4, (int)RideStatus.EnRoute);
        Assert.Equal(5, (int)RideStatus.PartialDropoff);
        Assert.Equal(6, (int)RideStatus.Arrived);
        Assert.Equal(7, (int)RideStatus.Completed);
        Assert.Equal(8, (int)RideStatus.Cancelled);
    }
}
