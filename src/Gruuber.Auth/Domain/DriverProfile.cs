using Gruuber.SharedKernel.Domain;

namespace Gruuber.Auth.Domain;

public class DriverProfile : EntityBase
{
    public Guid UserId { get; private set; }
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string PhoneNumber { get; private set; } = string.Empty;
    public string? ProfilePhotoUrl { get; private set; }

    // LTO-required documents
    public string LicenseNumber { get; private set; } = string.Empty;
    public DateOnly LicenseExpiry { get; private set; }
    public string DriverLicenseDocumentUrl { get; private set; } = string.Empty;
    public string MotorVehicleRegistrationUrl { get; private set; } = string.Empty;
    public string InsurancePolicyUrl { get; private set; } = string.Empty;
    public string NbiClearanceUrl { get; private set; } = string.Empty;

    // Approval
    public DriverApprovalStatus ApprovalStatus { get; private set; } = DriverApprovalStatus.PendingApproval;
    public string? RejectionReason { get; private set; }
    public DateTime? ApprovedAt { get; private set; }

    private DriverProfile() { }

    public static DriverProfile Create(
        Guid userId,
        string firstName,
        string lastName,
        string phoneNumber,
        string licenseNumber,
        DateOnly licenseExpiry,
        string driverLicenseDocumentUrl,
        string motorVehicleRegistrationUrl,
        string insurancePolicyUrl,
        string nbiClearanceUrl,
        int regionId,
        string? profilePhotoUrl = null)
    {
        return new DriverProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FirstName = firstName,
            LastName = lastName,
            PhoneNumber = phoneNumber,
            ProfilePhotoUrl = profilePhotoUrl,
            LicenseNumber = licenseNumber,
            LicenseExpiry = licenseExpiry,
            DriverLicenseDocumentUrl = driverLicenseDocumentUrl,
            MotorVehicleRegistrationUrl = motorVehicleRegistrationUrl,
            InsurancePolicyUrl = insurancePolicyUrl,
            NbiClearanceUrl = nbiClearanceUrl,
            ApprovalStatus = DriverApprovalStatus.PendingApproval,
            RegionId = regionId,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Approve()
    {
        ApprovalStatus = DriverApprovalStatus.Approved;
        ApprovedAt = DateTime.UtcNow;
        RejectionReason = null;
        Version++;
    }

    public void Reject(string reason)
    {
        ApprovalStatus = DriverApprovalStatus.Rejected;
        RejectionReason = reason;
        ApprovedAt = null;
        Version++;
    }
}
