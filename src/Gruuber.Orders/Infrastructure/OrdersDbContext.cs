using Gruuber.Orders.Domain;
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
