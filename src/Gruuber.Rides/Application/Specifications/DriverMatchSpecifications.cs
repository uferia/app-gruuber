using Gruuber.SharedKernel.Domain;

namespace Gruuber.Rides.Application.Specifications;

/// <summary>
/// Input data for driver eligibility evaluation.
/// </summary>
public record DriverCandidateContext(
    Guid DriverId,
    double Score,
    double DistanceKm,
    double Rating,
    bool IsAvailable,
    int RegionId);

/// <summary>
/// Specification — driver must be available (online and not on another ride).
/// </summary>
public sealed class DriverAvailableSpecification : ISpecification<DriverCandidateContext>
{
    public bool IsSatisfiedBy(DriverCandidateContext candidate) =>
        candidate.IsAvailable;
}

/// <summary>
/// Specification — driver must be within the search radius (default 5km).
/// </summary>
public sealed class DriverWithinRadiusSpecification(double maxDistanceKm = 5.0)
    : ISpecification<DriverCandidateContext>
{
    public bool IsSatisfiedBy(DriverCandidateContext candidate) =>
        candidate.DistanceKm <= maxDistanceKm;
}

/// <summary>
/// Specification — driver must meet the minimum rating threshold (default 3.5).
/// </summary>
public sealed class DriverMinRatingSpecification(double minRating = 3.5)
    : ISpecification<DriverCandidateContext>
{
    public bool IsSatisfiedBy(DriverCandidateContext candidate) =>
        candidate.Rating >= minRating;
}

/// <summary>
/// Composite specification for the standard driver-match eligibility check.
/// A candidate must be available, within radius, and meet the rating floor.
/// </summary>
public sealed class DriverMatchEligibilitySpecification : ISpecification<DriverCandidateContext>
{
    private readonly ISpecification<DriverCandidateContext> _composite;

    public DriverMatchEligibilitySpecification(double maxDistanceKm = 5.0, double minRating = 3.5)
    {
        _composite = new DriverAvailableSpecification()
            .And(new DriverWithinRadiusSpecification(maxDistanceKm))
            .And(new DriverMinRatingSpecification(minRating));
    }

    public bool IsSatisfiedBy(DriverCandidateContext candidate) =>
        _composite.IsSatisfiedBy(candidate);
}
