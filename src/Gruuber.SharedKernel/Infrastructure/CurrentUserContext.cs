using System.Security.Claims;
using Microsoft.AspNetCore.Http;

namespace Gruuber.SharedKernel.Infrastructure;

public class CurrentUserContext : ICurrentUserContext
{
    private readonly IHttpContextAccessor _httpContextAccessor;

    public CurrentUserContext(IHttpContextAccessor httpContextAccessor)
    {
        _httpContextAccessor = httpContextAccessor;
    }

    public Guid UserId =>
        Guid.Parse(_httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? _httpContextAccessor.HttpContext.User.FindFirstValue("sub")
            ?? throw new InvalidOperationException("UserId claim not found"));

    public string Role =>
        _httpContextAccessor.HttpContext!.User.FindFirstValue(ClaimTypes.Role)
            ?? throw new InvalidOperationException("Role claim not found");

    public int RegionId =>
        int.Parse(_httpContextAccessor.HttpContext!.User.FindFirstValue("region_id")
            ?? throw new InvalidOperationException("region_id claim not found"));
}
