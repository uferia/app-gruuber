using Confluent.Kafka;
using Gruuber.SharedKernel.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Gruuber.Api.Infrastructure.Kafka;

public class KafkaProducer : IKafkaProducer, IDisposable
{
    private readonly IProducer<string, string> _producer;
    private readonly ILogger<KafkaProducer> _logger;

    public KafkaProducer(IConfiguration configuration, ILogger<KafkaProducer> logger)
    {
        _logger = logger;
        var config = new ProducerConfig
        {
            BootstrapServers = configuration["Kafka:BootstrapServers"] ?? "localhost:9092",
            Acks = Acks.All,
            EnableIdempotence = true
        };
        _producer = new ProducerBuilder<string, string>(config).Build();
    }

    public async Task PublishAsync(string topic, string key, string payload, CancellationToken cancellationToken = default)
    {
        var message = new Message<string, string> { Key = key, Value = payload };
        var result = await _producer.ProduceAsync(topic, message, cancellationToken);
        _logger.LogInformation("Published {EventType} to {Topic} partition {Partition}",
            key, topic, result.Partition.Value);
    }

    public void Dispose() => _producer.Dispose();
}
