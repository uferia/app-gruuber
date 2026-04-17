using Gruuber.Payments.Domain;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Payments.Infrastructure;

public class PaymentsDbContext : DbContext
{
    public PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : base(options) { }

    public DbSet<Payment> Payments => Set<Payment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Payment>(e =>
        {
            e.ToTable("payments");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().IsRequired();
            e.Property(x => x.Currency).HasMaxLength(8);
            e.Property(x => x.Amount).HasPrecision(18, 4);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.RideId, x.Status });
        });

        modelBuilder.Entity<PaymentOutboxEntry>(e =>
        {
            e.ToTable("payment_outbox");
            e.HasKey(x => x.Id);
            e.Property(x => x.Payload).HasColumnType("jsonb");
        });
    }
}
