using Gruuber.Chat.Domain;
using Gruuber.Chat.Hubs;
using Gruuber.Chat.Infrastructure;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

public class ChatHubTests
{
    private static ChatDbContext CreateInMemoryDb()
    {
        var opts = new DbContextOptionsBuilder<ChatDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString()).Options;
        return new ChatDbContext(opts);
    }

    [Fact]
    public async Task JoinThread_AddsCallerToGroup_AndMarksMessagesDelivered()
    {
        await using var db = CreateInMemoryDb();
        var threadId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        var thread = new ChatThread { ThreadId = threadId, ContextType = "ride", ContextId = Guid.NewGuid(), RegionId = 1 };
        thread.Participants.Add(new ChatParticipant { ThreadId = threadId, UserId = userId, DisplayName = "Your Rider", Role = "rider" });
        thread.Messages.Add(new ChatMessage { MessageId = Guid.NewGuid(), ThreadId = threadId, SenderId = Guid.NewGuid(), Body = "Hi", DeliveryStatus = "sent" });
        db.Threads.Add(thread);
        await db.SaveChangesAsync();

        var mockClients = new Mock<IHubCallerClients>();
        var mockGroups = new Mock<IGroupManager>();
        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(x => x.ConnectionId).Returns("conn-1");
        mockContext.Setup(x => x.UserIdentifier).Returns(userId.ToString());

        var hub = new ChatHub(db, NullLogger<ChatHub>.Instance)
        {
            Clients = mockClients.Object,
            Groups = mockGroups.Object,
            Context = mockContext.Object
        };

        await hub.JoinThread(threadId);

        mockGroups.Verify(g => g.AddToGroupAsync("conn-1", $"chat:{threadId}", default), Times.Once);

        var updated = await db.Messages.Where(m => m.ThreadId == threadId).ToListAsync();
        Assert.All(updated, m => Assert.Equal("delivered", m.DeliveryStatus));
    }

    [Fact]
    public async Task JoinThread_ThreadNotFound_ThrowsHubException()
    {
        await using var db = CreateInMemoryDb();
        var mockClients = new Mock<IHubCallerClients>();
        var mockGroups = new Mock<IGroupManager>();
        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(x => x.ConnectionId).Returns("conn-1");
        mockContext.Setup(x => x.UserIdentifier).Returns(Guid.NewGuid().ToString());

        var hub = new ChatHub(db, NullLogger<ChatHub>.Instance)
        {
            Clients = mockClients.Object,
            Groups = mockGroups.Object,
            Context = mockContext.Object
        };

        await Assert.ThrowsAsync<HubException>(() => hub.JoinThread(Guid.NewGuid()));
    }

    [Fact]
    public async Task SendMessage_ToReadOnlyThread_ThrowsHubException()
    {
        await using var db = CreateInMemoryDb();
        var threadId = Guid.NewGuid();
        var userId = Guid.NewGuid();

        db.Threads.Add(new ChatThread
        {
            ThreadId = threadId,
            ContextType = "ride",
            ContextId = Guid.NewGuid(),
            RegionId = 1,
            Status = "read_only"
        });
        await db.SaveChangesAsync();

        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(x => x.UserIdentifier).Returns(userId.ToString());

        var hub = new ChatHub(db, NullLogger<ChatHub>.Instance)
        {
            Clients = new Mock<IHubCallerClients>().Object,
            Groups = new Mock<IGroupManager>().Object,
            Context = mockContext.Object
        };

        await Assert.ThrowsAsync<HubException>(() =>
            hub.SendMessage(threadId, "Hello?", isQuickReply: false));
    }

    [Fact]
    public async Task SendMessage_ValidThread_PersistsMessageAndBroadcastsToGroup()
    {
        await using var db = CreateInMemoryDb();
        var threadId = Guid.NewGuid();
        var senderId = Guid.NewGuid();

        db.Threads.Add(new ChatThread
        {
            ThreadId = threadId,
            ContextType = "ride",
            ContextId = Guid.NewGuid(),
            RegionId = 1,
            Status = "active"
        });
        db.Participants.Add(new ChatParticipant
        {
            ThreadId = threadId, UserId = senderId,
            DisplayName = "Your Driver", Role = "driver"
        });
        await db.SaveChangesAsync();

        var mockGroupClient = new Mock<IClientProxy>();
        var mockClients = new Mock<IHubCallerClients>();
        mockClients.Setup(c => c.Group($"chat:{threadId}")).Returns(mockGroupClient.Object);
        var mockContext = new Mock<HubCallerContext>();
        mockContext.Setup(x => x.UserIdentifier).Returns(senderId.ToString());

        var hub = new ChatHub(db, NullLogger<ChatHub>.Instance)
        {
            Clients = mockClients.Object,
            Groups = new Mock<IGroupManager>().Object,
            Context = mockContext.Object
        };

        await hub.SendMessage(threadId, "On my way!", isQuickReply: false);

        var savedMsg = await db.Messages.SingleAsync(m => m.ThreadId == threadId);
        Assert.Equal("On my way!", savedMsg.Body);
        Assert.Equal(senderId, savedMsg.SenderId);

        mockGroupClient.Verify(
            c => c.SendCoreAsync("MessageReceived", It.IsAny<object[]>(), default),
            Times.Once);
    }
}
