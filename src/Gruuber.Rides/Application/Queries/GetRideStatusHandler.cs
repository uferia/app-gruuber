using Gruuber.Rides.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Rides.Application.Queries;

public class GetRideStatusHandler
{
    private readonly RidesDbContext _db;

    public GetRideStatusHandler(RidesDbContext db)
    {
        _db = db;
    }

    public async Task<ApplicationResult<RideStatusResponse>> HandleAsync(
        GetRideStatusQuery query,
        CancellationToken cancellationToken = default)
    {
        var view = await _db.Set<RideView>()
            .FirstOrDefaultAsync(r => r.RideId == query.RideId, cancellationToken);

        if (view is null)
        {
            // Fall back to write model if view not yet populated
            var ride = await _db.Rides.FindAsync(new object[] { query.RideId }, cancellationToken);
            if (ride is null)
                return ApplicationResult<RideStatusResponse>.Failure("RIDE_NOT_FOUND", "Ride not found.", 404);

            return ApplicationResult<RideStatusResponse>.Success(
                new RideStatusResponse(ride.Id, ride.DriverId, string.Empty, ride.Status.ToString(), 0, 0));
        }

        return ApplicationResult<RideStatusResponse>.Success(
            new RideStatusResponse(view.RideId, view.DriverId, view.DriverName, view.Status, view.Lat, view.Lng));
    }
}
