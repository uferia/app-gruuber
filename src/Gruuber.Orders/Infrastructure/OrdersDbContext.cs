using Gruuber.Orders.Domain;
using Gruuber.SharedKernel.Payments;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Orders.Infrastructure;

public class OrdersDbContext : DbContext
{
    public OrdersDbContext(DbContextOptions<OrdersDbContext> options) : base(options) { }

    public DbSet<Order> Orders => Set<Order>();
    public DbSet<OrderItem> OrderItems => Set<OrderItem>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Order>(e =>
        {
            e.ToTable("orders");
            e.HasKey(x => x.Id);
            e.Property(x => x.Status).HasConversion<string>().IsRequired();
            e.Property(x => x.TotalAmount).HasPrecision(18, 4);
            e.Property(x => x.Version).IsConcurrencyToken();
            e.HasIndex(x => new { x.Status, x.RegionId });
            e.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.OrderId);
            e.Property(x => x.BaseFare).HasColumnType("numeric(10,2)");
            e.Property(x => x.SurgeMultiplier).HasColumnType("numeric(6,2)").HasDefaultValue(1.0m);
            e.Property(x => x.FinalFare).HasColumnType("numeric(10,2)");
            e.Property(x => x.SurgeReason).HasMaxLength(32);
            e.Property(x => x.DeliveryFee).HasColumnType("numeric(10,2)").HasDefaultValue(0m);
            e.Property(x => x.PaymentMethod).HasConversion<string>().HasMaxLength(32).HasDefaultValue(PaymentMethod.CardMock);
            e.Property(x => x.CancellationReason).HasConversion<string>().HasMaxLength(64);
            e.Property(x => x.CancellationNote).HasMaxLength(500);
            e.Property(x => x.CancelledByRole).HasMaxLength(32);
            e.HasIndex(x => new { x.RestaurantId, x.Status });
        });

        modelBuilder.Entity<OrderItem>(e =>
        {
            e.ToTable("order_items");
            e.HasKey(x => x.Id);
            e.Property(x => x.Price).HasPrecision(18, 4);
            e.Property(x => x.Subtotal).HasPrecision(18, 4);
        });

        modelBuilder.Entity<OrderOutboxEntry>(e =>
        {
            e.ToTable("order_outbox");
            e.HasKey(x => x.Id);
            e.Property(x => x.Payload).HasColumnType("jsonb");
        });
    }
}
