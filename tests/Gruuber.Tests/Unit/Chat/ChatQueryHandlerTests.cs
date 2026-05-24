using Gruuber.Chat.Application.Queries;
using Gruuber.Chat.Domain;
using Gruuber.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Xunit;

namespace Gruuber.Tests.Unit.Chat;

public class ChatQueryHandlerTests
{
    private static ChatDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new ChatDbContext(opts);
    }

    [Fact]
    public async Task GetThreadsForUser_ReturnsOnlyThreadsUserIsParticipantIn()
    {
        await using var db = CreateInMemoryDb();
        var userId = Guid.NewGuid();
        var contextId = Guid.NewGuid();

        var thread1 = new ChatThread { ContextType = "ride", ContextId = contextId, RegionId = 1 };
        thread1.Participants.Add(new ChatParticipant { ThreadId = thread1.ThreadId, UserId = userId, DisplayName = "Your Rider", Role = "rider" });

        var thread2 = new ChatThread { ContextType = "ride", ContextId = Guid.NewGuid(), RegionId = 1 };

        db.Threads.AddRange(thread1, thread2);
        await db.SaveChangesAsync();

        var handler = new ChatQueryHandler(db);
        var result = await handler.GetThreadsAsync(userId, contextId, CancellationToken.None);

        Assert.Single(result);
        Assert.Equal(thread1.ThreadId, result[0].ThreadId);
    }

    [Fact]
    public async Task GetMessages_Paginated_ReturnsOldestFirst()
    {
        await using var db = CreateInMemoryDb();
        var threadId = Guid.NewGuid();
        var senderId = Guid.NewGuid();

        db.Threads.Add(new ChatThread { ThreadId = threadId, ContextType = "ride", ContextId = Guid.NewGuid(), RegionId = 1 });
        for (int i = 0; i < 10; i++)
        {
            db.Messages.Add(new ChatMessage
            {
                ThreadId = threadId,
                SenderId = senderId,
                Body = $"Message {i}",
                SentAt = DateTime.UtcNow.AddMinutes(-10 + i)
            });
        }
        await db.SaveChangesAsync();

        var handler = new ChatQueryHandler(db);
        var result = await handler.GetMessagesAsync(threadId, page: 1, limit: 5, CancellationToken.None);

        Assert.Equal(5, result.Items.Count);
        Assert.Equal("Message 0", result.Items[0].Body);
        Assert.Equal(10, result.Total);
    }

    [Fact]
    public async Task GetQuickReplies_ReturnsOnlyMatchingRoleAndLocale()
    {
        await using var db = CreateInMemoryDb();
        db.QuickReplyTemplates.AddRange(
            new QuickReplyTemplate { Role = "driver", Body = "On my way", Locale = "en", IsActive = true },
            new QuickReplyTemplate { Role = "rider", Body = "Please hurry", Locale = "en", IsActive = true },
            new QuickReplyTemplate { Role = "driver", Body = "Estoy en camino", Locale = "es", IsActive = true }
        );
        await db.SaveChangesAsync();

        var handler = new ChatQueryHandler(db);
        var result = await handler.GetQuickRepliesAsync("driver", "en", CancellationToken.None);

        Assert.Single(result);
        Assert.Equal("On my way", result[0].Body);
    }
}
