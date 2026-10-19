namespace Kue.Api.Dtos.Users;

public record UserProfileDto
{
    public int Id { get; init; }
    public string Username { get; init; } = null!;
    public string Email { get; init; } = null!;
    public string? KueId { get; init; }
    public string? AvatarId { get; init; }
    public string? Bio { get; init; }
    public bool IsPrivate { get; init; }
    public IReadOnlyList<string> Roles { get; init; } = [];
    public DateTime CreatedAt { get; init; }

    /// <summary>
    /// Legal document version this user has accepted. The web app compares it
    /// with LEGAL_VERSION and re-prompts when they differ.
    /// </summary>
    public string? TermsVersion { get; init; }
}
