using Gruuber.SharedKernel.Domain;

namespace Gruuber.Restaurants.Domain;

public class Restaurant : EntityBase
{
    public Guid OwnerUserId { get; private set; }
    public string Name { get; private set; } = string.Empty;
    public string Description { get; private set; } = string.Empty;
    public string CuisineType { get; private set; } = string.Empty;
    public string Address { get; private set; } = string.Empty;
    public double Lat { get; private set; }
    public double Lng { get; private set; }
    public RestaurantApprovalStatus ApprovalStatus { get; private set; } = RestaurantApprovalStatus.Pending;
    public string? RejectionReason { get; private set; }
    public DateTime? ApprovedAt { get; private set; }
    public bool IsOpen { get; private set; }

    private Restaurant() { }

    public static Restaurant Create(
        Guid ownerUserId,
        string name,
        string description,
        string cuisineType,
        string address,
        double lat,
        double lng,
        int regionId)
    {
        return new Restaurant
        {
            Id = Guid.NewGuid(),
            OwnerUserId = ownerUserId,
            Name = name,
            Description = description,
            CuisineType = cuisineType,
            Address = address,
            Lat = lat,
            Lng = lng,
            ApprovalStatus = RestaurantApprovalStatus.Pending,
            IsOpen = false,
            RegionId = regionId,
            CreatedAt = DateTime.UtcNow
        };
    }

    public void UpdateProfile(string name, string description, string cuisineType, string address, double lat, double lng)
    {
        Name = name;
        Description = description;
        CuisineType = cuisineType;
        Address = address;
        Lat = lat;
        Lng = lng;
        Version++;
    }

    public void SetOpen(bool isOpen)
    {
        IsOpen = isOpen;
        Version++;
    }

    public void Approve()
    {
        ApprovalStatus = RestaurantApprovalStatus.Approved;
        ApprovedAt = DateTime.UtcNow;
        RejectionReason = null;
        Version++;
    }

    public void Reject(string reason)
    {
        ApprovalStatus = RestaurantApprovalStatus.Rejected;
        RejectionReason = reason;
        ApprovedAt = null;
        Version++;
    }
}
