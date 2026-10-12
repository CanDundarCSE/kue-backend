namespace Kue.Api.Entities;

public class ImportJob
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public User User { get; set; } = null!;

    /// <summary>mal-xml, anilist-json, trakt-json, csv.</summary>
    public string Source { get; set; } = null!;
    public string FileName { get; set; } = string.Empty;

    /// <summary>
    /// pending, running, awaiting_resolution, applying, completed, failed, cancelled.
    /// </summary>
    public string Status { get; set; } = "pending";
    public int TotalItems { get; set; }
    public int ProcessedItems { get; set; }
    public int AddedCount { get; set; }
    public int UpdatedCount { get; set; }
    public int SkippedCount { get; set; }
    public string? Error { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public List<ImportItem> Items { get; set; } = [];
}

public class ImportItem
{
    public int Id { get; set; }
    public int JobId { get; set; }
    public ImportJob Job { get; set; } = null!;

    public string Title { get; set; } = null!;
    public string? MediaType { get; set; }
    public string? ExternalSource { get; set; }
    public string? ExternalId { get; set; }

    public string? ImportedStatus { get; set; }
    public int? ImportedProgress { get; set; }
    public int? ImportedRating { get; set; }
    public string? ImportedPlatform { get; set; }
    public bool? ImportedFavorite { get; set; }

    public int? MatchedMediaId { get; set; }

    /// <summary>
    /// Serialized candidate snapshot for creating a Media row at apply time
    /// (title, cover, year, score, genres...) when no row exists yet.
    /// </summary>
    public string? CandidateJson { get; set; }

    /// <summary>
    /// Serialized [{ field, local, imported }] for conflicting fields.
    /// </summary>
    public string? ConflictJson { get; set; }

    /// <summary>
    /// pending, ready, conflict, unmatched, applied, skipped, failed.
    /// </summary>
    public string Status { get; set; } = "pending";

    /// <summary>keep, import, skip. Null until decided.</summary>
    public string? Decision { get; set; }
    public string? Error { get; set; }
}
