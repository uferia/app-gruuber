using Gruuber.SharedKernel.Results;
using Microsoft.Extensions.Logging;

namespace Gruuber.Tracking.Application.Commands;

public class UpdateDriverLocationHandler
{
    private readonly IGeoService _geoService;
    private readonly ILocationBroadcaster _broadcaster;
    private readonly ILogger<UpdateDriverLocationHandler> _logger;

    public UpdateDriverLocationHandler(
        IGeoService geoService,
        ILocationBroadcaster broadcaster,
        ILogger<UpdateDriverLocationHandler> logger)
    {
        _geoService = geoService;
        _broadcaster = broadcaster;
        _logger = logger;
    }

    public async Task<ApplicationResult<bool>> HandleAsync(
        UpdateDriverLocationCommand command,
        CancellationToken cancellationToken = default)
    {
        await _geoService.AddDriverLocationAsync(
            command.DriverId, command.Lat, command.Lng, command.RegionId, cancellationToken);

        if (command.ActiveRideId.HasValue)
        {
            await _broadcaster.BroadcastDriverLocationAsync(
                command.ActiveRideId.Value, command.DriverId, command.Lat, command.Lng, cancellationToken);
        }

        _logger.LogInformation("Driver {DriverId} location updated in region {RegionId}: ({Lat}, {Lng})",
            command.DriverId, command.RegionId, command.Lat, command.Lng);

        return ApplicationResult<bool>.Success(true);
    }
}

