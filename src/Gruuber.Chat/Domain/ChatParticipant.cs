namespace Gruuber.Chat.Domain;

public class ChatParticipant
{
    public Guid ThreadId { get; set; }
    public Guid UserId { get; set; }
    public string DisplayName { get; set; } = string.Empty;   // anonymized: "Your Driver", "Your Rider", "Restaurant Staff"
    public string Role { get; set; } = string.Empty;          // rider | driver | restaurant

    public ChatThread Thread { get; set; } = null!;
}
