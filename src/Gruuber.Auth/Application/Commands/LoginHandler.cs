using Gruuber.Auth.Application;
using Gruuber.Auth.Domain;
using Gruuber.Auth.Infrastructure;
using Gruuber.SharedKernel.Results;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace Gruuber.Auth.Application.Commands;

public class LoginHandler
{
    private readonly AuthDbContext _db;
    private readonly IJwtTokenService _jwt;
    private readonly IConfiguration _configuration;

    public LoginHandler(AuthDbContext db, IJwtTokenService jwt, IConfiguration configuration)
    {
        _db = db;
        _jwt = jwt;
        _configuration = configuration;
    }

    public async Task<ApplicationResult<LoginResponse>> HandleAsync(LoginCommand command, CancellationToken cancellationToken = default)
    {
        var user = await _db.Users
            .FirstOrDefaultAsync(u => u.Email == command.Email && u.IsActive, cancellationToken);

        if (user is null || !BCrypt.Net.BCrypt.Verify(command.Password, user.PasswordHash))
            return ApplicationResult<LoginResponse>.Failure("INVALID_CREDENTIALS", "Invalid email or password.", 401);

        var accessToken = _jwt.GenerateAccessToken(user);
        var rawRefreshToken = _jwt.GenerateRefreshToken();
        var tokenHash = JwtTokenService.HashToken(rawRefreshToken);

        var ttlDays = _configuration.GetValue<int>("Jwt:RefreshTokenTtlDays", 30);
        var refreshToken = RefreshToken.Create(user.Id, tokenHash, user.RegionId, ttlDays);
        _db.RefreshTokens.Add(refreshToken);
        await _db.SaveChangesAsync(cancellationToken);

        return ApplicationResult<LoginResponse>.Success(new LoginResponse(accessToken, rawRefreshToken, user.Role));
    }
}
