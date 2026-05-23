namespace Gruuber.SharedKernel.Infrastructure;

public interface ICurrentUserContext
{
    Guid UserId { get; }
    string Role { get; }
    int RegionId { get; }
}
