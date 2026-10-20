using Kue.Api.Entities;

namespace Kue.Api.Dtos.Lists;

public class CustomListDto
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = null!;

    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>"public", "friends" or "private".</summary>
    public string Visibility { get; set; } = CustomListVisibility.Public;

    /// <summary>
    /// Backwards-compatible projection of <see cref="Visibility"/> for older
    /// clients. Only true when the list is public.
    /// </summary>
    public bool IsPublic { get; set; }

    public int ItemCount { get; set; }

    public List<string> PreviewCoverImages { get; set; } = [];

    public DateTime CreatedAt { get; set; }
    public DateTime? UpdatedAt { get; set; }
}
