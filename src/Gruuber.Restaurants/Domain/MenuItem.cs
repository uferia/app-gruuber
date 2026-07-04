using Gruuber.SharedKernel.Domain;

namespace Gruuber.Restaurants.Domain;

public class MenuItem : EntityBase
{
    public Guid RestaurantId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string Category { get; private set; } = string.Empty;
    public decimal Price { get; private set; }
    public string Currency { get; private set; } = "USD";
    public bool IsAvailable { get; private set; } = true;

    private MenuItem() { }

    public static MenuItem Create(
        Guid restaurantId,
        string name,
        string description,
        string category,
        decimal price,
        string currency,
        int regionId)
    {
        return new MenuItem
        {
            Id = Guid.NewGuid(),
            RestaurantId = restaurantId,
            Name = name,
            Description = description,
            Category = category,
            Price = price,
            Currency = currency,
            IsAvailable = true,
            RegionId = regionId,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void Update(string name, string description, string category, decimal price, bool isAvailable)
    {
        Name = name;
        Description = description;
        Category = category;
        Price = price;
        IsAvailable = isAvailable;
        Version++;
    }
}
