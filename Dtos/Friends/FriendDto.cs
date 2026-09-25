namespace Kue.Api.Dtos.Friends;

public class FriendDto
{
    public int FriendshipId { get; set; }
    public int UserId { get; set; }
    public string Username { get; set; } = null!;
    public string? Bio { get; set; }
    public DateTime FriendsSince { get; set; }
}
