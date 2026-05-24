using Gruuber.Chat.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Chat.Infrastructure;

public class ChatDbContext : DbContext
{
    public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options) { }

    public DbSet<ChatThread> Threads => Set<ChatThread>();
    public DbSet<ChatParticipant> Participants => Set<ChatParticipant>();
    public DbSet<ChatMessage> Messages => Set<ChatMessage>();
    public DbSet<QuickReplyTemplate> QuickReplyTemplates => Set<QuickReplyTemplate>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<ChatThread>(e =>
        {
            e.ToTable("chat_threads");
            e.HasKey(t => t.ThreadId);
            e.Property(t => t.ThreadId).HasColumnName("thread_id");
            e.Property(t => t.ContextType).HasColumnName("context_type").HasMaxLength(20).IsRequired();
            e.Property(t => t.ContextId).HasColumnName("context_id").IsRequired();
            e.Property(t => t.RegionId).HasColumnName("region_id");
            e.Property(t => t.Status).HasColumnName("status").HasMaxLength(20).IsRequired();
            e.Property(t => t.CreatedAt).HasColumnName("created_at");
            e.Property(t => t.ClosesAt).HasColumnName("closes_at");
            e.HasIndex(t => new { t.ContextType, t.ContextId });
        });

        modelBuilder.Entity<ChatParticipant>(e =>
        {
            e.ToTable("chat_participants");
            e.HasKey(p => new { p.ThreadId, p.UserId });
            e.Property(p => p.ThreadId).HasColumnName("thread_id");
            e.Property(p => p.UserId).HasColumnName("user_id");
            e.Property(p => p.DisplayName).HasColumnName("display_name").HasMaxLength(100).IsRequired();
            e.Property(p => p.Role).HasColumnName("role").HasMaxLength(20).IsRequired();
            e.HasOne(p => p.Thread)
                .WithMany(t => t.Participants)
                .HasForeignKey(p => p.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(p => p.UserId);
        });

        modelBuilder.Entity<ChatMessage>(e =>
        {
            e.ToTable("chat_messages");
            e.HasKey(m => m.MessageId);
            e.Property(m => m.MessageId).HasColumnName("message_id");
            e.Property(m => m.ThreadId).HasColumnName("thread_id");
            e.Property(m => m.SenderId).HasColumnName("sender_id");
            e.Property(m => m.Body).HasColumnName("body").HasMaxLength(2000).IsRequired();
            e.Property(m => m.IsQuickReply).HasColumnName("is_quick_reply");
            e.Property(m => m.DeliveryStatus).HasColumnName("delivery_status").HasMaxLength(20).IsRequired();
            e.Property(m => m.SentAt).HasColumnName("sent_at");
            e.HasOne(m => m.Thread)
                .WithMany(t => t.Messages)
                .HasForeignKey(m => m.ThreadId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(m => new { m.ThreadId, m.SentAt });
        });

        modelBuilder.Entity<QuickReplyTemplate>(e =>
        {
            e.ToTable("quick_reply_templates");
            e.HasKey(q => q.Id);
            e.Property(q => q.Id).HasColumnName("id");
            e.Property(q => q.Role).HasColumnName("role").HasMaxLength(20).IsRequired();
            e.Property(q => q.Body).HasColumnName("body").HasMaxLength(500).IsRequired();
            e.Property(q => q.Locale).HasColumnName("locale").HasMaxLength(10).IsRequired();
            e.Property(q => q.IsActive).HasColumnName("is_active");
            e.HasIndex(q => new { q.Role, q.Locale });
        });
    }
}
