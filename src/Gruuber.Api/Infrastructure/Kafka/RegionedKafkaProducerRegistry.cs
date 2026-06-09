using System.Collections.Concurrent;
using Confluent.Kafka;
using Gruuber.SharedKernel.Infrastructure;
using Gruuber.SharedKernel.Messaging;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

namespace Gruuber.Api.Infrastructure.Kafka;

/// <summary>
/// Multiton (concrete) — creates and caches one IKafkaProducer per region.
/// Region-scoped producers allow per-region Kafka configuration (e.g. different brokers per region).
/// Falls back to the global bootstrap servers when no region override is configured.
/// </summary>
public sealed class RegionedKafkaProducerRegistry(
    IConfiguration configuration,
    ILoggerFactory loggerFactory)
    : IRegionClientRegistry<IKafkaProducer>, IDisposable
{
    private readonly ConcurrentDictionary<int, IKafkaProducer> _clients = new();
    private bool _disposed;

    public IKafkaProducer GetForRegion(int regionId)
    {
        return _clients.GetOrAdd(regionId, id =>
        {
            var bootstrapServers =
                configuration[$"Kafka:Regions:{id}:BootstrapServers"]
                ?? configuration["Kafka:BootstrapServers"]
                ?? "localhost:9092";

            var logger = loggerFactory.CreateLogger<RegionScopedKafkaProducer>();
            return new RegionScopedKafkaProducer(bootstrapServers, id, logger);
        });
    }

    public IReadOnlyDictionary<int, IKafkaProducer> All => _clients;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        foreach (var client in _clients.Values.OfType<IDisposable>())
            client.Dispose();
    }

    // ── Inner producer scoped to a single region ──────────────────────────────

    private sealed class RegionScopedKafkaProducer(
        string bootstrapServers,
        int regionId,
        ILogger logger) : IKafkaProducer, IDisposable
    {
        private readonly IProducer<string, string> _inner = new ProducerBuilder<string, string>(
            new ProducerConfig
            {
                BootstrapServers = bootstrapServers,
                Acks = Acks.All,
                EnableIdempotence = true
            }).Build();

        public async Task PublishAsync(string topic, string key, string payload, CancellationToken cancellationToken = default)
        {
            var message = new Message<string, string> { Key = key, Value = payload };
            var result = await _inner.ProduceAsync(topic, message, cancellationToken);
            logger.LogInformation(
                "Region {RegionId}: published {Key} to {Topic} partition {Partition}",
                regionId, key, topic, result.Partition.Value);
        }

        public void Dispose() => _inner.Dispose();
    }
}
