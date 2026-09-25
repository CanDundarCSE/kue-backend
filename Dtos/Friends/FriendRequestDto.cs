namespace Kue.Api.Dtos.Friends;

public class FriendRequestDto
{
    public int Id { get; set; }
    public int RequesterId { get; set; }
    public string RequesterUsername { get; set; } = null!;
    public string? RequesterBio { get; set; }

    public int AddresseeId { get; set; }
    public string AddresseeUsername { get; set; } = null!;

    public string Status { get; set; } = null!; // "pending", "accepted", "declined"
    public DateTime CreatedAt { get; set; }
}
