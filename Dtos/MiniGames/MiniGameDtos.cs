namespace Kue.Api.Dtos.MiniGames;

public class MiniGameWalletDto
{
    public int Balance { get; set; }
    public int TotalEarned { get; set; }
}

public class SpendHintRequest
{
    public string HintType { get; set; } = null!; // "year" | "type" | "firstLetter"
}

public class SpendHintResponseDto
{
    public string HintType { get; set; } = null!;
    public int Price { get; set; }
    public int Balance { get; set; }
}

public class MiniGameRoundResult
{
    public int MediaId { get; set; }
    public int AttemptsUsed { get; set; }
    public List<string> HintsUsed { get; set; } = [];
    public int Points { get; set; }
}

public class SubmitMiniGameRequest
{
    public string Game { get; set; } = null!; // "poster-guess"
    public List<string> Scope { get; set; } = [];
    public List<MiniGameRoundResult> Rounds { get; set; } = [];
}

public class SubmitMiniGameResponseDto
{
    public int Score { get; set; }
    public int RoundsWon { get; set; }
    public int RoundsTotal { get; set; }
    public int Balance { get; set; }
    public int TotalEarned { get; set; }
}

public class LeaderboardEntryDto
{
    public int UserId { get; set; }
    public string Username { get; set; } = null!;
    public string? AvatarId { get; set; }
    public int TotalScore { get; set; }
    public int BestScore { get; set; }
    public int GamesPlayed { get; set; }
}

public class LeaderboardResponseDto
{
    public List<LeaderboardEntryDto> Entries { get; set; } = [];
    public LeaderboardEntryDto? Mine { get; set; }
}
