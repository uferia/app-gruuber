namespace Gruuber.SharedKernel.Messaging;

public record OutboxMessage
{
    public Guid Id { get; init; } = Guid.NewGuid();
    public string EventType { get; init; } = string.Empty;
    public string Payload { get; init; } = string.Empty;
    public string Status { get; init; } = "pending";
    public int RetryCount { get; init; } = 0;
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    public DateTime? ProcessedAt { get; init; }
}
