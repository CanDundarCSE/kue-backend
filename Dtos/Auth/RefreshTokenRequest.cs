namespace Kue.Api.Dtos.Auth;

// Optional: for mobile clients sending the refresh token in the body instead of a cookie
public record RefreshTokenRequest(string? RefreshToken);