using Gruuber.Restaurants.Domain;
using Gruuber.Restaurants.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Restaurants.Application.Commands;

public class RegisterRestaurantHandler
{
    private readonly RestaurantsDbContext _db;

    public RegisterRestaurantHandler(RestaurantsDbContext db)
    {
        _db = db;
    }

    public async Task<ApplicationResult<RegisterRestaurantResponse>> HandleAsync(
        RegisterRestaurantCommand command,
        CancellationToken cancellationToken = default)
    {
        var exists = await _db.Restaurants.AnyAsync(r => r.OwnerUserId == command.OwnerUserId, cancellationToken);
        if (exists)
            return ApplicationResult<RegisterRestaurantResponse>.Failure(
                "RESTAURANT_ALREADY_EXISTS", "This account already has a registered restaurant.", 409);

        var restaurant = Restaurant.Create(
            command.OwnerUserId, command.Name, command.Description, command.CuisineType,
            command.Address, command.Lat, command.Lng, command.RegionId);
        _db.Restaurants.Add(restaurant);

        try
        {
            await _db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException)
        {
            return ApplicationResult<RegisterRestaurantResponse>.Failure(
                "RESTAURANT_ALREADY_EXISTS", "This account already has a registered restaurant.", 409);
        }

        return ApplicationResult<RegisterRestaurantResponse>.Success(
            new RegisterRestaurantResponse(restaurant.Id, restaurant.ApprovalStatus.ToString()), 201);
    }
}
