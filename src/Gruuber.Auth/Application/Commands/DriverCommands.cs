using System.ComponentModel.DataAnnotations;
using Gruuber.Auth.Domain;

namespace Gruuber.Auth.Application.Commands;

public record RegisterDriverCommand(
    [Required][EmailAddress] string Email,
    [Required][MinLength(8)] string Password,
    [Required][MaxLength(100)] string FirstName,
    [Required][MaxLength(100)] string LastName,
    [Required][MaxLength(20)] string PhoneNumber,
    // LTO document fields
    [Required][MaxLength(50)] string LicenseNumber,
    [Required] DateOnly LicenseExpiry,
    [Required] string DriverLicenseDocumentUrl,
    [Required] string MotorVehicleRegistrationUrl,
    [Required] string InsurancePolicyUrl,
    [Required] string NbiClearanceUrl,
    // Vehicle
    [Required][MaxLength(100)] string VehicleMake,
    [Required][MaxLength(100)] string VehicleModel,
    [Range(1900, 2100)] int VehicleYear,
    [Required][MaxLength(50)] string VehicleColor,
    [Required][MaxLength(20)] string LicensePlate,
    [Required] VehicleType VehicleType,
    [Range(1, int.MaxValue)] int RegionId,
    string? ProfilePhotoUrl = null);

public record RegisterDriverResponse(Guid UserId, Guid DriverProfileId, string ApprovalStatus);

public record ApproveDriverCommand(Guid DriverProfileId, long ExpectedVersion);
public record ApproveDriverResponse(Guid DriverProfileId, string ApprovalStatus, DateTime ApprovedAt);

public record RejectDriverCommand(Guid DriverProfileId, long ExpectedVersion, [Required] string Reason);
public record RejectDriverResponse(Guid DriverProfileId, string ApprovalStatus);
