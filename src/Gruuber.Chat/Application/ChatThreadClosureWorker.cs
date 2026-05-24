using Gruuber.Chat.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Gruuber.Chat.Application;

public class ChatThreadClosureWorker : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ChatThreadClosureWorker> _logger;
    private static readonly TimeSpan SweepInterval = TimeSpan.FromMinutes(5);

    public ChatThreadClosureWorker(IServiceScopeFactory scopeFactory, ILogger<ChatThreadClosureWorker> logger)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ChatDbContext>();
                var service = new ChatThreadClosureService(db, _logger);
                await service.SweepAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "ChatThreadClosureWorker sweep failed");
            }

            await Task.Delay(SweepInterval, stoppingToken);
        }
    }
}

public class ChatThreadClosureService
{
    private readonly ChatDbContext _db;
    private readonly ILogger _logger;

    public ChatThreadClosureService(ChatDbContext db, ILogger logger)
    {
        _db = db;
        _logger = logger;
    }

    public async Task SweepAsync(CancellationToken ct)
    {
        var now = DateTime.UtcNow;
        var expired = await _db.Threads
            .Where(t => t.Status == "active" && t.ClosesAt != null && t.ClosesAt <= now)
            .ToListAsync(ct);

        foreach (var thread in expired)
            thread.Status = "read_only";

        if (expired.Count > 0)
        {
            await _db.SaveChangesAsync(ct);
            _logger.LogInformation("ChatThreadClosureWorker marked {Count} threads as read_only", expired.Count);
        }
    }
}
