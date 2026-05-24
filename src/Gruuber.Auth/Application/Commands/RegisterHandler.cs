using Gruuber.Auth.Application;
using Gruuber.Auth.Domain;
using Gruuber.Auth.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;

namespace Gruuber.Auth.Application.Commands;

public class RegisterHandler : ICommandHandler<RegisterCommand, RegisterResponse>
{
    private static readonly HashSet<string> ValidRoles = ["rider", "driver", "restaurant"];
    private readonly AuthDbContext _db;

    public RegisterHandler(AuthDbContext db)
    {
        _db = db;
    }

    public async Task<ApplicationResult<RegisterResponse>> HandleAsync(
        RegisterCommand command,
        CancellationToken cancellationToken = default)
    {
        if (!ValidRoles.Contains(command.Role))
            return ApplicationResult<RegisterResponse>.Failure(
                "INVALID_ROLE", $"Role must be one of: {string.Join(", ", ValidRoles)}.", 400);

        var exists = await _db.Users.AnyAsync(u => u.Email == command.Email, cancellationToken);
        if (exists)
            return ApplicationResult<RegisterResponse>.Failure(
                "EMAIL_ALREADY_EXISTS", "An account with this email already exists.", 409);

        var user = User.Create(command.Email, BCrypt.Net.BCrypt.HashPassword(command.Password), command.Role, command.RegionId);
        _db.Users.Add(user);
        await _db.SaveChangesAsync(cancellationToken);

        return ApplicationResult<RegisterResponse>.Success(new RegisterResponse(user.Id), 201);
    }
}
