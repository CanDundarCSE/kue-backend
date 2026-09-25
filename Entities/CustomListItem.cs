namespace Kue.Api.Entities;

public class CustomListItem
{
    public int Id { get; set; }

    public int ListId { get; set; }
    public CustomList List { get; set; } = null!;

    public int MediaId { get; set; }
    public Media Media { get; set; } = null!;

    public int Order { get; set; } = 0;
    public string? Notes { get; set; }

    public DateTime AddedAt { get; set; } = DateTime.UtcNow;
}
