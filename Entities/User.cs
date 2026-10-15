namespace Kue.Api.Entities;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;

    /// <summary>
    /// Public shareable handle (e.g. "KUE-7X2P9Q") used to find accounts.
    /// Nullable only for rows predating the feature; always assigned for new
    /// registrations and backfilled on login.
    /// </summary>
    public string? KueId { get; set; }
    public string PasswordHash { get; set; } = null!;

    /// <summary>
    /// Selected profile avatar (one of the ids in Constants/AvatarIds, served
    /// by the web app from /public/avatars). Null means the first-letter
    /// avatar — the default for rows predating the feature.
    /// </summary>
    public string? AvatarId { get; set; }

    public string? Bio { get; set; }
    public bool IsPrivate { get; set; } = false;    public bool IsBanned { get; set; } = false;
    public string? BanReason { get; set; }
    public DateTime? BannedAt { get; set; }
    public List<string> Roles { get; set; } = ["User"];
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Explicit consent to the Privacy Policy and Terms of Service, captured at
    /// registration. False for rows that predate the feature/consent.
    /// </summary>
    public bool IsTermsAccepted { get; set; } = false;

    /// <summary>
    /// UTC moment the terms were accepted. Null for rows predating the feature.
    /// </summary>
    public DateTime? TermsAcceptedAt { get; set; }

    /// <summary>
    /// Spendable mini-game points. Earned by solving rounds, spent on hints.
    /// Starts at zero, so brand-new players can't buy hints on round one.
    /// </summary>
    public int GameBalance { get; set; } = 0;

    /// <summary>
    /// Lifetime mini-game earnings. Never decreases; backs the profile stats.
    /// </summary>
    public int GameTotalEarned { get; set; } = 0;

    public string? PasswordResetToken { get; set; }
    public DateTime? PasswordResetTokenExpiresAt { get; set; }

    public List<RefreshToken> RefreshTokens { get; set; } = [];
    public List<LibraryEntry> LibraryEntries { get; set; } = [];
    public List<CustomList> CustomLists { get; set; } = [];
    public List<Friendship> SentFriendRequests { get; set; } = [];
    public List<Friendship> ReceivedFriendRequests { get; set; } = [];
    public List<Notification> Notifications { get; set; } = [];
    public List<Notification> TriggeredNotifications { get; set; } = [];
    public List<UserActivity> Activities { get; set; } = [];
    public List<Review> Reviews { get; set; } = [];
    public List<MiniGameScore> MiniGameScores { get; set; } = [];
}