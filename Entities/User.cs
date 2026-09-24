namespace Kue.Api.Entities;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string? AvatarUrl { get; set; }
    public List<string> Roles { get; set; } = ["User"];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetTokenExpiresAt { get; set; }

    public List<RefreshToken> RefreshTokens { get; set; } = [];
    public List<LibraryEntry> LibraryEntries { get; set; } = [];
}