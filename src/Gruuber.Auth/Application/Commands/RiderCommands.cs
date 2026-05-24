using System.ComponentModel.DataAnnotations;

namespace Gruuber.Auth.Application.Commands;

public record RegisterRiderCommand(
    [Required][EmailAddress] string Email,
    [Required][MinLength(8)] string Password,
    [Required][MaxLength(100)] string FirstName,
    [Required][MaxLength(100)] string LastName,
    [Required][MaxLength(20)] string PhoneNumber,
    [Range(1, int.MaxValue)] int RegionId,
    string? ProfilePhotoUrl = null);

public record RegisterRiderResponse(Guid UserId, Guid RiderProfileId);
