namespace Kue.Api.Dtos.Lists;

public class CustomListDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = null!;
    public string? UserAvatarUrl { get; set; }

    public string Name { get; set; } = null!;
    public string? Description { get; set; }
    public bool IsPublic { get; set; }
    public int ItemCount { get; set; }

    public List<string> PreviewCoverImages { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
