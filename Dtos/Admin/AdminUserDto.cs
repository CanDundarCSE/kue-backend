namespace Kue.Api.Dtos.Admin;

public class AdminUserDto
{
    public int Id { get; set; }
    public string Username { get; set; } = null!;
    public string Email { get; set; } = null!;
    public string? Bio { get; set; }
    public bool IsPrivate { get; set; }
    public bool IsBanned { get; set; }
    public string? BanReason { get; set; }
    public DateTime? BannedAt { get; set; }
    public List<string> Roles { get; set; } = [];
    public DateTime CreatedAt { get; set; }

    public int LibraryCount { get; set; }
    public int ListsCount { get; set; }
}
