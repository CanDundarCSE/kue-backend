namespace Kue.Api.Dtos.Admin;

public class AdminUserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? KueId { get; set; }
    public string? AvatarId { get; set; }
    public string? Bio { get; set; }

    public bool IsPrivate { get; set; }
    public bool IsBanned { get; set; }
    public string? BanReason { get; set; }
    public DateTime? BannedAt { get; set; }
    public List<string> Roles { get; set; } = [];
    public DateTime CreatedAt { get; set; }

    public bool IsTermsAccepted { get; set; }
    public DateTime? TermsAcceptedAt { get; set; }
    public string? TermsVersion { get; set; }

    public int GameBalance { get; set; }
    public int GameTotalEarned { get; set; }

    public int LibraryCount { get; set; }
    public int ListsCount { get; set; }
    public int ReviewsCount { get; set; }
    public int FriendsCount { get; set; }
    public int ActivitiesCount { get; set; }
    public int NotificationsCount { get; set; }
    public int ActiveSessions { get; set; }
    public DateTime? LastActivityAt { get; set; }
}
