namespace Kue.Api.Entities;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string PasswordHash { get; set; } = null!;
    public string? Bio { get; set; }
    public bool IsPrivate { get; set; } = false;
    public List<string> Roles { get; set; } = ["User"];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetTokenExpiresAt { get; set; }

    public List<RefreshToken> RefreshTokens { get; set; } = [];
    public List<LibraryEntry> LibraryEntries { get; set; } = [];
    public List<Review> Reviews { get; set; } = [];
    public List<ReviewLike> ReviewLikes { get; set; } = [];
    public List<CustomList> CustomLists { get; set; } = [];
    public List<Friendship> SentFriendRequests { get; set; } = [];
    public List<Friendship> ReceivedFriendRequests { get; set; } = [];
}