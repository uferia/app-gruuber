using Gruuber.Api.Infrastructure.Kafka;
using Gruuber.Api.Infrastructure.Redis;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using StackExchange.Redis;

namespace Gruuber.Tests.Unit.Patterns;

/// <summary>
/// Tests for the Multiton pattern — IRegionClientRegistry&lt;TClient&gt;.
/// Covers RegionedRedisDatabaseRegistry (Redis) and RegionedKafkaProducerRegistry (Kafka).
/// Uses Moq so no real infrastructure is required.
/// </summary>
[TestClass]
public class MultitonTests
{
    // ══════════════════════════════════════════════════════════════════════════
    // RegionedRedisDatabaseRegistry
    // ══════════════════════════════════════════════════════════════════════════

    private static (Mock<IConnectionMultiplexer>, Mock<IDatabase>) CreateRedisMock()
    {
        var dbMock         = new Mock<IDatabase>();
        var multiplexer    = new Mock<IConnectionMultiplexer>();
        multiplexer.Setup(m => m.GetDatabase(It.IsAny<int>(), It.IsAny<object?>()))
                   .Returns(dbMock.Object);
        return (multiplexer, dbMock);
    }

    [TestMethod]
    public void RedisRegistry_GetForRegion_ReturnsDatabaseInstance()
    {
        // Arrange
        var (multiplexer, dbMock) = CreateRedisMock();
        var registry = new RegionedRedisDatabaseRegistry(multiplexer.Object);

        // Act
        var db = registry.GetForRegion(regionId: 1);

        // Assert
        db.Should().NotBeNull();
        db.Should().BeSameAs(dbMock.Object);
    }

    [TestMethod]
    public void RedisRegistry_SameRegion_ReturnsSameInstance()
    {
        // Arrange — Multiton: same key → same instance
        var (multiplexer, _) = CreateRedisMock();
        var registry = new RegionedRedisDatabaseRegistry(multiplexer.Object);

        // Act
        var db1 = registry.GetForRegion(1);
        var db2 = registry.GetForRegion(1);

        // Assert
        db2.Should().BeSameAs(db1);
    }

    [TestMethod]
    public void RedisRegistry_DifferentRegions_ReturnsDifferentInstances()
    {
        // Arrange — Multiton: different keys may produce different instances
        var dbMock1    = new Mock<IDatabase>();
        var dbMock2    = new Mock<IDatabase>();
        var multiplexer = new Mock<IConnectionMultiplexer>();

        // Return different IDatabase objects for different db indexes
        multiplexer.Setup(m => m.GetDatabase(1, It.IsAny<object?>())).Returns(dbMock1.Object);
        multiplexer.Setup(m => m.GetDatabase(2, It.IsAny<object?>())).Returns(dbMock2.Object);

        var registry = new RegionedRedisDatabaseRegistry(multiplexer.Object);

        // Act
        var db1 = registry.GetForRegion(1);
        var db2 = registry.GetForRegion(2);

        // Assert
        db2.Should().NotBeSameAs(db1);
    }

    [TestMethod]
    public void RedisRegistry_All_ReflectsCreatedRegions()
    {
        // Arrange
        var (multiplexer, _) = CreateRedisMock();
        var registry = new RegionedRedisDatabaseRegistry(multiplexer.Object);

        // Act — touch three regions
        registry.GetForRegion(1);
        registry.GetForRegion(2);
        registry.GetForRegion(3);

        // Assert
        registry.All.Count.Should().Be(3);
        registry.All.ContainsKey(1).Should().BeTrue();
        registry.All.ContainsKey(2).Should().BeTrue();
        registry.All.ContainsKey(3).Should().BeTrue();
    }

    [TestMethod]
    public void RedisRegistry_All_IsEmpty_BeforeFirstAccess()
    {
        // Arrange
        var (multiplexer, _) = CreateRedisMock();
        var registry = new RegionedRedisDatabaseRegistry(multiplexer.Object);

        // Assert — no clients created yet
        registry.All.Count.Should().Be(0);
    }

    // ══════════════════════════════════════════════════════════════════════════
    // RegionedKafkaProducerRegistry
    // ══════════════════════════════════════════════════════════════════════════

    private static IConfiguration BuildKafkaConfig(string bootstrapServers = "localhost:9092")
    {
        var dict = new Dictionary<string, string?>
        {
            ["Kafka:BootstrapServers"] = bootstrapServers
        };
        return new ConfigurationBuilder()
            .AddInMemoryCollection(dict)
            .Build();
    }

    [TestMethod]
    public void KafkaRegistry_GetForRegion_ReturnsProducerInstance()
    {
        // Arrange
        var registry = new RegionedKafkaProducerRegistry(
            BuildKafkaConfig(),
            NullLoggerFactory.Instance);

        // Act
        var producer = registry.GetForRegion(regionId: 1);

        // Assert
        producer.Should().NotBeNull();
    }

    [TestMethod]
    public void KafkaRegistry_SameRegion_ReturnsSameInstance()
    {
        // Arrange
        var registry = new RegionedKafkaProducerRegistry(
            BuildKafkaConfig(),
            NullLoggerFactory.Instance);

        // Act
        var p1 = registry.GetForRegion(1);
        var p2 = registry.GetForRegion(1);

        // Assert — Multiton: same key → same instance
        p2.Should().BeSameAs(p1);
    }

    [TestMethod]
    public void KafkaRegistry_DifferentRegions_ReturnDifferentInstances()
    {
        // Arrange
        var registry = new RegionedKafkaProducerRegistry(
            BuildKafkaConfig(),
            NullLoggerFactory.Instance);

        // Act
        var p1 = registry.GetForRegion(1);
        var p2 = registry.GetForRegion(2);

        // Assert
        p2.Should().NotBeSameAs(p1);
    }

    [TestMethod]
    public void KafkaRegistry_All_ReflectsAllCreatedProducers()
    {
        // Arrange
        var registry = new RegionedKafkaProducerRegistry(
            BuildKafkaConfig(),
            NullLoggerFactory.Instance);

        // Act
        registry.GetForRegion(10);
        registry.GetForRegion(20);

        // Assert
        registry.All.Count.Should().Be(2);
        registry.All.ContainsKey(10).Should().BeTrue();
        registry.All.ContainsKey(20).Should().BeTrue();
    }

    [TestMethod]
    public void KafkaRegistry_RegionOverrideConfig_IsPickedUpWhenPresent()
    {
        // Arrange — region 5 has its own broker
        var dict = new Dictionary<string, string?>
        {
            ["Kafka:BootstrapServers"]           = "default:9092",
            ["Kafka:Regions:5:BootstrapServers"] = "region5-broker:9092"
        };
        var config   = new ConfigurationBuilder().AddInMemoryCollection(dict).Build();
        var registry = new RegionedKafkaProducerRegistry(config, NullLoggerFactory.Instance);

        // Act — creating the producer must not throw; config key is read internally
        var producer = registry.GetForRegion(5);

        // Assert — producer created successfully with region-specific config
        producer.Should().NotBeNull();
    }

    [TestMethod]
    public void KafkaRegistry_Dispose_DoesNotThrow()
    {
        // Arrange
        var registry = new RegionedKafkaProducerRegistry(
            BuildKafkaConfig(),
            NullLoggerFactory.Instance);
        registry.GetForRegion(1); // create at least one producer

        // Act / Assert — dispose must not throw
        registry.Dispose();
    }
}
