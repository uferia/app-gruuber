using Gruuber.Auth.Domain;

namespace Gruuber.Auth.Application;

public interface IJwtTokenService
{
    string GenerateAccessToken(User user);
    string GenerateRefreshToken();
}
