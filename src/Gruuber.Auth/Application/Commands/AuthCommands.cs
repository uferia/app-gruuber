namespace Gruuber.Auth.Application;

public record LoginCommand(string Email, string Password);
public record LoginResponse(string AccessToken, string RefreshToken, string Role);

public record RefreshCommand(string RefreshToken);
public record RefreshResponse(string AccessToken, string RefreshToken);
