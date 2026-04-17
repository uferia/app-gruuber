using Gruuber.Rides.Domain;

namespace Gruuber.Rides.Domain;

public interface IRideRepository
{
    Task<Ride?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);
    Task AddAsync(Ride ride, CancellationToken cancellationToken = default);
    Task<int> UpdateWithVersionCheckAsync(Ride ride, CancellationToken cancellationToken = default);
}
