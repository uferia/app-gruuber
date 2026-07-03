using Gruuber.Rides.Domain;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class RidePoolStatusTests
{
    [TestMethod]
    public void RideStatus_HasPoolStates()
    {
        _ = RideStatus.PoolQueued;
        _ = RideStatus.PoolMatched;
        _ = RideStatus.PartialDropoff;
    }

    [TestMethod]
    public void RideStatus_IntegerValues_MatchExpectedOrdinals()
    {
        // Guard against accidental reordering when enum values are persisted as ints in the DB
        ((int)RideStatus.Requested).Should().Be(0);
        ((int)RideStatus.PoolQueued).Should().Be(1);
        ((int)RideStatus.PoolMatched).Should().Be(2);
        ((int)RideStatus.Matched).Should().Be(3);
        ((int)RideStatus.EnRoute).Should().Be(4);
        ((int)RideStatus.PartialDropoff).Should().Be(5);
        ((int)RideStatus.Arrived).Should().Be(6);
        ((int)RideStatus.Completed).Should().Be(7);
        ((int)RideStatus.Cancelled).Should().Be(8);
    }
}
