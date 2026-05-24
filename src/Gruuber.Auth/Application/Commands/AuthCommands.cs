using System.ComponentModel.DataAnnotations;

namespace Gruuber.Auth.Application;

public record LoginCommand(
    [Required][EmailAddress] string Email,
    [Required] string Password);
public record LoginResponse(string AccessToken, string RefreshToken, string Role, string? ApprovalStatus = null);

public record RefreshCommand(string RefreshToken);
public record RefreshResponse(string AccessToken, string RefreshToken);

public record RegisterCommand(
    [Required][EmailAddress] string Email,
    [Required][MinLength(8)] string Password,
    [Required] string Role,
    [Range(1, int.MaxValue)] int RegionId);

public record RegisterResponse(Guid UserId);
