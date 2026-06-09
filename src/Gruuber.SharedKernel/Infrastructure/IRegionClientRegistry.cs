namespace Gruuber.SharedKernel.Infrastructure;

/// <summary>
/// Multiton pattern — a keyed singleton registry that returns the same instance
/// for a given key (regionId), creating it on first access.
/// Used for region-scoped Redis databases and Kafka producers.
/// </summary>
public interface IRegionClientRegistry<TClient>
{
    /// <summary>Returns the (lazily created) client for the given region.</summary>
    TClient GetForRegion(int regionId);

    /// <summary>Returns all currently registered region clients.</summary>
    IReadOnlyDictionary<int, TClient> All { get; }
}
