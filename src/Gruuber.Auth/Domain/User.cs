using Gruuber.SharedKernel.Domain;

namespace Gruuber.Auth.Domain;

public class User : EntityBase
{
    public string Email { get; private set; } = string.Empty;
    public string PasswordHash { get; private set; } = string.Empty;
    public string Role { get; private set; } = string.Empty; // rider | driver | restaurant
    public bool IsActive { get; private set; } = true;

    private User() { }

    public static User Create(string email, string passwordHash, string role, int regionId)
        => Create(Guid.NewGuid(), email, passwordHash, role, regionId);

    public static User Create(Guid id, string email, string passwordHash, string role, int regionId)
    {
        return new User
        {
            Id = id,
            Email = email,
            PasswordHash = passwordHash,
            Role = role,
            RegionId = regionId,
            CreatedAt = DateTime.UtcNow
        };
    }
}
