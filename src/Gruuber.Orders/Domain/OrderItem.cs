namespace Gruuber.Orders.Domain;

public class OrderItem
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public Guid OrderId { get; private set; }
    public Guid MenuItemId { get; private set; }
    public int Quantity { get; private set; }
    public decimal Price { get; private set; }
    public decimal Subtotal { get; private set; }

    private OrderItem() { }

    public static OrderItem Create(Guid orderId, Guid menuItemId, int quantity, decimal price)
    {
        return new OrderItem
        {
            Id = Guid.NewGuid(),
            OrderId = orderId,
            MenuItemId = menuItemId,
            Quantity = quantity,
            Price = price,
            Subtotal = quantity * price
        };
    }
}
