namespace Kue.Api.Dtos.Users;

public class PublicUserProfileDto
{
    public int Id { get; set; }
    public string Username { get; set; } = null!;
    public string? Bio { get; set; }
    public DateTime JoinedAt { get; set; }

    public UserProfileCountsDto Counts { get; set; } = new();
}

public class UserProfileCountsDto
{
    public int TotalLibraryItems { get; set; }
    public int CompletedItems { get; set; }
    public int InProgressItems { get; set; }
    public int PlanningItems { get; set; }
    public int OnHoldItems { get; set; }
    public int DroppedItems { get; set; }
    public int FavoritesCount { get; set; }

    public int ListsCount { get; set; }
    public int ReviewsCount { get; set; }
    public int RatingsCount { get; set; }
}
