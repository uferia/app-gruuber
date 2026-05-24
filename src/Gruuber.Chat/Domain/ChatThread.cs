namespace Gruuber.Chat.Domain;

public class ChatThread
{
    public Guid ThreadId { get; set; } = Guid.NewGuid();
    public string ContextType { get; set; } = string.Empty;   // "ride" | "order"
    public Guid ContextId { get; set; }
    public int RegionId { get; set; }
    public string Status { get; set; } = "active";             // active | read_only | closed
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ClosesAt { get; set; }

    public List<ChatParticipant> Participants { get; set; } = [];
    public List<ChatMessage> Messages { get; set; } = [];
}
