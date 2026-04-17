namespace Gruuber.SharedKernel.Messaging;

public interface IOutboxPublisher
{
    Task PublishAsync(OutboxMessage message, CancellationToken cancellationToken = default);
}
