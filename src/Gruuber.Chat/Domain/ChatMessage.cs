namespace Gruuber.Chat.Domain;

public class ChatMessage
{
    public Guid MessageId { get; set; } = Guid.NewGuid();
    public Guid ThreadId { get; set; }
    public Guid SenderId { get; set; }
    public string Body { get; set; } = string.Empty;
    public bool IsQuickReply { get; set; }
    public string DeliveryStatus { get; set; } = "sent";   // sent | delivered | read
    public DateTime SentAt { get; set; } = DateTime.UtcNow;

    public ChatThread Thread { get; set; } = null!;
}
