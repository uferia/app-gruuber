using Gruuber.Auth.Application;
using Gruuber.Auth.Domain;
using Gruuber.Auth.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Gruuber.Auth.Application.Commands;

public class RefreshTokenHandler : ICommandHandler<RefreshCommand, RefreshResponse>
{
    private readonly AuthDbContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly IConfiguration _configuration;

    public RefreshTokenHandler(AuthDbContext db, IJwtTokenService jwt, IConfiguration configuration)
    {
        _db = db;
        _jwt = jwt;
        _configuration = configuration;
    }

    public async Task<ApplicationResult<RefreshResponse>> HandleAsync(RefreshCommand command, CancellationToken cancellationToken = default)
    {
        var tokenHash = JwtTokenService.HashToken(command.RefreshToken);

        var existing = await _db.RefreshTokens
            .FirstOrDefaultAsync(r => r.TokenHash == tokenHash && !r.IsRevoked && r.ExpiresAt > DateTime.UtcNow, cancellationToken);

        if (existing is null)
            return ApplicationResult<RefreshResponse>.Failure("INVALID_REFRESH_TOKEN", "Refresh token is invalid or expired.", 401);

        var user = await _db.Users.FindAsync(new object[] { existing.UserId }, cancellationToken);
        if (user is null || !user.IsActive)
            return ApplicationResult<RefreshResponse>.Failure("USER_NOT_FOUND", "User account is inactive.", 401);

        // Rotate: revoke old, issue new
        existing.Revoke();

        var rawNewToken = _jwt.GenerateRefreshToken();
        var newHash = JwtTokenService.HashToken(rawNewToken);
        var ttlDays = _configuration.GetValue<int>("Jwt:RefreshTokenTtlDays", 30);
        var newRefresh = RefreshToken.Create(user.Id, newHash, user.RegionId, ttlDays);

        _db.RefreshTokens.Add(newRefresh);
        await _db.SaveChangesAsync(cancellationToken);

        var accessToken = _jwt.GenerateAccessToken(user);
        return ApplicationResult<RefreshResponse>.Success(new RefreshResponse(accessToken, rawNewToken));
    }
}
