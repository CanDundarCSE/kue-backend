namespace Kue.Api.Entities;

public class Notification
{
    public int Id { get; set; }

    public int UserId { get; set; }
    public User User { get; set; } = null!;

    public int ActorId { get; set; }
    public User Actor { get; set; } = null!;

    public string Type { get; set; } = null!; // "friend_request_received", "friend_request_accepted", "review_liked"
    public string Title { get; set; } = null!;
    public string Message { get; set; } = null!;

    public int? ReferenceId { get; set; }
    public string? ReferenceType { get; set; } // "friendship", "review"

    public bool IsRead { get; set; } = false;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? ReadAt { get; set; }
}
