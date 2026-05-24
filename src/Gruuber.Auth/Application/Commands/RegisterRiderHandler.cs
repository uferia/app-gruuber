using Gruuber.Auth.Domain;
using Gruuber.Auth.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Auth.Application.Commands;

public class RegisterRiderHandler : ICommandHandler<RegisterRiderCommand, RegisterRiderResponse>
{
    private readonly AuthDbContext _db;

    public RegisterRiderHandler(AuthDbContext db)
    {
        _db = db;
    }

    public async Task<ApplicationResult<RegisterRiderResponse>> HandleAsync(
        RegisterRiderCommand command,
        CancellationToken cancellationToken = default)
    {
        var exists = await _db.Users.AnyAsync(u => u.Email == command.Email, cancellationToken);
        if (exists)
            return ApplicationResult<RegisterRiderResponse>.Failure(
                "EMAIL_ALREADY_EXISTS", "An account with this email already exists.", 409);

        await using var tx = await _db.Database.BeginTransactionAsync(cancellationToken);

        var user = User.Create(command.Email, BCrypt.Net.BCrypt.HashPassword(command.Password), "rider", command.RegionId);
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        var profile = RiderProfile.Create(user.Id, command.FirstName, command.LastName, command.PhoneNumber, command.RegionId, command.ProfilePhotoUrl);
        _db.RiderProfiles.Add(profile);
        await _db.SaveChangesAsync(cancellationToken);

        await tx.CommitAsync(cancellationToken);

        return ApplicationResult<RegisterRiderResponse>.Success(new RegisterRiderResponse(user.Id, profile.Id), 201);
    }
}
