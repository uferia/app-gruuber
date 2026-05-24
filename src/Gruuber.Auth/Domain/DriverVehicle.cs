using Gruuber.SharedKernel.Domain;

namespace Gruuber.Auth.Domain;

public class DriverVehicle : EntityBase
{
    public Guid DriverProfileId { get; private set; }
    public string Make { get; private set; } = string.Empty;
    public string Model { get; private set; } = string.Empty;
    public int Year { get; private set; }
    public string Color { get; private set; } = string.Empty;
    public string LicensePlate { get; private set; } = string.Empty;
    public VehicleType VehicleType { get; private set; }

    private DriverVehicle() { }

    public static DriverVehicle Create(
        Guid driverProfileId,
        string make,
        string model,
        int year,
        string color,
        string licensePlate,
        VehicleType vehicleType,
        int regionId)
    {
        return new DriverVehicle
        {
            Id = Guid.NewGuid(),
            DriverProfileId = driverProfileId,
            Make = make,
            Model = model,
            Year = year,
            Color = color,
            LicensePlate = licensePlate,
            VehicleType = vehicleType,
            RegionId = regionId,
            CreatedAt = DateTime.UtcNow
        };
    }
}
