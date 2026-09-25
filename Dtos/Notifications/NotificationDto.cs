namespace Kue.Api.Dtos.Notifications;

public class NotificationDto
{
    public int Id { get; set; }
    public int UserId { get; set; }

    public int ActorId { get; set; }
    public string ActorUsername { get; set; } = null!;

    public string Type { get; set; } = null!; // "friend_request_received", "friend_request_accepted", "review_liked"
    public string Title { get; set; } = null!;
    public string Message { get; set; } = null!;

    public int? ReferenceId { get; set; }
    public string? ReferenceType { get; set; }

    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? ReadAt { get; set; }
}
