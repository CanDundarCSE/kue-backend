namespace Kue.Api.Entities;

public class MiniGameScore
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Game { get; set; } = null!; // "poster-guess"
    public int Score { get; set; }
    public int RoundsWon { get; set; }
    public int RoundsTotal { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    public User User { get; set; } = null!;
}
