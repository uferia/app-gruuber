using Gruuber.SharedKernel.Domain;

namespace Gruuber.Auth.Domain;

public class RefreshToken : EntityBase
{
    public Guid UserId { get; private set; }
    public string TokenHash { get; private set; } = string.Empty;
    public DateTime ExpiresAt { get; private set; }
    public bool IsRevoked { get; private set; }

    private RefreshToken() { }

    public static RefreshToken Create(Guid userId, string tokenHash, int regionId, int ttlDays = 30)
    {
        return new RefreshToken
        {
            Id = Guid.NewGuid(),
            UserId = userId,
            TokenHash = tokenHash,
            ExpiresAt = DateTime.UtcNow.AddDays(ttlDays),
            IsRevoked = false,
            RegionId = regionId
        };
    }

    public void Revoke() => IsRevoked = true;
}
