using Gruuber.Chat.Application;
using Gruuber.Chat.Domain;
using Gruuber.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using FluentAssertions;
using Microsoft.VisualStudio.TestTools.UnitTesting;

[TestClass]
public class ChatThreadClosureWorkerTests
{
    private static ChatDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new ChatDbContext(opts);
    }

    [TestMethod]
    public async Task SweepExpired_MarksThreadsPassedClosesAtAsReadOnly()
    {
        await using var db = CreateInMemoryDb();

        db.Threads.Add(new ChatThread
        {
            ContextType = "ride", ContextId = Guid.NewGuid(), RegionId = 1,
            Status = "active",
            ClosesAt = DateTime.UtcNow.AddMinutes(-10)
        });

        db.Threads.Add(new ChatThread
        {
            ContextType = "ride", ContextId = Guid.NewGuid(), RegionId = 1,
            Status = "active",
            ClosesAt = DateTime.UtcNow.AddHours(10)
        });

        db.Threads.Add(new ChatThread
        {
            ContextType = "order", ContextId = Guid.NewGuid(), RegionId = 1,
            Status = "active",
            ClosesAt = null
        });

        await db.SaveChangesAsync();

        var service = new ChatThreadClosureService(db, NullLogger<ChatThreadClosureService>.Instance);
        await service.SweepAsync(CancellationToken.None);

        var threads = await db.Threads.OrderByDescending(t => t.ClosesAt).ToListAsync();
        threads[0].Status.Should().Be("active");
        threads[1].Status.Should().Be("read_only");
        threads[2].Status.Should().Be("active");
    }

    [TestMethod]
    public async Task SweepExpired_AlreadyReadOnly_NoStateChange()
    {
        await using var db = CreateInMemoryDb();

        db.Threads.Add(new ChatThread
        {
            ContextType = "ride", ContextId = Guid.NewGuid(), RegionId = 1,
            Status = "read_only",
            ClosesAt = DateTime.UtcNow.AddMinutes(-60)
        });
        await db.SaveChangesAsync();

        var service = new ChatThreadClosureService(db, NullLogger<ChatThreadClosureService>.Instance);
        await service.SweepAsync(CancellationToken.None);

        var thread = await db.Threads.SingleAsync();
        thread.Status.Should().Be("read_only");
    }
}
