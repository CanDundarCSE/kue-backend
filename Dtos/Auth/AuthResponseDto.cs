namespace Kue.Api.DTOs.Auth;

public record AuthResponseDto
{
    public string AccessToken { get; init; } = null!;
    public string? RefreshToken { get; init; } 
    public string TokenType { get; init; } = "Bearer";
    public int ExpiresIn { get; init; } 
    public DateTimeOffset ExpiresAt { get; init; }
    public AuthUserDto User { get; init; } = null!;
}

public record AuthUserDto
{
    public int Id { get; init; }
    public string Username { get; init; } = null!;
    public string Email { get; init; } = null!;
    public string? DisplayName { get; init; }
    public string? AvatarUrl { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
}