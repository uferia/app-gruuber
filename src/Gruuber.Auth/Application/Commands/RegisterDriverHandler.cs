using Gruuber.Auth.Domain;
using Gruuber.Auth.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Auth.Application.Commands;

public class RegisterDriverHandler : ICommandHandler<RegisterDriverCommand, RegisterDriverResponse>
{
    private readonly AuthDbContext _db;

    public RegisterDriverHandler(AuthDbContext db)
    {
        _db = db;
    }

    public async Task<ApplicationResult<RegisterDriverResponse>> HandleAsync(
        RegisterDriverCommand command,
        CancellationToken cancellationToken = default)
    {
        var exists = await _db.Users.AnyAsync(u => u.Email == command.Email, cancellationToken);
        if (exists)
            return ApplicationResult<RegisterDriverResponse>.Failure(
                "EMAIL_ALREADY_EXISTS", "An account with this email already exists.", 409);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var user = User.Create(command.Email, BCrypt.Net.BCrypt.HashPassword(command.Password), "driver", command.RegionId);
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        var profile = DriverProfile.Create(
            user.Id,
            command.FirstName,
            command.LastName,
            command.PhoneNumber,
            command.LicenseNumber,
            command.LicenseExpiry,
            command.DriverLicenseDocumentUrl,
            command.MotorVehicleRegistrationUrl,
            command.InsurancePolicyUrl,
            command.NbiClearanceUrl,
            command.RegionId,
            command.ProfilePhotoUrl);
        _db.DriverProfiles.Add(profile);
        await _db.SaveChangesAsync(cancellationToken);

        var vehicle = DriverVehicle.Create(
            profile.Id,
            command.VehicleMake,
            command.VehicleModel,
            command.VehicleYear,
            command.VehicleColor,
            command.LicensePlate,
            command.VehicleType,
            command.RegionId);
        _db.DriverVehicles.Add(vehicle);
        await _db.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return ApplicationResult<RegisterDriverResponse>.Success(
            new RegisterDriverResponse(user.Id, profile.Id, profile.ApprovalStatus.ToString()), 201);
    }
}
