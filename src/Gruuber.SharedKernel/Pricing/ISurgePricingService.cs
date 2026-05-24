namespace Gruuber.SharedKernel.Pricing;

public interface ISurgePricingService
{
    /// <summary>
    /// Resolves the surge multiplier for a region+type at the current moment.
    /// Never throws — returns multiplier=1.0 on any failure.
    /// </summary>
    Task<SurgeResolution> ResolveAsync(
        int regionId,
        string rideType,        // "ride" or "food"
        decimal baseFare,
        CancellationToken cancellationToken = default);
}
