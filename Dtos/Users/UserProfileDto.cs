namespace Kue.Api.Dtos.Users;

public record UserProfileDto
{
    public int Id { get; init; }
    public string Username { get; init; } = null!;
    public string Email { get; init; } = null!;
    public string? AvatarUrl { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public DateTime CreatedAt { get; init; }
}
