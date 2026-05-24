using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Gruuber.Chat.Infrastructure;

public class ChatDbContextFactory : IDesignTimeDbContextFactory<ChatDbContext>
{
    public ChatDbContext CreateDbContext(string[] args)
    {
        var opts = new DbContextOptionsBuilder<ChatDbContext>()
            .UseNpgsql("Host=localhost;Database=gruuber_chat;Username=postgres;Password=postgres")
            .Options;
        return new ChatDbContext(opts);
    }
}
