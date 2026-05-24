using Gruuber.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Chat.Application.Queries;

public class ChatQueryHandler
{
    private readonly ChatDbContext _db;
    public ChatQueryHandler(ChatDbContext db) => _db = db;

    public async Task<List<ThreadSummaryResponse>> GetThreadsAsync(Guid userId, Guid? contextId, CancellationToken ct)
    {
        var query = _db.Threads
            .Include(t => t.Participants)
            .Where(t => t.Participants.Any(p => p.UserId == userId));

        if (contextId.HasValue)
            query = query.Where(t => t.ContextId == contextId.Value);

        return await query
            .OrderByDescending(t => t.CreatedAt)
            .Select(t => new ThreadSummaryResponse(
                t.ThreadId,
                t.ContextType,
                t.ContextId,
                t.Status,
                t.CreatedAt,
                t.ClosesAt,
                t.Participants
                    .Select(p => new ParticipantInfo(p.UserId, p.DisplayName, p.Role))
                    .ToList()))
            .ToListAsync(ct);
    }

    public async Task<PagedChatResponse<MessageResponse>> GetMessagesAsync(Guid threadId, int page, int limit, CancellationToken ct)
    {
        var query = _db.Messages
            .Where(m => m.ThreadId == threadId)
            .OrderBy(m => m.SentAt);

        var total = await query.CountAsync(ct);
        var items = await query.Skip((page - 1) * limit).Take(limit)
            .Select(m => new MessageResponse(m.MessageId, m.ThreadId, m.SenderId, m.Body, m.IsQuickReply, m.DeliveryStatus, m.SentAt))
            .ToListAsync(ct);

        return new PagedChatResponse<MessageResponse>(items, total, page, limit);
    }

    public async Task<List<QuickReplyResponse>> GetQuickRepliesAsync(string role, string locale, CancellationToken ct)
    {
        return await _db.QuickReplyTemplates
            .Where(q => q.Role == role && q.Locale == locale && q.IsActive)
            .Select(q => new QuickReplyResponse(q.Id, q.Body))
            .ToListAsync(ct);
    }
}

public record ThreadSummaryResponse(Guid ThreadId, string ContextType, Guid ContextId,
    string Status, DateTime CreatedAt, DateTime? ClosesAt, List<ParticipantInfo> Participants);
public record ParticipantInfo(Guid UserId, string DisplayName, string Role);
public record MessageResponse(Guid MessageId, Guid ThreadId, Guid SenderId, string Body,
    bool IsQuickReply, string DeliveryStatus, DateTime SentAt);
public record QuickReplyResponse(int Id, string Body);
public record PagedChatResponse<T>(List<T> Items, int Total, int Page, int Limit);
