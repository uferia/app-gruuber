using Gruuber.Chat.Application;
using Gruuber.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Gruuber.Tests.Unit.Chat;

[TestClass]
public class ChatThreadConsumerTests
{
    private static ChatDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new ChatDbContext(opts);
    }

    [TestMethod]
    public async Task ProcessRideMatched_CreatesOneThreadWithTwoParticipants()
    {
        await using var db = CreateInMemoryDb();
        var processor = new ChatEventProcessor(db, NullLogger<ChatEventProcessor>.Instance);

        var rideId = Guid.NewGuid();
        var riderId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var payload = $@"{{
            ""EventName"": ""ride_matched"",
            ""RideId"": ""{rideId}"",
            ""RiderId"": ""{riderId}"",
            ""DriverId"": ""{driverId}"",
            ""RegionId"": 1
        }}";

        await processor.ProcessAsync(payload, CancellationToken.None);

        var threads = await db.Threads.Include(t => t.Participants).ToListAsync();
        threads.Should().ContainSingle();

        var thread = threads[0];
        thread.ContextType.Should().Be("ride");
        thread.ContextId.Should().Be(rideId);
        thread.Participants.Count.Should().Be(2);

        var riderParticipant = thread.Participants.FirstOrDefault(p => p.UserId == riderId);
        riderParticipant.Should().NotBeNull();
        riderParticipant!.DisplayName.Should().Be("Your Rider");
        riderParticipant.Role.Should().Be("rider");

        var driverParticipant = thread.Participants.FirstOrDefault(p => p.UserId == driverId);
        driverParticipant.Should().NotBeNull();
        driverParticipant!.DisplayName.Should().Be("Your Driver");
        driverParticipant.Role.Should().Be("driver");
    }

    [TestMethod]
    public async Task ProcessOrderAccepted_CreatesTwoThreads_RiderDriverAndRiderRestaurant()
    {
        await using var db = CreateInMemoryDb();
        var processor = new ChatEventProcessor(db, NullLogger<ChatEventProcessor>.Instance);

        var orderId = Guid.NewGuid();
        var riderId = Guid.NewGuid();
        var driverId = Guid.NewGuid();
        var restaurantId = Guid.NewGuid();
        var payload = $@"{{
            ""EventName"": ""order_accepted"",
            ""OrderId"": ""{orderId}"",
            ""RiderId"": ""{riderId}"",
            ""DriverId"": ""{driverId}"",
            ""RestaurantId"": ""{restaurantId}"",
            ""RegionId"": 1
        }}";

        await processor.ProcessAsync(payload, CancellationToken.None);

        var threads = await db.Threads.Include(t => t.Participants).ToListAsync();
        threads.Count.Should().Be(2);

        var riderDriverThread = threads.FirstOrDefault(t =>
            t.Participants.Any(p => p.Role == "driver") &&
            t.Participants.Any(p => p.Role == "rider" && p.UserId == riderId));
        riderDriverThread.Should().NotBeNull();

        var riderRestaurantThread = threads.FirstOrDefault(t =>
            t.Participants.Any(p => p.Role == "restaurant") &&
            t.Participants.Any(p => p.Role == "rider" && p.UserId == riderId));
        riderRestaurantThread.Should().NotBeNull();
    }

    [TestMethod]
    public async Task OrderAccepted_WithoutDriverId_CreatesNoThreads()
    {
        await using var db = CreateInMemoryDb();
        var processor = new ChatEventProcessor(db, NullLogger<ChatEventProcessor>.Instance);

        var payload = $@"{{
            ""EventName"": ""order_accepted"",
            ""OrderId"": ""{Guid.NewGuid()}"",
            ""RiderId"": ""{Guid.NewGuid()}"",
            ""RestaurantId"": ""{Guid.NewGuid()}"",
            ""RegionId"": 1
        }}";

        await processor.ProcessAsync(payload, CancellationToken.None);

        (await db.Threads.CountAsync()).Should().Be(0);
    }

    [TestMethod]
    public async Task ProcessRideMatched_IdempotentOnDuplicateEvent_ThreadCreatedOnlyOnce()
    {
        await using var db = CreateInMemoryDb();
        var processor = new ChatEventProcessor(db, NullLogger<ChatEventProcessor>.Instance);

        var rideId = Guid.NewGuid();
        var payload = $@"{{
            ""EventName"": ""ride_matched"",
            ""RideId"": ""{rideId}"",
            ""RiderId"": ""{Guid.NewGuid()}"",
            ""DriverId"": ""{Guid.NewGuid()}"",
            ""RegionId"": 1
        }}";

        await processor.ProcessAsync(payload, CancellationToken.None);
        await processor.ProcessAsync(payload, CancellationToken.None);

        (await db.Threads.ToListAsync()).Should().ContainSingle();
    }
}
