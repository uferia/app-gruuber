namespace Gruuber.Chat.Domain;

public class QuickReplyTemplate
{
    public int Id { get; set; }
    public string Role { get; set; } = string.Empty;    // rider | driver | restaurant
    public string Body { get; set; } = string.Empty;
    public string Locale { get; set; } = "en";
    public bool IsActive { get; set; } = true;
}
