namespace Kue.Api.Entities;

public class CustomList
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public string Name { get; set; } = null!;
    public string? Description { get; set; }

    /// <summary>
    /// One of <see cref="CustomListVisibility"/> ("public", "friends", "private").
    /// Replaces the old boolean IsPublic column.
    /// </summary>
    public string Visibility { get; set; } = CustomListVisibility.Public;

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<CustomListItem> Items { get; set; } = [];
}
