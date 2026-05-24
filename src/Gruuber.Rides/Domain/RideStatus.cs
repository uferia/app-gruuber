namespace Gruuber.Rides.Domain;

public enum RideStatus
{
    Requested = 0,
    PoolQueued = 1,     // waiting in Redis queue for a pool match
    PoolMatched = 2,    // paired with another rider; driver matching pending
    Matched = 3,
    EnRoute = 4,
    PartialDropoff = 5, // Rider 1 dropped off; en route to Rider 2
    Arrived = 6,
    Completed = 7,
    Cancelled = 8
}
