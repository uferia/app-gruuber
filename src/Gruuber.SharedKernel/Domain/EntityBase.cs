namespace Gruuber.SharedKernel.Domain;

public abstract class EntityBase
{
    public Guid Id { get; protected set; } = Guid.NewGuid();
    public long Version { get; protected set; } = 1;
    public int RegionId { get; protected set; }
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
}
