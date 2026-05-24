namespace Gruuber.SharedKernel.Pricing;

public record SurgeResolution(
    decimal Multiplier,
    string? Reason,    // 'demand' | 'time_rule' | null
    decimal BaseFare,
    decimal FinalFare
);

public record FareEstimate(
    decimal BaseFare,
    decimal FinalFare,
    decimal? SurgeMultiplier,
    string? SurgeReason);
