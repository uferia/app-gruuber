using Gruuber.Chat.Domain;
using Gruuber.Chat.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Gruuber.Chat.Hubs;

[Authorize]
public class ChatHub : Hub
{
    private readonly ChatDbContext _db;
    private readonly ILogger<ChatHub> _logger;

    public ChatHub(ChatDbContext db, ILogger<ChatHub> logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task JoinThread(Guid threadId)
    {
        var thread = await _db.Threads
            .Include(t => t.Participants)
            .Include(t => t.Messages)
            .FirstOrDefaultAsync(t => t.ThreadId == threadId);

        if (thread is null)
            throw new HubException("Thread not found.");

        await Groups.AddToGroupAsync(Context.ConnectionId, $"chat:{threadId}");

        var callerId = Guid.Parse(Context.UserIdentifier!);
        var undelivered = thread.Messages
            .Where(m => m.SenderId != callerId && m.DeliveryStatus == "sent")
            .ToList();

        foreach (var msg in undelivered)
            msg.DeliveryStatus = "delivered";

        if (undelivered.Count > 0)
            await _db.SaveChangesAsync();

        _logger.LogInformation("User {UserId} joined chat thread {ThreadId}", callerId, threadId);
    }

    public async Task SendMessage(Guid threadId, string body, bool isQuickReply)
    {
        var thread = await _db.Threads
            .Include(t => t.Participants)
            .FirstOrDefaultAsync(t => t.ThreadId == threadId);

        if (thread is null)
            throw new HubException("Thread not found.");

        if (thread.Status is "read_only" or "closed")
            throw new HubException("This conversation has ended and is no longer accepting messages.");

        var senderId = Guid.Parse(Context.UserIdentifier!);
        var participant = thread.Participants.FirstOrDefault(p => p.UserId == senderId);
        if (participant is null)
            throw new HubException("You are not a participant in this thread.");

        var message = new ChatMessage
        {
            ThreadId = threadId,
            SenderId = senderId,
            Body = body,
            IsQuickReply = isQuickReply,
            DeliveryStatus = "sent",
            SentAt = DateTime.UtcNow
        };
        _db.Messages.Add(message);
        await _db.SaveChangesAsync();

        _logger.LogInformation("Message {MessageId} sent in thread {ThreadId} by {UserId}",
            message.MessageId, threadId, senderId);

        await Clients.Group($"chat:{threadId}").SendCoreAsync("MessageReceived", new object[]
        {
            new
            {
                message.MessageId,
                message.ThreadId,
                SenderDisplayName = participant.DisplayName,
                message.Body,
                message.IsQuickReply,
                message.DeliveryStatus,
                message.SentAt
            }
        });
    }

    public async Task MarkRead(Guid threadId, Guid[] messageIds)
    {
        var callerId = Guid.Parse(Context.UserIdentifier!);
        var messages = await _db.Messages
            .Where(m => m.ThreadId == threadId && messageIds.Contains(m.MessageId)
                        && m.SenderId != callerId && m.DeliveryStatus != "read")
            .ToListAsync();

        if (messages.Count == 0) return;

        foreach (var msg in messages)
            msg.DeliveryStatus = "read";

        await _db.SaveChangesAsync();

        foreach (var senderGroup in messages.GroupBy(m => m.SenderId))
        {
            var readIds = senderGroup.Select(m => m.MessageId).ToArray();
            await Clients.Group($"chat:{threadId}").SendCoreAsync("MessageRead", new object[]
            {
                new { ThreadId = threadId, MessageIds = readIds, ReadAt = DateTime.UtcNow }
            });
        }
    }
}
