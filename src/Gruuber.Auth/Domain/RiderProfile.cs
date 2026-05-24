using Gruuber.SharedKernel.Domain;

namespace Gruuber.Auth.Domain;

public class RiderProfile : EntityBase
{
    public Guid UserId { get; private set; }
    public string FirstName { get; private set; } = string.Empty;
    public string LastName { get; private set; } = string.Empty;
    public string PhoneNumber { get; private set; } = string.Empty;
    public string? ProfilePhotoUrl { get; private set; }

    private RiderProfile() { }

    public static RiderProfile Create(Guid userId, string firstName, string lastName, string phoneNumber, int regionId, string? profilePhotoUrl = null)
    {
        return new RiderProfile
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            FirstName = firstName,
            LastName = lastName,
            PhoneNumber = phoneNumber,
            ProfilePhotoUrl = profilePhotoUrl,
            RegionId = regionId,
            CreatedAt = DateTime.UtcNow
        };
    }
}
