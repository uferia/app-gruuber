using Gruuber.Chat.Application;
using Gruuber.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Gruuber.Tests.Unit.Chat;

public class ChatThreadConsumerTests
{
    private static ChatDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new ChatDbContext(opts);
    }

    [Fact]
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
        Assert.Single(threads);

        var thread = threads[0];
        Assert.Equal("ride", thread.ContextType);
        Assert.Equal(rideId, thread.ContextId);
        Assert.Equal(2, thread.Participants.Count);

        var riderParticipant = thread.Participants.FirstOrDefault(p => p.UserId == riderId);
        Assert.NotNull(riderParticipant);
        Assert.Equal("Your Rider", riderParticipant!.DisplayName);
        Assert.Equal("rider", riderParticipant.Role);

        var driverParticipant = thread.Participants.FirstOrDefault(p => p.UserId == driverId);
        Assert.NotNull(driverParticipant);
        Assert.Equal("Your Driver", driverParticipant!.DisplayName);
        Assert.Equal("driver", driverParticipant.Role);
    }

    [Fact]
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
        Assert.Equal(2, threads.Count);

        var riderDriverThread = threads.FirstOrDefault(t =>
            t.Participants.Any(p => p.Role == "driver") &&
            t.Participants.Any(p => p.Role == "rider" && p.UserId == riderId));
        Assert.NotNull(riderDriverThread);

        var riderRestaurantThread = threads.FirstOrDefault(t =>
            t.Participants.Any(p => p.Role == "restaurant") &&
            t.Participants.Any(p => p.Role == "rider" && p.UserId == riderId));
        Assert.NotNull(riderRestaurantThread);
    }

    [Fact]
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

        Assert.Single(await db.Threads.ToListAsync());
    }
}
